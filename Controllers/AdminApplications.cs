using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

public partial class AdminController
{
    [HttpGet]
    public async Task<IActionResult> UserDetails(string id)
    {
        var row = await accounts.Query().SingleOrDefaultAsync(u => u.Id == id);
        return row is null ? NotFound() : View(row);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AccountStatus(string id, AccountStatusInput input)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (id == users.GetUserId(User)) ModelState.AddModelError("", "You cannot change your own account access.");
        if (!ModelState.IsValid)
        {
            ViewData["StatusInput"] = input;
            return View("UserDetails", await accounts.Query().SingleAsync(u => u.Id == id));
        }
        if (user.IsDisabled == input.Disable) return RedirectToAction(nameof(UserDetails), new { id });
        await using var transaction = await db.Database.BeginTransactionAsync();
        user.IsDisabled = input.Disable;
        var result = await users.UpdateAsync(user);
        if (result.Succeeded) result = await users.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            AccountValidation.AddIdentityErrors(ModelState, result);
            return View("UserDetails", await accounts.Query().SingleAsync(u => u.Id == id));
        }
        await db.AccountSessions.Where(s => s.UserId == id && s.RevokedUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedUtc, DateTime.UtcNow));
        records.AddActivity(users.GetUserId(User)!, input.Disable ? "Disabled account" : "Reactivated account", "User", id, user.DisplayName, input.Reason.Trim());
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        TempData["Success"] = input.Disable ? "Account disabled. Existing sessions have been revoked; records are preserved." : "Account reactivated. The user can log in again.";
        return RedirectToAction(nameof(UserDetails), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Applications(string? search, string? status, int page = 1)
    {
        // Queue only the latest revision; every earlier submission stays in the detail history.
        var query = db.InstructorApplications.AsNoTracking().Where(a => !db.InstructorApplications.Any(newer => newer.UserId == a.UserId && newer.Revision > a.Revision));
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search)) query = query.Where(a => a.FullName.Contains(search) || a.WorkEmail.Contains(search));
        if (status is ApplicationStatus.Pending or ApplicationStatus.MoreInformation or ApplicationStatus.Approved or ApplicationStatus.Rejected) query = query.Where(a => a.Status == status);
        var total = await query.CountAsync(); page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 20d)));
        return View(new InstructorApplicationListModel { Search = search, Status = status, Total = total, Page = page,
            Applications = await query.OrderByDescending(a => a.SubmittedUtc).ThenByDescending(a => a.Id).Skip((page - 1) * 20).Take(20)
                .Select(a => new InstructorApplication { Id = a.Id, FullName = a.FullName, WorkEmail = a.WorkEmail, TeachingTopics = a.TeachingTopics, Revision = a.Revision, Status = a.Status, SubmittedUtc = a.SubmittedUtc, TeachingDays = a.TeachingDays }).ToListAsync() });
    }

    [HttpGet]
    public async Task<IActionResult> ReviewApplication(long id, [FromServices] InstructorApplications applicationService)
    {
        var model = await applicationService.Details(id);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewApplication(long id, InstructorReviewInput input, [FromServices] InstructorApplications applicationService)
    {
        var model = await applicationService.Details(id);
        if (model is null) return NotFound();
        if (!model.IsLatest || model.Application.Status != ApplicationStatus.Pending) return Conflict("This application has already been reviewed or replaced. Reload its current status.");
        var actorId = users.GetUserId(User)!;
        if (model.Application.UserId == actorId) return Forbid();
        if (!InstructorApplications.Decisions.Contains(input.Decision)) ModelState.AddModelError(nameof(input.Decision), "Choose a review decision.");
        if (!InstructorApplications.Categories.Contains(input.ReasonCategory)) ModelState.AddModelError(nameof(input.ReasonCategory), "Choose an eligibility reason.");
        if (input.Decision != ApplicationStatus.Approved && input.ReasonCategory == "Eligibility verified") ModelState.AddModelError(nameof(input.ReasonCategory), "Choose the eligibility issue that needs attention.");
        if (input.Decision == ApplicationStatus.Approved)
        {
            if (!(input.RelevantTeaching && input.ExperienceVerified && input.EvidenceVerified && input.DetailsConsistent))
                ModelState.AddModelError("", "Confirm all four required eligibility checks before approval.");
            if (input.VerifiedYears < 5 || input.ReasonCategory != "Eligibility verified") ModelState.AddModelError(nameof(input.VerifiedYears), "Approval requires at least five verified years and the eligibility-verified reason.");
            if (string.IsNullOrWhiteSpace(input.VerificationNotes) || input.VerificationNotes.Trim().Length < 20)
                ModelState.AddModelError(nameof(input.VerificationNotes), "Record the sources you checked and the findings (at least 20 characters).");
        }
        if (!ModelState.IsValid) { model.Review = input; return View(model); }
        await using var transaction = await db.Database.BeginTransactionAsync();
        var application = await db.InstructorApplications.SingleAsync(a => a.Id == id);
        if (application.Status != ApplicationStatus.Pending) return Conflict("Another administrator already reviewed this application.");
        var user = await users.FindByIdAsync(application.UserId);
        if (user is null || user.IsDisabled) return Conflict("The applicant is unavailable or disabled. Resolve account access before reviewing.");
        if (input.Decision == ApplicationStatus.Approved && (!await db.Students.AnyAsync(s => s.UserId == user.Id)
            || await db.Instructors.AnyAsync(s => s.UserId == user.Id) || await db.Admins.AnyAsync(s => s.UserId == user.Id)))
            return Conflict("Only a current Student can be promoted through this application.");
        application.Status = input.Decision;
        db.InstructorReviews.Add(new InstructorReview { ApplicationId = id, ReviewerId = actorId, ReviewerName = await db.Users.Where(u => u.Id == actorId).Select(u => u.DisplayName).SingleAsync(),
            Decision = input.Decision, ReasonCategory = input.ReasonCategory, Reason = input.Reason.Trim(), VerificationNotes = input.VerificationNotes?.Trim() ?? "",
            VerifiedYears = input.VerifiedYears, RelevantTeaching = input.RelevantTeaching, ExperienceVerified = input.ExperienceVerified,
            EvidenceVerified = input.EvidenceVerified, DetailsConsistent = input.DetailsConsistent });
        if (input.Decision == ApplicationStatus.Approved)
        {
            db.Students.RemoveRange(await db.Students.Where(s => s.UserId == user.Id).ToListAsync());
            db.Instructors.Add(new Instructor { UserId = user.Id });
        }
        var auditDetails = $"{input.Decision}; revision {application.Revision}. {input.Reason.Trim()}";
        records.AddActivity(actorId, "Reviewed Instructor application", "Application", id.ToString(), application.FullName,
            auditDetails.Length > 512 ? auditDetails[..512] : auditDetails);
        try
        {
            await db.SaveChangesAsync();
            await personalization.Notify(user.Id, $"review:{id}", $"Instructor application: {input.Decision}",
                input.Decision == ApplicationStatus.MoreInformation ? "More information is needed. Open your application to read the review." : "Your application has been reviewed. Open it to read the decision and reason.", $"/Education/Status/{id}");
            if (input.Decision == ApplicationStatus.Approved)
            {
                var result = await users.UpdateSecurityStampAsync(user);
                if (!result.Succeeded) throw new DbUpdateException("Unable to refresh the applicant's security stamp.");
                await db.AccountSessions.Where(s => s.UserId == user.Id && s.RevokedUtc == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedUtc, DateTime.UtcNow));
            }
            await transaction.CommitAsync();
        }
        catch (DbUpdateException) { await transaction.RollbackAsync(); return Conflict("This record changed while you were reviewing it. Reload and check the current decision."); }
        TempData["Success"] = "Decision recorded. Approval changes account access; all decisions and submitted evidence remain in the history.";
        return RedirectToAction(nameof(ReviewApplication), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> AuditLog(string? search, bool mine = false, int page = 1)
    {
        var query = db.AdminActivities.AsNoTracking(); search = search?.Trim();
        if (mine) { var actor = users.GetUserId(User)!; query = query.Where(r => r.ActorUserId == actor); }
        if (!string.IsNullOrEmpty(search)) query = query.Where(r => r.Action.Contains(search) || r.TargetName.Contains(search) || r.Details.Contains(search));
        var total = await query.CountAsync(); page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 20d)));
        var rows = await query.OrderByDescending(r => r.OccurredUtc).ThenByDescending(r => r.Id).Skip((page - 1) * 20).Take(20).ToListAsync();
        var actors = rows.Select(r => r.ActorUserId).ToList();
        return View(new AuditLogModel { Search = search, Mine = mine, Page = page, Total = total, Records = rows,
            Actors = await db.Users.Where(u => actors.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName) });
    }
}
