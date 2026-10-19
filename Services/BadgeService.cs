using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Services;

public class BadgeService(ApplicationDbContext db)
{
    public async Task<List<BadgeItem>> GetUserBadgesAsync(string userId)
    {
        // 1. Enrollment stats
        var enrollments = await db.Enrollments.Where(e => e.UserId == userId).ToListAsync();
        var enrollmentCount = enrollments.Count;

        // 2. Completed items by type
        var enrollmentIds = enrollments.Select(e => e.EnrollmentId).ToList();
        var progress = await db.ProgressRecords
            .Where(p => enrollmentIds.Contains(p.EnrollmentId) && p.IsCompleted)
            .ToListAsync();

        var completedLessons = progress.Count(p => p.ContentType == ProgressContentType.Lesson);
        var completedChallenges = progress.Count(p => p.ContentType == ProgressContentType.Challenge);
        var completedSimulations = progress.Count(p => p.ContentType == ProgressContentType.Simulation);

        // 3. Points from unique solved challenges
        var solvedChallengeIds = await db.ChallengeSubmissions
            .Where(s => s.UserId == userId && s.IsCorrect)
            .Select(s => s.ChallengeId)
            .Distinct()
            .ToListAsync();

        var totalPoints = await db.Challenges
            .Where(c => solvedChallengeIds.Contains(c.ChallengeId))
            .SumAsync(c => (int?)c.Points) ?? 0;

        // 4. Course completions (100% completed)
        var completedCoursesCount = 0;
        foreach (var enrollment in enrollments)
        {
            var totalCourseLessons = await db.Lessons.CountAsync(l => l.CourseId == enrollment.CourseId);
            var totalCourseChallenges = await db.Challenges.CountAsync(c => c.CourseId == enrollment.CourseId && c.IsPublished);
            var totalCourseSimulations = await db.SimulationTasks.CountAsync(s => s.CourseId == enrollment.CourseId && s.IsPublished);
            var totalItems = totalCourseLessons + totalCourseChallenges + totalCourseSimulations;

            if (totalItems > 0)
            {
                var completedInThisCourse = progress.Count(p => p.EnrollmentId == enrollment.EnrollmentId);
                if (completedInThisCourse >= totalItems)
                {
                    completedCoursesCount++;
                }
            }
        }

        // 5. Definitions
        var badges = new List<BadgeItem>
        {
            new()
            {
                Id = "cyber_recruit",
                Title = "Cyber Recruit",
                Description = "Enrolled in your first HackAcad cybersecurity course.",
                Category = "Enrollment",
                ColorHex = "#33796e",
                TargetProgress = 1,
                CurrentProgress = enrollmentCount,
                IsUnlocked = enrollmentCount >= 1
            },
            new()
            {
                Id = "first_step",
                Title = "First Step",
                Description = "Read and completed your first guided security lesson.",
                Category = "Learning",
                ColorHex = "#1d694b",
                TargetProgress = 1,
                CurrentProgress = completedLessons,
                IsUnlocked = completedLessons >= 1
            },
            new()
            {
                Id = "diligent_learner",
                Title = "Diligent Learner",
                Description = "Completed 5 or more theory and technical lessons.",
                Category = "Learning",
                ColorHex = "#28545a",
                TargetProgress = 5,
                CurrentProgress = completedLessons,
                IsUnlocked = completedLessons >= 5
            },
            new()
            {
                Id = "first_blood",
                Title = "First Blood",
                Description = "Captured and verified your first CTF challenge flag.",
                Category = "CTF",
                ColorHex = "#b12e3f",
                TargetProgress = 1,
                CurrentProgress = solvedChallengeIds.Count,
                IsUnlocked = solvedChallengeIds.Count >= 1
            },
            new()
            {
                Id = "flag_hunter",
                Title = "Flag Hunter",
                Description = "Successfully solved 3 or more CTF cybersecurity challenges.",
                Category = "CTF",
                ColorHex = "#d9534f",
                TargetProgress = 3,
                CurrentProgress = solvedChallengeIds.Count,
                IsUnlocked = solvedChallengeIds.Count >= 3
            },
            new()
            {
                Id = "century_club",
                Title = "Century Club",
                Description = "Accumulated 100+ points on the HackAcad CTF scoreboard.",
                Category = "Scoring",
                ColorHex = "#f2cf45",
                TargetProgress = 100,
                CurrentProgress = totalPoints,
                IsUnlocked = totalPoints >= 100
            },
            new()
            {
                Id = "terminal_novice",
                Title = "Terminal Apprentice",
                Description = "Completed your first hands-on simulated Linux CLI exercise.",
                Category = "Simulation",
                ColorHex = "#0284c7",
                TargetProgress = 1,
                CurrentProgress = completedSimulations,
                IsUnlocked = completedSimulations >= 1
            },
            new()
            {
                Id = "academy_graduate",
                Title = "Academy Graduate",
                Description = "Achieved 100% completion in a full cybersecurity course.",
                Category = "Mastery",
                ColorHex = "#624080",
                TargetProgress = 1,
                CurrentProgress = completedCoursesCount,
                IsUnlocked = completedCoursesCount >= 1
            }
        };

        return badges;
    }
}
