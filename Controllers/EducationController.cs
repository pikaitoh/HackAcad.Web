using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class EducationController(ApplicationDbContext db, InstructorApplications applications, TimeProvider clock, Personalization personalization) : Controller
{
    [HttpGet] public async Task<IActionResult> Index()
    {
        ViewData["HasApplication"] = User.Identity?.IsAuthenticated == true && await db.InstructorApplications.AnyAsync(a => a.UserId == User.FindFirstValue(ClaimTypes.NameIdentifier));
        return View();
    }

    [Authorize(Roles = AccountRoles.Student), HttpGet]
    public async Task<IActionResult> Apply()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var previous = await db.InstructorApplications.AsNoTracking().Where(a => a.UserId == id).OrderByDescending(a => a.Revision).FirstOrDefaultAsync();
        if (previous?.Status is ApplicationStatus.Pending or ApplicationStatus.Approved) return RedirectToAction(nameof(Status));
        var user = await db.Users.FindAsync(id);
        var model = new InstructorApplicationInput { FullName = user!.FullName ?? "", WorkEmail = user.Email ?? "" };
        if (previous is not null)
        {
            model.FullName = previous.FullName; model.WorkEmail = previous.WorkEmail; model.TeachingTopics = previous.TeachingTopics;
            model.Experience = InstructorApplications.ReadExperience(previous.ExperienceJson); model.SupportingInformation = previous.SupportingInformation;
        }
        return View(model);
    }

    [Authorize(Roles = AccountRoles.Student), HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Apply(InstructorApplicationInput input)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var previous = await db.InstructorApplications.Where(a => a.UserId == id).OrderByDescending(a => a.Revision).FirstOrDefaultAsync();
        if (previous?.Status is ApplicationStatus.Pending or ApplicationStatus.Approved) return RedirectToAction(nameof(Status));
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        input.Experience ??= [];
        for (var i = 0; i < input.Experience.Count; i++)
        {
            var item = input.Experience[i];
            if (item.StartDate is null || item.StartDate < new DateOnly(1950, 1, 1) || item.StartDate > today)
                ModelState.AddModelError($"Experience[{i}].StartDate", "Enter a start date between 1950 and today.");
            if (item.EndDate > today || item.EndDate < item.StartDate)
                ModelState.AddModelError($"Experience[{i}].EndDate", "Enter an end date on or after the start, and no later than today.");
        }
        var days = ModelState.IsValid ? InstructorApplications.TeachingDays(input.Experience, today) : 0;
        if (ModelState.IsValid && days < InstructorApplications.MinimumDays(today))
            ModelState.AddModelError(nameof(input.Experience), "At least five years of relevant teaching experience is required. Overlapping dates count only once.");
        byte[] evidence = [];
        if (input.Evidence is not { Length: > 0 and <= 5 * 1024 * 1024 } || !string.Equals(Path.GetExtension(input.Evidence.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError(nameof(input.Evidence), "Upload a PDF containing verifiable teaching evidence, up to 5 MB.");
        else
        {
            await using var stream = input.Evidence.OpenReadStream(); using var memory = new MemoryStream(); await stream.CopyToAsync(memory);
            evidence = memory.ToArray();
            if (evidence.Length < 5 || Encoding.ASCII.GetString(evidence, 0, 5) != "%PDF-") ModelState.AddModelError(nameof(input.Evidence), "Upload a valid PDF document.");
        }
        if (JsonSerializer.Serialize(input.Experience).Length > 6000) ModelState.AddModelError(nameof(input.Experience), "Shorten the teaching history details to fit the evidence form.");
        if (!ModelState.IsValid) return View(input);
        db.InstructorApplications.Add(new InstructorApplication { UserId = id, Revision = (previous?.Revision ?? 0) + 1,
            FullName = input.FullName.Trim(), WorkEmail = input.WorkEmail.Trim(), TeachingTopics = input.TeachingTopics.Trim(),
            ExperienceJson = JsonSerializer.Serialize(input.Experience), SupportingInformation = input.SupportingInformation?.Trim() ?? "",
            SubmissionNote = input.SubmissionNote?.Trim() ?? "", EvidencePdf = evidence, EvidenceName = "teaching-evidence.pdf", TeachingDays = days,
            SubmittedUtc = clock.GetUtcNow().UtcDateTime });
        await using var transaction = await db.Database.BeginTransactionAsync();
        try {
            await db.SaveChangesAsync();
            var submitted = await db.InstructorApplications.Where(a => a.UserId == id).OrderByDescending(a => a.Revision).Select(a => a.Id).FirstAsync();
            await personalization.NotifyAdmins(submitted, input.FullName.Trim(), previous is not null);
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            if (await db.InstructorApplications.AnyAsync(a => a.UserId == id && a.Revision == (previous == null ? 1 : previous.Revision + 1)))
                return RedirectToAction(nameof(Status));
            throw;
        }
        TempData["Success"] = "Application submitted. You remain a Student while an administrator reviews your evidence.";
        return RedirectToAction(nameof(Status));
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> Status(long? id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var applicationId = id ?? await db.InstructorApplications.Where(a => a.UserId == userId).OrderByDescending(a => a.Revision).Select(a => (long?)a.Id).FirstOrDefaultAsync();
        if (applicationId is null) { TempData["Success"] = "You have not submitted an Instructor application yet."; return RedirectToAction(nameof(Apply)); }
        var model = await applications.Details(applicationId.Value);
        if (model is null || model.Application.UserId != userId) return NotFound();
        return View(model);
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> Evidence(long id)
    {
        var application = await db.InstructorApplications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id);
        if (application is null || (application.UserId != User.FindFirstValue(ClaimTypes.NameIdentifier) && !User.IsInRole(AccountRoles.Admin))) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(application.EvidencePdf, "application/pdf", application.EvidenceName);
    }
}
