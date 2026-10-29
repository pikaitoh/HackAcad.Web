using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

// ADMIN - Dashboard menu: aggregate persisted statistics for a selected local-calendar period.

public partial class AdminController
{
    // Build statistics and chart series from persisted data for the selected 7, 30 or 90 local days.
    [HttpGet]
    public async Task<IActionResult> Index([FromServices] TimeProvider clock, int days = 30)
    {
        if (days is not (7 or 30 or 90))
            return BadRequest("Choose a 7, 30 or 90 day period.");
        // ADMIN - Use the viewer's local calendar for boundaries, then query stored UTC timestamps.
        var today = DateOnly.FromDateTime(userTime.Convert(clock.GetUtcNow().UtcDateTime).DateTime);
        var from = today.AddDays(1 - days);
        var start = userTime.StartOfDayUtc(from);
        var end = userTime.StartOfDayUtc(today.AddDays(1));
        var previous = userTime.StartOfDayUtc(from.AddDays(-days));
        var signIns = await db.LoginRecords.AsNoTracking()
            .Where(r => r.OccurredUtc >= start && r.OccurredUtc < end && r.Outcome.StartsWith("Signed in"))
            .Select(r => new { r.UserId, r.OccurredUtc }).ToListAsync();
        // Group in the viewer's time zone, including local 23/25-hour DST days.
        var daily = signIns.GroupBy(r => DateOnly.FromDateTime(userTime.Convert(r.OccurredUtc).DateTime))
            .ToDictionary(g => g.Key, g => g.Select(r => r.UserId).Distinct().Count());
        // ADMIN - Current totals, period activity and previous-period comparisons represent different measurements.
        var model = new AdminOverviewModel
        {
            AdminName = await db.Users.Where(u => u.Id == users.GetUserId(User)).Select(u => u.DisplayName).FirstOrDefaultAsync() ?? "Admin",
            Days = days,
            From = from,
            To = today,
            Users = await db.Users.CountAsync(),
            NewUsers = await db.Users.CountAsync(u => u.JoinedDate >= start && u.JoinedDate < end),
            DisabledUsers = await db.Users.CountAsync(u => u.IsDisabled),
            ActiveUsers = signIns.Select(r => r.UserId).Distinct().Count(),
            PreviousActiveUsers = await db.LoginRecords.Where(r => r.OccurredUtc >= previous && r.OccurredUtc < start && r.Outcome.StartsWith("Signed in"))
                .Select(r => r.UserId).Distinct().CountAsync(),
            Courses = await db.Courses.CountAsync(),
            PublishedCourses = await db.Courses.CountAsync(c => c.IsPublished),
            PendingApplications = await db.InstructorApplications.CountAsync(a => a.Status == ApplicationStatus.Pending),
            PendingSupport = await db.SupportTickets.CountAsync(t => t.Status == "Awaiting admin"),
            OpenAlerts = await db.SecurityAlerts.CountAsync(a => a.Status != "Resolved"),
            Activity = Enumerable.Range(0, days).Select(i => new DashboardActivityDay(from.AddDays(i), daily.GetValueOrDefault(from.AddDays(i)))).ToList(),
            CourseEnrollments = await db.Courses.AsNoTracking().Select(c => new DashboardCourseEnrollments
            {
                CourseId = c.CourseId,
                Title = c.Title,
                Current = db.Enrollments.Count(e => e.CourseId == c.CourseId && e.EnrolledDate >= start && e.EnrolledDate < end),
                Previous = db.Enrollments.Count(e => e.CourseId == c.CourseId && e.EnrolledDate >= previous && e.EnrolledDate < start)
            }).OrderByDescending(c => c.Current).ThenByDescending(c => c.Previous).ThenBy(c => c.CourseId).Take(5).ToListAsync(),
            Roles = await accounts.Query().GroupBy(u => u.Role)
                .Select(g => new DashboardRoleCount { Role = g.Key, Count = g.Count() }).ToListAsync(),
            RecentActions = await db.AdminActivities.AsNoTracking().Where(a => a.TargetType != "Account security")
                .OrderByDescending(a => a.OccurredUtc).ThenByDescending(a => a.Id).Take(5).ToListAsync()
        };
        var userIds = model.RecentActions.Select(a => a.ActorUserId)
            .Concat(model.RecentActions.Where(a => AuditPresentation.AccountTargetTypes.Contains(a.TargetType)).Select(a => a.TargetId)).Distinct().ToList();
        model.UserNames = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);
        return View(model);
    }
}
