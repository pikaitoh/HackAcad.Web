using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

public partial class AdminController
{
    private async Task<CourseReviewModel?> CourseReviewDetails(int id)
    {
        var course = await db.Courses.AsNoTracking().SingleOrDefaultAsync(c => c.CourseId == id);
        if (course is null) return null;
        return new CourseReviewModel {
            Course = course, Instructor = await InstructorName(course.InstructorId),
            Input = new CourseReviewInput { Version = course.ReviewVersion },
            Lessons = await db.Lessons.AsNoTracking().Where(l => l.CourseId == id).OrderBy(l => l.OrderIndex).ToListAsync(),
            Challenges = await db.Challenges.AsNoTracking().Where(c => c.CourseId == id).ToListAsync(),
            Simulations = await db.SimulationTasks.AsNoTracking().Where(s => s.CourseId == id).ToListAsync(),
            Notebooks = await db.CourseNotebooks.AsNoTracking().Where(n => n.CourseId == id).OrderBy(n => n.OrderIndex).ToListAsync()
        };
    }

    [HttpGet]
    public async Task<IActionResult> ReviewCourse(int id)
    {
        var model = await CourseReviewDetails(id);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewCourse(int id, [Bind(Prefix = "Input")] CourseReviewInput input)
    {
        var model = await CourseReviewDetails(id);
        if (model is null) return NotFound();
        if (model.Course.ReviewStatus != CourseReviewStatus.Pending || model.Course.ReviewVersion != input.Version)
            return Conflict("This submission changed or was already reviewed. Reload the course before deciding.");
        if (model.Course.InstructorId == users.GetUserId(User)) return Forbid();
        if (!ModelState.IsValid) { model.Input = input; return View(model); }
        await using var transaction = await db.Database.BeginTransactionAsync();
        var course = await db.Courses.SingleAsync(c => c.CourseId == id);
        if (course.ReviewVersion != input.Version || course.ReviewStatus != CourseReviewStatus.Pending)
            return Conflict("This course changed. Review its current content.");
        if (!await db.Users.AnyAsync(u => u.Id == course.InstructorId && !u.IsDisabled))
            return Conflict("Resolve the instructor's disabled account before approving or rejecting this course.");
        course.ReviewStatus = input.Decision;
        course.ReviewReason = input.Reason?.Trim();
        course.ReviewedUtc = DateTime.UtcNow;
        course.IsPublished = false;
        var details = $"{input.Decision}; submission {course.ReviewRevision}. {course.ReviewReason}";
        records.AddActivity(users.GetUserId(User)!, input.Decision == CourseReviewStatus.Approved ? "Approved course" : "Rejected course",
            "Course", id.ToString(), course.Title, details.Length > 512 ? details[..512] : details,
            CourseReviewStatus.Pending, input.Decision);
        try
        {
            await db.SaveChangesAsync();
            await personalization.Notify(course.InstructorId, $"course-decision:{id}:{course.ReviewRevision}", $"Course {input.Decision.ToLowerInvariant()}",
                input.Decision == CourseReviewStatus.Approved ? "Your course is approved. Choose Publish in Instructor Studio when you are ready."
                    : "Your course was rejected. Read the reason, revise the content and submit it again.", $"/InstructorCourses/Curriculum/{id}");
            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException) { return Conflict("This course changed during review. Reload and review the current content."); }
        TempData["Success"] = input.Decision == CourseReviewStatus.Approved ? "Course approved. The instructor can now choose whether to publish it."
            : "Course rejected. The instructor can read the reason and submit a revised course.";
        return RedirectToAction(nameof(Courses));
    }
}
