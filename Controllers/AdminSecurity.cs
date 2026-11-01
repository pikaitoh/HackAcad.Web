using System.Security.Claims;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

// ADMIN - Security menu and verification endpoints: investigate alerts and control account sessions.

public partial class AdminController
{
    // Display a valid verification challenge for the current administrator and browser session.
    [HttpGet]
    public async Task<IActionResult> Verify(string? ticket, [FromServices] AdminSecurity security)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
            return Challenge();
        if (!user.TwoFactorEnabled)
            return RedirectToAction("TwoFactor", "Account");
        var session = User.FindFirstValue(AccountSessions.ClaimName) ?? "";
        ticket ??= security.ChallengeTicket(user, session, "session", "/Admin/Security");
        if (security.ReadTicket(ticket, user, session) is null)
            return BadRequest("Verification expired. Return to the original page and try again.");
        ViewData["HasPassword"] = await users.HasPasswordAsync(user);
        ViewData["SmsEnabled"] = user.SmsTwoFactorEnabled;
        return View(new AdminVerifyInput { Ticket = ticket });
    }

    // Verify password/MFA, issue a scoped grant and continue the preserved operation.
    [HttpPost, EnableRateLimiting("security-action")]
    public async Task<IActionResult> Verify(AdminVerifyInput input, [FromServices] AdminSecurity security, [FromServices] AdminOperations operations,
        [FromServices] SignInManager<ApplicationUser> signIn, [FromServices] PhoneVerification phones)
    {
        var user = await users.GetUserAsync(User);
        var session = User.FindFirstValue(AccountSessions.ClaimName) ?? "";
        if (user is null || !user.TwoFactorEnabled || await users.IsLockedOutAsync(user))
            return Forbid();
        // ADMIN - A ticket binds verification to this exact Admin, browser session and requested operation.
        var challenge = security.ReadTicket(input.Ticket, user, session);
        if (challenge is null)
            return BadRequest("Verification expired. Return to the original page and try again.");
        var password = await users.HasPasswordAsync(user);
        if (password && (string.IsNullOrEmpty(input.CurrentPassword) || !(await signIn.CheckPasswordSignInAsync(user, input.CurrentPassword, true)).Succeeded))
            ModelState.AddModelError("CurrentPassword", "Enter your current HackAcad password. Too many failed attempts temporarily lock your account.");
        // ADMIN - Verify an authenticator/SMS code first, then allow an unused recovery code as a fallback.
        if (ModelState.IsValid)
        {
            var code = (input.Code ?? "").Replace(" ", "").Replace("-", "");
            var valid = false;
            if (code.Length == 6)
            {
                valid = user.SmsTwoFactorEnabled
                    ? user.PhoneNumberConfirmed && user.PhoneNumber is not null && (await phones.CheckAsync(user.Id, user.SecurityStamp ?? "", code, "admin:" + session + ":" + SecurityMonitor.Hash(input.Ticket))).Number == user.PhoneNumber
                    : await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code) && security.UseAuthenticatorCode(user, code);
            }
            if (!valid && !string.IsNullOrWhiteSpace(input.Code))
                valid = (await users.RedeemTwoFactorRecoveryCodeAsync(user, input.Code.Replace(" ", ""))).Succeeded;
            if (!valid)
            {
                await users.AccessFailedAsync(user);
                ModelState.AddModelError("Code", "Enter an unused current verification code or an unused recovery code.");
            }
        }
        // ADMIN - Never redisplay password or verification-code input after a failed attempt.
        ModelState.SetModelValue("CurrentPassword", "", "");
        ModelState.SetModelValue("Code", "", "");
        if (!ModelState.IsValid)
        {
            ViewData["HasPassword"] = password;
            ViewData["SmsEnabled"] = user.SmsTwoFactorEnabled;
            return AdminOperations.IsAsync(Request) ? AdminOperations.Errors(this.ControllerContext) : View(input);
        }
        await users.ResetAccessFailedCountAsync(user);
        await security.MarkMfaAsync(HttpContext, user);
        if (challenge.Value.Scope != "session")
            security.GrantAccess(user, session, challenge.Value.Scope);
        records.AddActivity(user.Id, "Verified Admin identity", "Admin security", user.Id, user.DisplayName, "Verification applies to " + challenge.Value.Scope + " for five minutes.");
        await db.SaveChangesAsync();
        // ADMIN - Fetch resumes in admin-operations.js; normal forms use the server-generated resume page.
        if (AdminOperations.IsAsync(Request))
            return Json(new
            {
                verified = true
            });
        if (challenge.Value.OperationId is { } operationId)
        {
            var resume = operations.Resume(operationId, user, session);
            if (resume is null)
                return Conflict("This operation expired. Return to the original page and review it again.");
            return View("ResumeOperation", resume);
        }
        TempData["Success"] = "Identity verified. You can now access the website.";
        return Url.IsLocalUrl(challenge.Value.ReturnUrl) ? LocalRedirect(challenge.Value.ReturnUrl) : RedirectToAction(nameof(Security));
    }

    // Send a rate-limited code only to the verified phone bound to this valid challenge.
    [HttpPost, EnableRateLimiting("phone-otp-send")]
    public async Task<IActionResult> SendVerifySms(AdminVerifyInput input, [FromServices] AdminSecurity security, [FromServices] PhoneVerification phones)
    {
        var user = await users.GetUserAsync(User);
        var session = User.FindFirstValue(AccountSessions.ClaimName) ?? "";
        if (user is null || !user.SmsTwoFactorEnabled || !user.TwoFactorEnabled || !user.PhoneNumberConfirmed || user.PhoneNumber is null
            || await users.IsLockedOutAsync(user) || security.ReadTicket(input.Ticket, user, session) is null)
            return Forbid();
        var error = await phones.SendAsync(user.Id, user.SecurityStamp ?? "", user.PhoneNumber, "admin:" + session + ":" + SecurityMonitor.Hash(input.Ticket));
        if (AdminOperations.IsAsync(Request))
            return error is null ? Json(new
            {
                notice = "Verification code sent to your verified phone number."
            }) : new JsonResult(new { errors = new[] { error } }) { StatusCode = 400 };
        TempData[error is null ? "Success" : "Error"] = error ?? "Verification code sent to your verified phone number.";
        return RedirectToAction(nameof(Verify), new
        {
            ticket = input.Ticket
        });
    }

    // Aggregate security counts and filter alerts by investigation state.
    [HttpGet]
    public async Task<IActionResult> Security(string? status, int page = 1, string? search = null)
    {
        if (status is not (null or "" or "open" or "New" or "Investigating" or "Resolved"))
            return BadRequest();
        search = search?.Trim();
        var query = db.SecurityAlerts.AsNoTracking();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(a => a.Title.Contains(search) || a.IpAddress.Contains(search));
        if (status == "open")
            query = query.Where(a => a.Status != "Resolved");
        else if (!string.IsNullOrEmpty(status))
            query = query.Where(a => a.Status == status);
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 20d)));
        var since = DateTime.UtcNow.AddDays(-1);
        var now = DateTimeOffset.UtcNow;
        ViewData["ReadOnlyAdmin"] = await db.Admins.AnyAsync(a => a.UserId == users.GetUserId(User) && a.IsReadOnly);
        return View(new SecurityOverviewModel
        {
            Search = search,
            Status = status,
            Page = page,
            Total = total,
            FailedLogins = await db.SecurityEvents.CountAsync(e => !e.Success && e.OccurredUtc >= since),
            LockedAccounts = await db.Users.CountAsync(u => u.LockoutEnd != null && u.LockoutEnd > now),
            OpenAlerts = await db.SecurityAlerts.CountAsync(a => a.Status != "Resolved"),
            Alerts = await query.OrderByDescending(a => a.CreatedUtc).ThenByDescending(a => a.Id).Skip((page - 1) * 20).Take(20).ToListAsync()
        });
    }

    // Load investigation details while retaining a safe return link to the filtered alert list.
    [HttpGet]
    public async Task<IActionResult> Alert(long id, string? returnUrl = null)
    {
        var alert = await db.SecurityAlerts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id);
        if (alert is null)
            return NotFound();
        ViewData["ReadOnlyAdmin"] = await db.Admins.AnyAsync(a => a.UserId == users.GetUserId(User) && a.IsReadOnly);
        ViewData["ReturnUrl"] = SecurityReturn(returnUrl);
        return View(alert);
    }

    // Validate the alert version and save investigation notes with a before/after audit record.
    [HttpPost, EnableRateLimiting("security-action")]
    public async Task<IActionResult> Alert(long id, AlertUpdateInput input)
    {
        var alert = await db.SecurityAlerts.SingleOrDefaultAsync(a => a.Id == id);
        if (alert is null)
            return NotFound();
        // ADMIN - Resolution closes the investigation, including submissions from an older browser tab.
        if (alert.Status == "Resolved")
            return Conflict("This security alert has been resolved. The investigation is read-only.");
        if (input.Version != alert.Version)
            return Conflict("This alert changed. Reload before updating it.");
        if (input.Status is not ("New" or "Investigating" or "Resolved"))
            ModelState.AddModelError("Status", "Choose a valid status.");
        ViewData["ReturnUrl"] = SecurityReturn(input.ReturnUrl);
        if (!ModelState.IsValid)
            return View(alert);
        if (alert.Status == input.Status && alert.Notes == input.Notes.Trim())
            return LocalRedirect(SecurityReturn(input.ReturnUrl));
        var before = System.Text.Json.JsonSerializer.Serialize(new
        {
            alert.Status,
            alert.Notes
        });
        alert.Status = input.Status;
        alert.Notes = input.Notes.Trim();
        alert.UpdatedUtc = DateTime.UtcNow;
        alert.Version = Guid.NewGuid().ToString("N");
        records.AddActivity(users.GetUserId(User)!, "Updated security alert", "Security alert", id.ToString(), alert.Title, "Investigation status or notes updated.", before, System.Text.Json.JsonSerializer.Serialize(new
        {
            alert.Status,
            alert.Notes
        }));
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException) { return Conflict("This alert changed. Reload before updating it."); }
        TempData["Success"] = "Investigation saved.";
        return LocalRedirect(SecurityReturn(input.ReturnUrl));
    }

    // Restrict navigation to the local Security page and its optional query string.
    private string SecurityReturn(string? path) => path is not null && Url.IsLocalUrl(path)
        && (path == "/Admin/Security" || path.StartsWith("/Admin/Security?", StringComparison.Ordinal)) ? path : "/Admin/Security";

    // List usable, unexpired sessions whose security stamp still matches the selected account.
    [HttpGet]
    public async Task<IActionResult> Sessions(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null)
            return NotFound();
        return View(new AdminSessionsModel
        {
            UserId = id,
            DisplayName = user.DisplayName,
            Sessions = await db.AccountSessions.AsNoTracking().Where(s => s.UserId == id && s.RevokedUtc == null && s.ExpiresUtc > DateTime.UtcNow && s.SecurityStamp == user.SecurityStamp).OrderByDescending(s => s.LastSeenUtc).ToListAsync()
        });
    }

    // Revoke selected browser sessions and audit the reason in the same transaction.
    [HttpPost, EnableRateLimiting("security-action")]
    public async Task<IActionResult> RevokeSessions(string id, string? sessionId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 10 or > 500)
            return BadRequest("Enter a reason between 10 and 500 characters.");
        var user = await users.FindByIdAsync(id);
        if (user is null)
            return NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var query = db.AccountSessions.Where(s => s.UserId == id && s.RevokedUtc == null && s.SecurityStamp == user.SecurityStamp && s.ExpiresUtc > DateTime.UtcNow);
        if (!string.IsNullOrEmpty(sessionId))
            query = query.Where(s => s.Id == sessionId);
        // ADMIN - Revocation is enforced on the target browser's next authenticated request, even if it is currently offline.
        var count = await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedUtc, DateTime.UtcNow));
        if (count > 0)
        {
            records.AddActivity(users.GetUserId(User)!, "Revoked user sessions", "User", id, user.DisplayName, reason.Trim(), $"Active matching sessions: {count}", "Revoked");
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        TempData["Success"] = count == 0 ? "No matching active sessions remain." : $"{count} session(s) revoked.";
        return RedirectToAction(nameof(Sessions), new { id });
    }

    // Change another Admin's auditor/manager mode while preserving an active full administrator.
    [HttpPost, EnableRateLimiting("security-action")]
    public async Task<IActionResult> AdminAccess(string id, AdminAccessInput input)
    {
        if (!ModelState.IsValid || id == users.GetUserId(User))
            return BadRequest("You cannot change your own administrative access. Provide a reason for another administrator's change.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        // ADMIN - Lock the role set so simultaneous changes cannot remove the last active manager.
        var admins = await db.Admins.FromSqlRaw("SELECT * FROM `admins` FOR UPDATE").ToListAsync();
        var actorId = users.GetUserId(User)!;
        var actorStamp = User.FindFirst("AspNet.Identity.SecurityStamp")?.Value;
        if (!admins.Any(a => a.UserId == actorId && !a.IsReadOnly)
            || !await db.Users.AsNoTracking().AnyAsync(u => u.Id == actorId && !u.IsDisabled && u.EmailConfirmed && u.TwoFactorEnabled && u.SecurityStamp == actorStamp))
            return Forbid();
        var target = admins.SingleOrDefault(a => a.UserId == id);
        if (target is null)
            return NotFound();
        if (target.IsReadOnly == input.ReadOnly)
            return RedirectToAction(nameof(UserDetails), new { id });
        if (input.ReadOnly && await db.Admins.CountAsync(a => !a.IsReadOnly && a.UserId != id && !a.User!.IsDisabled) == 0)
            return Conflict("Keep at least one active administrator with management access.");
        var user = (await users.FindByIdAsync(id))!;
        var before = target.IsReadOnly ? "Read-only auditor" : "Administrator";
        target.IsReadOnly = input.ReadOnly;
        if (!(await users.UpdateSecurityStampAsync(user)).Succeeded)
            return Conflict("The account changed. Reload and try again.");
        records.AddActivity(users.GetUserId(User)!, "Changed administrative access", "User", id, user.DisplayName, input.Reason.Trim(), before, input.ReadOnly ? "Read-only auditor" : "Administrator");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return RedirectToAction(nameof(UserDetails), new { id });
    }
}
