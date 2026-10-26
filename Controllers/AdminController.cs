using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

[Authorize(Roles = AccountRoles.Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public partial class AdminController(ApplicationDbContext db, UserManager<ApplicationUser> users,
    UserAccounts accounts, AccountRecords records, UserTimeZone userTime, Personalization personalization) : Controller
{
    public async Task<IActionResult> Users(string? search, string? role, string? status, int page = 1)
    {
        var query = accounts.Query();
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search)) query = query.Where(u => u.DisplayName.Contains(search) || u.Email.Contains(search));
        if (AccountRoles.All.Contains(role)) query = query.Where(u => u.Role == role);
        if (status == "active") query = query.Where(u => !u.IsDisabled);
        if (status == "disabled") query = query.Where(u => u.IsDisabled);
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 20d)));
        return View(new UserListModel { Search = search, Role = role, Status = status, Total = total, Page = page,
            Users = await query.OrderByDescending(u => u.JoinedDate).ThenBy(u => u.Id).Skip((page - 1) * 20).Take(20).ToListAsync() });
    }

    [HttpGet]
    public async Task<IActionResult> LoginLogs(string? search, string? result, DateOnly? from, DateOnly? to, int page = 1)
    {
        var model = new LoginLogsModel { Search = search?.Trim(), Result = result, From = from, To = to };
        if (from > to) ModelState.AddModelError("To", "Choose an end date on or after the start date.");
        DateTime? start = null, end = null;
        try { if (from.HasValue) start = userTime.StartOfDayUtc(from.Value); }
        catch (ArgumentException) { ModelState.AddModelError("From", "Choose a valid start date."); }
        try { if (to.HasValue) end = userTime.StartOfDayUtc(to.Value.AddDays(1)); }
        catch (ArgumentException) { ModelState.AddModelError("To", "Choose a valid end date before 31 Dec 9999."); }
        if (!ModelState.IsValid) return View(model);
        var query = from record in db.LoginRecords.AsNoTracking()
                    join user in db.Users on record.UserId equals user.Id
                    select new LoginLogRow { Id = record.Id, UserId = user.Id, Username = user.DisplayName,
                        Email = user.Email ?? "", OccurredUtc = record.OccurredUtc, Outcome = record.Outcome,
                        Device = record.Device, IpAddress = record.IpAddress };
        if (!string.IsNullOrEmpty(model.Search))
            query = query.Where(r => r.Username.Contains(model.Search) || r.Email.Contains(model.Search));
        if (result == "success") query = query.Where(r => r.Outcome.StartsWith("Signed in"));
        if (result == "failed") query = query.Where(r => r.Outcome == "Failed" || r.Outcome == "Verification failed");
        if (result == "locked") query = query.Where(r => r.Outcome == "Locked out");
        if (start.HasValue) query = query.Where(r => r.OccurredUtc >= start.Value);
        if (end.HasValue) query = query.Where(r => r.OccurredUtc < end.Value);
        model.Total = await query.CountAsync();
        model.Page = Math.Clamp(page, 1, model.PageCount);
        model.Records = await query.OrderByDescending(r => r.OccurredUtc).ThenByDescending(r => r.Id)
            .Skip((model.Page - 1) * 20).Take(20).ToListAsync();
        return View(model);
    }

    public async Task<IActionResult> Courses(string? search, string? status, int page = 1)
    {
        var query = from c in db.Courses.AsNoTracking()
                    join instructor in db.Users on c.InstructorId equals instructor.Id
                    select new CourseRow { CourseId = c.CourseId, Title = c.Title, Category = c.Category,
                        IsPublished = c.IsPublished, ReviewStatus = c.ReviewStatus, Instructor = instructor.DisplayName,
                        Enrollments = db.Enrollments.Count(e => e.CourseId == c.CourseId) };
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search)) query = query.Where(c => c.Title.Contains(search) || c.Instructor.Contains(search));
        if (status == "published") query = query.Where(c => c.IsPublished);
        if (status == "draft") query = query.Where(c => !c.IsPublished);
        if (status is CourseReviewStatus.Draft or CourseReviewStatus.Pending or CourseReviewStatus.Approved or CourseReviewStatus.Rejected)
            query = query.Where(c => c.ReviewStatus == status);
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 20d)));
        return View(new CourseListModel { Search = search, Status = status, Page = page, Total = total,
            Courses = await query.OrderBy(c => c.Title).ThenBy(c => c.CourseId).Skip((page - 1) * 20).Take(20).ToListAsync() });
    }

    [HttpGet]
    public async Task<IActionResult> EditCourse(int id)
    {
        var course = await db.Courses.AsNoTracking().SingleOrDefaultAsync(c => c.CourseId == id);
        if (course is null) return NotFound();
        ViewData["OriginalCourse"] = course;
        return View(new EditCourseModel { CourseId = id, Title = course.Title, Description = course.Description,
            Category = course.Category, IsPublished = course.IsPublished, Instructor = await InstructorName(course.InstructorId) });
    }

    [HttpPost]
    public async Task<IActionResult> EditCourse(int id, EditCourseModel input)
    {
        var course = await db.Courses.SingleOrDefaultAsync(c => c.CourseId == id);
        if (course is null) return NotFound();
        input.CourseId = id;
        input.Instructor = await InstructorName(course.InstructorId);
        ViewData["OriginalCourse"] = course;
        if (input.IsPublished && course.ReviewStatus != CourseReviewStatus.Approved)
            ModelState.AddModelError(nameof(input.IsPublished), "Approve the submitted course before publishing it.");
        if (!ModelState.IsValid) return View(input);
        static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        input.Title = input.Title.Trim();
        input.Description = Optional(input.Description);
        input.Category = Optional(input.Category);
        if (course.Title.Trim() == input.Title && Optional(course.Description) == input.Description
            && Optional(course.Category) == input.Category && course.IsPublished == input.IsPublished)
        {
            ViewData["NoChanges"] = "No changes to save.";
            return View(input);
        }
        await using var transaction = await db.Database.BeginTransactionAsync();
        var wasPublished = course.IsPublished;
        var contentChanged = course.Title.Trim() != input.Title || Optional(course.Description) != input.Description || Optional(course.Category) != input.Category;
        var before = System.Text.Json.JsonSerializer.Serialize(new { course.Title, course.Description, course.Category, course.IsPublished, course.ReviewStatus });
        course.Title = input.Title.Trim();
        course.Description = input.Description?.Trim();
        course.Category = input.Category?.Trim();
        course.IsPublished = input.IsPublished && !contentChanged;
        if (contentChanged) course.ReviewStatus = CourseReviewStatus.Draft;
        var publicationChanged = wasPublished != course.IsPublished;
        records.AddActivity(users.GetUserId(User)!, "Updated course", "Course", course.CourseId.ToString(), course.Title,
            contentChanged ? "Course details changed; new approval required." : publicationChanged ? (course.IsPublished ? "Course published" : "Course unpublished") : "Course details changed", before, System.Text.Json.JsonSerializer.Serialize(new { course.Title, course.Description, course.Category, course.IsPublished, course.ReviewStatus }));
        await db.SaveChangesAsync();
        if (publicationChanged && !course.IsPublished)
            await personalization.Notify(course.InstructorId, $"course-unpublished:{id}:{Guid.NewGuid():N}", "Course unpublished by Admin",
                $"{course.Title[..Math.Min(course.Title.Length, 300)]} is no longer published. Open Instructor Studio to review it.", $"/InstructorCourses/Edit/{id}");
        await transaction.CommitAsync();
        TempData["Success"] = contentChanged ? "Course details saved. The instructor must submit the changed course for approval before publishing." : "Course publication status updated.";
        return RedirectToAction(nameof(Courses));
    }

    [HttpGet]
    public async Task<IActionResult> DeleteCourse(int id)
    {
        var course = await db.Courses.AsNoTracking().SingleOrDefaultAsync(c => c.CourseId == id);
        if (course is null) return NotFound();
        ViewData["HasLearningData"] = await HasCourseLearningData(id);
        return View(course);
    }

    [HttpPost, ActionName("DeleteCourse")]
    public async Task<IActionResult> DeleteCourseConfirmed(int id)
    {
        var course = await db.Courses.SingleOrDefaultAsync(c => c.CourseId == id);
        if (course is null) return NotFound();
        var hasLearningData = await HasCourseLearningData(id);
        ViewData["HasLearningData"] = hasLearningData;
        if (hasLearningData) ModelState.AddModelError("", "This course has learning records. Unpublish it to preserve students' work.");
        if (ModelState.IsValid)
        {
            try
            {
                records.AddActivity(users.GetUserId(User)!, "Deleted course", "Course", course.CourseId.ToString(), course.Title, "Course permanently deleted.", System.Text.Json.JsonSerializer.Serialize(new { course.Title, course.Description, course.Category, course.IsPublished }), "Deleted");
                db.Courses.Remove(course);
                await db.SaveChangesAsync();
                TempData["Success"] = "Course and its unused learning activities deleted.";
                return RedirectToAction(nameof(Courses));
            }
            catch (DbUpdateException) { ModelState.AddModelError("", "Linked learning records prevent deletion. Unpublish this course instead."); }
        }
        return View("DeleteCourse", course);
    }

    [HttpGet]
    public IActionResult Activity() => RedirectToAction(nameof(AuditLog), new { mine = true });

    private async Task<bool> HasCourseLearningData(int id) => await db.Enrollments.AnyAsync(e => e.CourseId == id)
        || await db.ChallengeSubmissions.AnyAsync(s => db.Challenges.Any(c => c.ChallengeId == s.ChallengeId && c.CourseId == id));

    private async Task<string> InstructorName(string id) => await db.Users.Where(u => u.Id == id).Select(u => u.DisplayName).FirstOrDefaultAsync() ?? "Unknown instructor";
}
