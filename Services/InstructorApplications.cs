using System.Text.Json;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Services;

// ADMIN - Shared evidence/history queries and calendar-based teaching-experience calculations for Admin review.

public class InstructorApplications(ApplicationDbContext db)
{
    public static readonly string[] Decisions = [ApplicationStatus.Approved, ApplicationStatus.MoreInformation, ApplicationStatus.Rejected];
    public static readonly string[] Categories = ["Eligibility verified", "Experience below five years", "Teaching field not relevant", "Evidence cannot be verified", "Information incomplete or inconsistent", "Other eligibility issue"];

    // Merge overlapping teaching periods so concurrent jobs do not inflate years of experience.
    public static int TeachingDays(IEnumerable<TeachingExperience> experience, DateOnly today)
    {
        var periods = experience.Select(e => (Start: e.StartDate!.Value.DayNumber,
            End: e.EndDate is { } end && end < today ? end.AddDays(1).DayNumber : today.DayNumber))
            .OrderBy(e => e.Start).ToList();
        var total = 0;
        var until = 0;
        foreach (var period in periods)
        {
            total += Math.Max(0, period.End - Math.Max(period.Start, until));
            until = Math.Max(until, period.End);
        }
        return total;
    }

    // Calculate the actual five-year calendar threshold, including leap days.
    public static int MinimumDays(DateOnly today) => today.DayNumber - today.AddYears(-5).DayNumber;
    public static List<TeachingExperience> ReadExperience(string json) => JsonSerializer.Deserialize<List<TeachingExperience>>(json) ?? [];

    // Load submission history and linked Admin reviews without modifying the evidence.
    public async Task<ApplicationDetailsModel?> Details(long id)
    {
        var application = await db.InstructorApplications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id);
        if (application is null)
            return null;
        var history = await db.InstructorApplications.AsNoTracking().Where(a => a.UserId == application.UserId)
            .OrderByDescending(a => a.Revision).Select(a => new InstructorApplication { Id = a.Id, Revision = a.Revision, Status = a.Status, SubmittedUtc = a.SubmittedUtc }).ToListAsync();
        var ids = history.Select(a => a.Id).ToList();
        return new ApplicationDetailsModel
        {
            Application = application,
            History = history,
            Experience = ReadExperience(application.ExperienceJson),
            IsLatest = history[0].Id == id,
            ApplicantEmail = await db.Users.Where(u => u.Id == application.UserId).Select(u => u.Email).SingleAsync() ?? "",
            Reviews = await db.InstructorReviews.AsNoTracking().Where(r => ids.Contains(r.ApplicationId)).OrderByDescending(r => r.ReviewedUtc).ToListAsync()
        };
    }
}
