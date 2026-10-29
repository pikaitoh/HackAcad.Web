using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

// ADMIN - Courses menu: manage catalogue settings and preserve existing student learning records.

public partial class AdminController
{
    // Filter instructor courses by review state or publication state.
    public async Task<IActionResult> Courses(string? search, string? status, int page = 1)
    {
        var query = from c in db.Courses.AsNoTracking()
                    join instructor in db.Users on c.InstructorId equals instructor.Id
                    select new CourseRow
                    {
                        CourseId = c.CourseId,
                        Title = c.Title,
                        Category = c.Category,
                        IsPublished = c.IsPublished,
                        ReviewStatus = c.ReviewStatus,
                        Instructor = instructor.DisplayName,
                        Enrollments = db.Enrollments.Count(e => e.CourseId == c.CourseId)
                    };
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(c => c.Title.Contains(search) || c.Instructor.Contains(search));
        if (status == "published")
            query = query.Where(c => c.IsPublished);
        if (status == "draft")
            query = query.Where(c => !c.IsPublished);
        if (status is CourseReviewStatus.Draft or CourseReviewStatus.Pending or CourseReviewStatus.Approved or CourseReviewStatus.Rejected)
            query = query.Where(c => c.ReviewStatus == status);
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 20d)));
        return View(new CourseListModel
        {
            Search = search,
            Status = status,
            Page = page,
            Total = total,
            Courses = await query.OrderBy(c => c.Title).ThenBy(c => c.CourseId).Skip((page - 1) * 20).Take(20).ToListAsync()
        });
    }

    // Load saved catalogue values so the form can identify an unchanged submission.
    [HttpGet]
    public async Task<IActionResult> EditCourse(int id)
    {
        var course = await db.Courses.AsNoTracking().SingleOrDefaultAsync(c => c.CourseId == id);
        if (course is null)
            return NotFound();
        ViewData["OriginalCourse"] = course;
        return View(new EditCourseModel
        {
            CourseId = id,
            Title = course.Title,
            Description = course.Description,
            Category = course.Category,
            IsPublished = course.IsPublished,
            Instructor = await InstructorName(course.InstructorId)
        });
    }

    // Validate catalogue changes and cancel prior approval when reviewed content changes.
    [HttpPost]
    public async Task<IActionResult> EditCourse(int id, EditCourseModel input)
    {
        var course = await db.Courses.SingleOrDefaultAsync(c => c.CourseId == id);
        if (course is null)
            return NotFound();
        input.CourseId = id;
        input.Instructor = await InstructorName(course.InstructorId);
        ViewData["OriginalCourse"] = course;
        if (input.IsPublished && course.ReviewStatus != CourseReviewStatus.Approved)
            ModelState.AddModelError(nameof(input.IsPublished), "Approve the submitted course before publishing it.");
        if (!ModelState.IsValid)
            return View(input);
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
        // ADMIN - Capture the original state before applying moderation rules and writing the audit snapshot.
        var wasPublished = course.IsPublished;
        var contentChanged = course.Title.Trim() != input.Title || Optional(course.Description) != input.Description || Optional(course.Category) != input.Category;
        var before = System.Text.Json.JsonSerializer.Serialize(new
        {
            course.Title,
            course.Description,
            course.Category,
            course.IsPublished,
            course.ReviewStatus
        });
        course.Title = input.Title.Trim();
        course.Description = input.Description?.Trim();
        course.Category = input.Category?.Trim();
        course.IsPublished = input.IsPublished && !contentChanged;
        if (contentChanged)
            course.ReviewStatus = CourseReviewStatus.Draft;
        var publicationChanged = wasPublished != course.IsPublished;
        records.AddActivity(users.GetUserId(User)!, "Updated course", "Course", course.CourseId.ToString(), course.Title,
            contentChanged ? "Course details changed; new approval required." : publicationChanged ? (course.IsPublished ? "Course published" : "Course unpublished") : "Course details changed", before, System.Text.Json.JsonSerializer.Serialize(new
            {
                course.Title,
                course.Description,
                course.Category,
                course.IsPublished,
                course.ReviewStatus
            }));
        await db.SaveChangesAsync();
        if (publicationChanged && !course.IsPublished)
            await personalization.Notify(course.InstructorId, $"course-unpublished:{id}:{Guid.NewGuid():N}", "Course unpublished by Admin",
                $"{course.Title[..Math.Min(course.Title.Length, 300)]} is no longer published. Open Instructor Studio to review it.", $"/InstructorCourses/Edit/{id}");
        await transaction.CommitAsync();
        TempData["Success"] = contentChanged ? "Course details saved. The instructor must submit the changed course for approval before publishing." : "Course publication status updated.";
        return RedirectToAction(nameof(Courses));
    }

    // Explain whether permanent deletion is allowed before showing the confirmation form.
    [HttpGet]
    public async Task<IActionResult> DeleteCourse(int id)
    {
        var course = await db.Courses.AsNoTracking().SingleOrDefaultAsync(c => c.CourseId == id);
        if (course is null)
            return NotFound();
        ViewData["HasLearningData"] = await HasCourseLearningData(id);
        return View(course);
    }

    // Delete only courses without learning data and retain an audit entry for the operation.
    [HttpPost, ActionName("DeleteCourse")]
    public async Task<IActionResult> DeleteCourseConfirmed(int id)
    {
        var course = await db.Courses.SingleOrDefaultAsync(c => c.CourseId == id);
        if (course is null)
            return NotFound();
        // ADMIN - Check learning references again inside the deletion flow; confirmation pages may be stale.
        var hasLearningData = await HasCourseLearningData(id);
        ViewData["HasLearningData"] = hasLearningData;
        if (hasLearningData)
            ModelState.AddModelError("", "This course has learning records. Unpublish it to preserve students' work.");
        if (ModelState.IsValid)
        {
            try
            {
                records.AddActivity(users.GetUserId(User)!, "Deleted course", "Course", course.CourseId.ToString(), course.Title, "Course permanently deleted.", System.Text.Json.JsonSerializer.Serialize(new
                {
                    course.Title,
                    course.Description,
                    course.Category,
                    course.IsPublished
                }), "Deleted");
                db.Courses.Remove(course);
                await db.SaveChangesAsync();
                TempData["Success"] = "Course and its unused learning activities deleted.";
                return RedirectToAction(nameof(Courses));
            }
            catch (DbUpdateException) { ModelState.AddModelError("", "Linked learning records prevent deletion. Unpublish this course instead."); }
        }
        return View("DeleteCourse", course);
    }

    // Protect enrolments and other student records from destructive course deletion.
    private async Task<bool> HasCourseLearningData(int id) => await db.Enrollments.AnyAsync(e => e.CourseId == id)
        || await db.ChallengeSubmissions.AnyAsync(s => db.Challenges.Any(c => c.ChallengeId == s.ChallengeId && c.CourseId == id));


    // Resolve the owner's current display name for Admin-facing course screens.
    private async Task<string> InstructorName(string id) => await db.Users.Where(u => u.Id == id).Select(u => u.DisplayName).FirstOrDefaultAsync() ?? "Unknown instructor";

}
