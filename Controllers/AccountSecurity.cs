using System.Security.Claims;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QRCoder;

namespace HackAcad.Web.Controllers;

public partial class AccountController
{
    private async Task<bool> ConfirmCurrentPassword(ApplicationUser user, string password)
    {
        if (!await users.HasPasswordAsync(user))
        {
            ModelState.AddModelError("CurrentPassword", "Set a HackAcad password in Password settings before using this security action.");
            return false;
        }
        var result = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (result.Succeeded) return true;
        ModelState.AddModelError("CurrentPassword", result.IsLockedOut
            ? "Too many failed attempts. Try again in 5 minutes."
            : "Your current password is incorrect.");
        ModelState.SetModelValue("CurrentPassword", "", "");
        return false;
    }

    [HttpGet]
    public async Task<IActionResult> LoginHistory(int page = 1)
    {
        var id = users.GetUserId(User)!;
        var query = db.LoginRecords.AsNoTracking().Where(r => r.UserId == id);
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 20d)));
        return View(new LoginHistoryModel { Page = page, Total = total,
            Records = await query.OrderByDescending(r => r.OccurredUtc).ThenByDescending(r => r.Id).Skip((page - 1) * 20).Take(20).ToListAsync() });
    }

    private async Task<SessionsModel> SessionsData(ApplicationUser user, SessionsModel? model = null)
    {
        model ??= new();
        model.CurrentSessionId = User.FindFirstValue(AccountSessions.ClaimName);
        model.Sessions = await db.AccountSessions.AsNoTracking().Where(s => s.UserId == user.Id
            && s.RevokedUtc == null && s.ExpiresUtc > DateTime.UtcNow && s.SecurityStamp == user.SecurityStamp)
            .OrderByDescending(s => s.LastSeenUtc).ToListAsync();
        return model;
    }

    [HttpGet]
    public async Task<IActionResult> Sessions()
    {
        var user = await users.GetUserAsync(User);
        return user is null ? Challenge() : View(await SessionsData(user));
    }

    [HttpPost, EnableRateLimiting("security-action")]
    public async Task<IActionResult> Sessions(SessionsModel input, string? sessionId = null)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (!ModelState.IsValid || !await ConfirmCurrentPassword(user, input.CurrentPassword))
            return View(await SessionsData(user, input));
        var currentId = User.FindFirstValue(AccountSessions.ClaimName);
        var query = db.AccountSessions.Where(s => s.UserId == user.Id && s.Id != currentId && s.RevokedUtc == null);
        if (sessionId is not null)
        {
            if (await query.Where(s => s.Id == sessionId).ExecuteUpdateAsync(set => set.SetProperty(s => s.RevokedUtc, DateTime.UtcNow)) == 0)
                return NotFound();
        }
        else
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await query.ExecuteUpdateAsync(set => set.SetProperty(s => s.RevokedUtc, DateTime.UtcNow));
            var result = await users.UpdateSecurityStampAsync(user);
            if (!result.Succeeded) { ModelState.AddModelError("", "The account changed. Reload and try again."); return View(await SessionsData(user)); }
            await transaction.CommitAsync();
            await signIn.RefreshSignInAsync(user);
        }
        TempData["Success"] = sessionId is null ? "Other devices have been signed out. You are still signed in here." : "That device has been signed out.";
        return RedirectToAction(nameof(Sessions));
    }

    [HttpGet]
    public async Task<IActionResult> TwoFactor()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        return View(await TwoFactorData(user));
    }

    private async Task<TwoFactorModel> TwoFactorData(ApplicationUser user) => new()
    { Enabled = user.TwoFactorEnabled, SmsEnabled = user.SmsTwoFactorEnabled, HasVerifiedPhone = user.PhoneNumberConfirmed && user.PhoneNumber is not null,
        RecoveryCodesLeft = await users.CountRecoveryCodesAsync(user) };

    [HttpPost, EnableRateLimiting("security-action")]
    public async Task<IActionResult> BeginAuthenticator(CurrentPasswordModel input)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (user.TwoFactorEnabled) return RedirectToAction(nameof(TwoFactor));
        if (!ModelState.IsValid || !await ConfirmCurrentPassword(user, input.CurrentPassword)) return View("TwoFactor", await TwoFactorData(user));
        var result = await users.ResetAuthenticatorKeyAsync(user);
        if (!result.Succeeded) { ModelState.AddModelError("", "Setup could not start. Reload and try again."); return View("TwoFactor", await TwoFactorData(user)); }
        securityCache.Set("totp-setup:" + user.Id, user.SecurityStamp, TimeSpan.FromMinutes(10));
        await signIn.RefreshSignInAsync(user);
        return RedirectToAction(nameof(EnableAuthenticator));
    }

    private bool HasSetupGrant(ApplicationUser user) => !user.TwoFactorEnabled
        && securityCache.TryGetValue<string>("totp-setup:" + user.Id, out var stamp) && stamp == user.SecurityStamp;

    private async Task<EnableAuthenticatorModel> AuthenticatorData(ApplicationUser user, EnableAuthenticatorModel? input = null)
    {
        input ??= new();
        var key = await users.GetAuthenticatorKeyAsync(user) ?? "";
        input.SharedKey = key;
        var uri = $"otpauth://totp/HackAcad:{Uri.EscapeDataString(user.Email!)}?secret={key}&issuer=HackAcad&digits=6";
        using var qr = QRCodeGenerator.GenerateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(qr);
        input.QrImage = "data:image/png;base64," + Convert.ToBase64String(png.GetGraphic(6));
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return input;
    }

    [HttpGet]
    public async Task<IActionResult> EnableAuthenticator()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        return HasSetupGrant(user) ? View(await AuthenticatorData(user)) : RedirectToAction(nameof(TwoFactor));
    }

    [HttpPost, EnableRateLimiting("security-action")]
    public async Task<IActionResult> EnableAuthenticator(EnableAuthenticatorModel input)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (!HasSetupGrant(user)) return RedirectToAction(nameof(TwoFactor));
        if (await users.IsLockedOutAsync(user)) ModelState.AddModelError("Code", "Too many failed attempts. Try again in 5 minutes.");
        if (ModelState.IsValid && !await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, input.Code))
        {
            await users.AccessFailedAsync(user);
            ModelState.AddModelError("Code", "That code is incorrect or has expired. Try the current code in your app.");
        }
        if (!ModelState.IsValid) return View(await AuthenticatorData(user, input));
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (!(await users.SetTwoFactorEnabledAsync(user, true)).Succeeded) return Conflict();
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))?.ToArray();
        if (codes is null || !(await users.UpdateSecurityStampAsync(user)).Succeeded) return Conflict();
        await users.ResetAccessFailedCountAsync(user);
        await transaction.CommitAsync();
        securityCache.Remove("totp-setup:" + user.Id);
        await signIn.RefreshSignInAsync(user);
        await adminSecurity.MarkMfaAsync(HttpContext, user);
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return View("RecoveryCodes", codes);
    }

    [HttpPost, EnableRateLimiting("security-action")]
    public async Task<IActionResult> ManageTwoFactor(TwoFactorModel input, string operation)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (!user.TwoFactorEnabled) return Forbid();
        if (operation != "disable" && operation != "regenerate") return BadRequest();
        // ADMIN - An administrator must keep MFA enabled even through the normal account-settings endpoint.
        if (operation == "disable" && User.IsInRole(AccountRoles.Admin)) {
            ModelState.AddModelError("", "Administrators must keep two-factor authentication enabled.");
            return View("TwoFactor", await TwoFactorData(user));
        }
        if (ModelState.IsValid && await ConfirmCurrentPassword(user, input.CurrentPassword))
        {
            var code = (input.Code ?? "").Replace(" ", "").Replace("-", "");
            var valid = code.Length == 6 && (user.SmsTwoFactorEnabled
                ? user.PhoneNumberConfirmed && user.PhoneNumber is not null && (await phones.CheckAsync(user.Id, user.SecurityStamp ?? "", code, "sms-manage")).Number == user.PhoneNumber
                : await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code));
            if (!valid) valid = !string.IsNullOrEmpty(input.Code) && (await users.RedeemTwoFactorRecoveryCodeAsync(user, input.Code.Replace(" ", ""))).Succeeded;
            if (!valid)
            {
                await users.AccessFailedAsync(user);
                ModelState.AddModelError("Code", "Enter a current verification code or an unused recovery code.");
            }
        }
        if (!ModelState.IsValid) return View("TwoFactor", await TwoFactorData(user));
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (operation == "disable")
        {
            user.SmsTwoFactorEnabled = false;
            if (!(await users.SetTwoFactorEnabledAsync(user, false)).Succeeded || !(await users.ResetAuthenticatorKeyAsync(user)).Succeeded) return Conflict();
        }
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, operation == "disable" ? 0 : 10))?.ToArray();
        if (codes is null || !(await users.UpdateSecurityStampAsync(user)).Succeeded) return Conflict();
        await users.ResetAccessFailedCountAsync(user);
        await transaction.CommitAsync();
        await signIn.ForgetTwoFactorClientAsync();
        await signIn.RefreshSignInAsync(user);
        if (operation == "regenerate") { Response.Headers["Referrer-Policy"] = "no-referrer"; return View("RecoveryCodes", codes); }
        TempData["Success"] = "Two-factor authentication has been turned off. Other devices must sign in again.";
        return RedirectToAction(nameof(TwoFactor));
    }

    private async Task BeginTwoFactorLogin(ApplicationUser user)
    {
        var request = Guid.NewGuid().ToString("N");
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, user.Id), new Claim("hackacad:pending-stamp", user.SecurityStamp ?? ""), new Claim("hackacad:sms-request", request)], IdentityConstants.TwoFactorUserIdScheme);
        await HttpContext.SignInAsync(IdentityConstants.TwoFactorUserIdScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { ExpiresUtc = clock.GetUtcNow().AddMinutes(5), IsPersistent = false });
        if (user.SmsTwoFactorEnabled && user.PhoneNumberConfirmed && user.PhoneNumber is not null)
        {
            var error = await phones.SendAsync(user.Id, user.SecurityStamp ?? "", user.PhoneNumber, "sms-login:" + request);
            if (error is not null) TempData["Error"] = error;
        }
    }

    private async Task<ApplicationUser?> PendingTwoFactorUser()
    {
        var pending = await HttpContext.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is not null && !user.IsDisabled && user.EmailConfirmed && user.TwoFactorEnabled
            && pending.Principal?.FindFirstValue("hackacad:pending-stamp") == user.SecurityStamp && !await users.IsLockedOutAsync(user)) return user;
        await HttpContext.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
        return null;
    }

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> LoginTwoFactor(bool rememberMe = false, string? returnUrl = null)
    {
        var user = await PendingTwoFactorUser();
        if (user is null) return RedirectToAction(nameof(Login));
        return user.SmsTwoFactorEnabled ? RedirectToAction(nameof(LoginSms), new { rememberMe, returnUrl }) : View(new TwoFactorLoginModel { RememberMe = rememberMe, ReturnUrl = returnUrl });
    }

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> LoginRecoveryCode(string? returnUrl = null)
        => await PendingTwoFactorUser() is null ? RedirectToAction(nameof(Login)) : View(new TwoFactorLoginModel { ReturnUrl = returnUrl });

    [AllowAnonymous, HttpPost, EnableRateLimiting("two-factor-login")]
    public Task<IActionResult> LoginTwoFactor(TwoFactorLoginModel input) => CompleteTwoFactorLogin(input, recovery: false);

    [AllowAnonymous, HttpPost, EnableRateLimiting("two-factor-login")]
    public Task<IActionResult> LoginRecoveryCode(TwoFactorLoginModel input) => CompleteTwoFactorLogin(input, recovery: true);

    private async Task<IActionResult> CompleteTwoFactorLogin(TwoFactorLoginModel input, bool recovery)
    {
        var user = await PendingTwoFactorUser();
        if (user is null) return RedirectToAction(nameof(Login));
        if (!recovery && user.SmsTwoFactorEnabled) return RedirectToAction(nameof(LoginSms), new { input.RememberMe, input.ReturnUrl });
        if (ModelState.IsValid)
        {
            var code = input.Code.Replace(" ", "");
            var result = recovery ? await signIn.TwoFactorRecoveryCodeSignInAsync(code)
                : await signIn.TwoFactorAuthenticatorSignInAsync(code.Replace("-", ""), input.RememberMe, rememberClient: false);
            if (result.Succeeded)
            {
                // ADMIN - Bind successful login MFA to the current browser session for the shared Admin request filter.
                await adminSecurity.MarkMfaAsync(HttpContext, user);
                await records.LoginAsync(user.Id, recovery ? "Signed in with recovery code" : "Signed in with 2FA");
                return Url.IsLocalUrl(input.ReturnUrl) ? LocalRedirect(input.ReturnUrl!) : RedirectToAction(nameof(Dashboard));
            }
            // Identity 8 does not count failed recovery-code guesses itself.
            if (recovery) await users.AccessFailedAsync(user);
            await records.LoginAsync(user.Id, "Verification failed");
            ModelState.AddModelError("Code", await users.IsLockedOutAsync(user) ? "Too many failed attempts. Try again in 5 minutes."
                : "That code is incorrect or has expired. Try again.");
        }
        ModelState.SetModelValue("Code", "", "");
        return View(recovery ? "LoginRecoveryCode" : "LoginTwoFactor", input);
    }
}
