using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace HackAcad.Web.Services;

public class BadgeService(ApplicationDbContext db, TimeProvider clock)
{
    public async Task<List<BadgeItem>> GetUserBadgesAsync(string userId)
    {
        // 1. Enrollment stats
        var isInstructor = await db.Instructors.AnyAsync(i => i.UserId == userId);
        var enrollments = await db.Enrollments
            .Where(e => e.UserId == userId && (!isInstructor
                || !db.Courses.Any(c => c.CourseId == e.CourseId && c.InstructorId == userId)))
            .ToListAsync();
        var enrollmentCount = enrollments.Count;

        // 2. Completed items by type
        var enrollmentIds = enrollments.Select(e => e.EnrollmentId).ToList();
        var progress = await db.ProgressRecords
            .Where(p => enrollmentIds.Contains(p.EnrollmentId) && p.IsCompleted)
            .ToListAsync();

        var lessonProgress = progress.Where(p => p.ContentType == ProgressContentType.Lesson).ToList();
        var completedLessons = lessonProgress.Count;
        var completedChallenges = progress.Count(p => p.ContentType == ProgressContentType.Challenge);
        var simulationProgress = progress.Where(p => p.ContentType == ProgressContentType.Simulation).ToList();
        var completedSimulations = simulationProgress.Count;

        // A challenge counts once, at the first correct submission for that challenge.
        var correctSubmissions = await db.ChallengeSubmissions
            .Where(s => s.UserId == userId && s.IsCorrect)
            .ToListAsync();
        var solvedAt = correctSubmissions
            .GroupBy(s => s.ChallengeId)
            .ToDictionary(group => group.Key, group => group.Min(s => AsUtc(s.SubmittedDate)));
        var solvedChallengeIds = solvedAt.Keys.ToList();

        var pointsByChallenge = await db.Challenges
            .Where(c => solvedChallengeIds.Contains(c.ChallengeId))
            .ToDictionaryAsync(c => c.ChallengeId, c => c.Points);
        var totalPoints = pointsByChallenge.Values.Sum();

        // 4. Course completions (100% completed)
        var completedCoursesCount = 0;
        var courseCompletionDates = new List<DateTime>();
        foreach (var enrollment in enrollments)
        {
            var lessonIds = await db.Lessons.Where(l => l.CourseId == enrollment.CourseId)
                .Select(l => l.LessonId).ToListAsync();
            var challengeIds = await db.Challenges.Where(c => c.CourseId == enrollment.CourseId && c.IsPublished)
                .Select(c => c.ChallengeId).ToListAsync();
            var simulationIds = await db.SimulationTasks.Where(s => s.CourseId == enrollment.CourseId && s.IsPublished)
                .Select(s => s.TaskId).ToListAsync();
            var totalItems = lessonIds.Count + challengeIds.Count + simulationIds.Count;

            if (totalItems > 0)
            {
                var courseProgress = progress.Where(p => p.EnrollmentId == enrollment.EnrollmentId).ToList();
                var completedLessonIds = courseProgress.Where(p => p.ContentType == ProgressContentType.Lesson && p.LessonId.HasValue)
                    .Select(p => p.LessonId!.Value).Intersect(lessonIds).Distinct().ToHashSet();
                var completedChallengeIds = courseProgress.Where(p => p.ContentType == ProgressContentType.Challenge && p.ChallengeId.HasValue)
                    .Select(p => p.ChallengeId!.Value).Intersect(challengeIds).Distinct().ToHashSet();
                var completedSimulationIds = courseProgress.Where(p => p.ContentType == ProgressContentType.Simulation && p.SimulationTaskId.HasValue)
                    .Select(p => p.SimulationTaskId!.Value).Intersect(simulationIds).Distinct().ToHashSet();
                var completedCount = completedLessonIds.Count + completedChallengeIds.Count + completedSimulationIds.Count;
                if (completedCount >= totalItems)
                {
                    completedCoursesCount++;
                    var requiredProgress = courseProgress.Where(p =>
                        p.ContentType == ProgressContentType.Lesson && p.LessonId.HasValue && completedLessonIds.Contains(p.LessonId.Value)
                        || p.ContentType == ProgressContentType.Challenge && p.ChallengeId.HasValue && completedChallengeIds.Contains(p.ChallengeId.Value)
                        || p.ContentType == ProgressContentType.Simulation && p.SimulationTaskId.HasValue && completedSimulationIds.Contains(p.SimulationTaskId.Value));
                    courseCompletionDates.Add(requiredProgress.Max(CompletionDateUtc));
                }
            }
        }

        var lessonDates = lessonProgress.Select(CompletionDateUtc).OrderBy(date => date).ToList();
        var challengeDates = solvedAt.Values.OrderBy(date => date).ToList();
        var simulationDates = simulationProgress.Select(CompletionDateUtc).OrderBy(date => date).ToList();
        var pointThresholdDate = GetPointThresholdDate(solvedAt, pointsByChallenge, 100);
        var awardDates = new Dictionary<string, DateTime?>
        {
            ["cyber_recruit"] = enrollments.Count == 0 ? null : enrollments.Min(e => AsUtc(e.EnrolledDate)),
            ["first_step"] = lessonDates.Count > 0 ? lessonDates[0] : null,
            ["diligent_learner"] = lessonDates.Count >= 5 ? lessonDates[4] : null,
            ["first_blood"] = challengeDates.Count > 0 ? challengeDates[0] : null,
            ["flag_hunter"] = challengeDates.Count >= 3 ? challengeDates[2] : null,
            ["century_club"] = pointThresholdDate,
            ["terminal_novice"] = simulationDates.Count > 0 ? simulationDates[0] : null,
            ["academy_graduate"] = courseCompletionDates.Count == 0 ? null : courseCompletionDates.Min()
        };

        // 5. Definitions
        var badges = new List<BadgeItem>
        {
            new()
            {
                Id = "cyber_recruit",
                Title = "Cyber Recruit",
                Description = "Enrolled in your first HackAcad cybersecurity course.",
                Category = "Enrollment",
                Rarity = "Common",
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
                Rarity = "Common",
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
                Rarity = "Uncommon",
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
                Rarity = "Common",
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
                Rarity = "Uncommon",
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
                Rarity = "Rare",
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
                Rarity = "Common",
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
                Rarity = "Epic",
                ColorHex = "#624080",
                TargetProgress = 1,
                CurrentProgress = completedCoursesCount,
                IsUnlocked = completedCoursesCount >= 1
            }
        };

        // Synchronize any newly-earned badges. The unique index makes this an
        // idempotent operation even when two requests finish at the same time.
        var existingAwards = await db.UserBadgeAwards.Where(a => a.UserId == userId)
            .ToDictionaryAsync(a => a.BadgeId);
        var newAwards = badges
            .Where(b => b.IsUnlocked && !existingAwards.ContainsKey(b.Id))
            .Select(b => new UserBadgeAward
            {
                UserId = userId,
                BadgeId = b.Id,
                AwardedUtc = AsUtc(awardDates[b.Id] ?? clock.GetUtcNow().UtcDateTime)
            })
            .ToList();

        if (newAwards.Count > 0)
        {
            db.UserBadgeAwards.AddRange(newAwards);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException exception) when (IsDuplicateAward(exception))
            {
                // A parallel request already inserted one or more of these awards.
                foreach (var entry in db.ChangeTracker.Entries<UserBadgeAward>().Where(e => e.State == EntityState.Added))
                    entry.State = EntityState.Detached;
            }
        }

        var awardMap = await db.UserBadgeAwards.AsNoTracking().Where(a => a.UserId == userId)
            .ToDictionaryAsync(a => a.BadgeId, a => a.AwardedUtc);
        foreach (var badge in badges)
        {
            if (!awardMap.TryGetValue(badge.Id, out var awardedUtc)) continue;
            badge.IsUnlocked = true;
            badge.UnlockedDate = AsUtc(awardedUtc);
            badge.CurrentProgress = Math.Max(badge.CurrentProgress, badge.TargetProgress);
        }

        return badges;
    }

    private static DateTime CompletionDateUtc(Progress progress) =>
        AsUtc(progress.CompletedUtc ?? progress.LastUpdatedDate);

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? GetPointThresholdDate(
        IReadOnlyDictionary<int, DateTime> solvedAt,
        IReadOnlyDictionary<int, int> pointsByChallenge,
        int target)
    {
        var points = 0;
        foreach (var solved in solvedAt.OrderBy(pair => pair.Value))
        {
            if (!pointsByChallenge.TryGetValue(solved.Key, out var challengePoints)) continue;
            points += challengePoints;
            if (points >= target) return solved.Value;
        }

        return null;
    }

    private static bool IsDuplicateAward(DbUpdateException exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
            if (current is MySqlException { Number: 1062 }) return true;
        return false;
    }
}
