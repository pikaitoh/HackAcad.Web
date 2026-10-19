using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
namespace HackAcad.Web.Services;

public class Personalization(ApplicationDbContext db, TimeProvider clock)
{
    public async Task Notify(string userId, string key, string title, string message, string link)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        // The unique recipient/event key makes retries and concurrent tabs idempotent.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO UserNotifications (UserId, EventKey, Title, Message, Link, CreatedUtc) VALUES ({userId}, {key}, {title}, {message}, {link}, {now}) ON DUPLICATE KEY UPDATE Id = Id");
    }
    public async Task NotifyAdmins(long applicationId, string applicant, bool revised)
    {
        foreach (var id in await db.Admins.Select(a => a.UserId).ToListAsync())
            await Notify(id, $"application:{applicationId}", revised ? "Instructor application resubmitted" : "New Instructor application",
                $"{applicant} submitted an application for review.", $"/Admin/ReviewApplication/{applicationId}");
    }
    public async Task SyncRewards(string userId)
    {
        if (!await db.Students.AnyAsync(s => s.UserId == userId)) return;
        var earned = await db.BannerRewards.Where(r => r.UserId == userId).Select(r => r.CourseId).ToListAsync();
        var enrollments = await (from e in db.Enrollments.AsNoTracking() join c in db.Courses on e.CourseId equals c.CourseId
            where e.UserId == userId && !earned.Contains(c.CourseId)
            select new { e.EnrollmentId, c.CourseId, c.Title, c.Category }).ToListAsync();
        foreach (var item in enrollments)
        {
            var lessons = await db.Lessons.Where(l => l.CourseId == item.CourseId).Select(l => l.LessonId).ToListAsync();
            var challenges = await db.Challenges.Where(c => c.CourseId == item.CourseId && c.IsPublished).Select(c => c.ChallengeId).ToListAsync();
            var labs = await db.SimulationTasks.Where(t => t.CourseId == item.CourseId && t.IsPublished).Select(t => t.TaskId).ToListAsync();
            if (lessons.Count + challenges.Count + labs.Count == 0) continue;
            var progress = await db.ProgressRecords.Where(p => p.EnrollmentId == item.EnrollmentId && p.IsCompleted).ToListAsync();
            if (!lessons.All(id => progress.Any(p => p.ContentType == ProgressContentType.Lesson && p.LessonId == id))
                || !challenges.All(id => progress.Any(p => p.ContentType == ProgressContentType.Challenge && p.ChallengeId == id))
                || !labs.All(id => progress.Any(p => p.ContentType == ProgressContentType.Simulation && p.SimulationTaskId == id))) continue;
            var theme = ThemeFor(item.Category ?? item.Title);
            var title = item.Title.Length > 150 ? item.Title[..150] : item.Title;
            var now = clock.GetUtcNow().UtcDateTime;
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO BannerRewards (UserId, CourseId, CourseTitle, Theme, UnlockedUtc) VALUES ({userId}, {item.CourseId}, {title}, {theme}, {now}) ON DUPLICATE KEY UPDATE Id = Id");
            await Notify(userId, $"banner:{item.CourseId}", "New profile banner unlocked", $"You completed {title}. Choose your reward in My profile.", "/Identity/Account/Profile#profile-banners");
        }
    }
    public static string ThemeFor(string topic) => topic.Contains("crypt", StringComparison.OrdinalIgnoreCase) ? "cipher"
        : topic.Contains("linux", StringComparison.OrdinalIgnoreCase) ? "terminal"
        : topic.Contains("web", StringComparison.OrdinalIgnoreCase) ? "network" : "scholar";
    public async Task<List<BannerChoice>> Banners(string userId, bool student)
    {
        var choices = new List<BannerChoice> {
            new("foundation", "Foundation", "foundation", true, "Available to everyone"),
            new("evergreen", "Evergreen", "evergreen", true, "Available to everyone"),
            new("midnight", "Midnight", "midnight", true, "Available to everyone") };
        if (!student) return choices;
        var rewards = await db.BannerRewards.AsNoTracking().Where(r => r.UserId == userId).ToListAsync();
        choices.AddRange(rewards.Select(r => new BannerChoice($"course-{r.CourseId}", r.CourseTitle, r.Theme, true, "Course completed")));
        var earned = rewards.Select(r => r.CourseId).ToList();
        var locked = await db.Courses.AsNoTracking().Where(c => c.IsPublished && !earned.Contains(c.CourseId)).OrderBy(c => c.Title).Select(c => new { c.CourseId, c.Title, c.Category }).ToListAsync();
        choices.AddRange(locked.Select(c => new BannerChoice($"course-{c.CourseId}", c.Title, ThemeFor(c.Category ?? c.Title), false, $"Complete {c.Title} to unlock")));
        return choices;
    }
}
