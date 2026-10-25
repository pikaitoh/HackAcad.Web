using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace HackAcad.Web.Services;

public class BadgeService(ApplicationDbContext db, TimeProvider clock, Personalization notifications, UserTimeZone userTime)
{
    public async Task<List<BadgeItem>> GetUserBadgesAsync(string userId, DateTime? activityEventUtc = null)
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
        var uniqueCompletedLessonIds = lessonProgress.Where(p => p.LessonId.HasValue).Select(p => p.LessonId!.Value).Distinct().ToHashSet();
        var completedLessons = uniqueCompletedLessonIds.Count;
        var simulationProgress = progress.Where(p => p.ContentType == ProgressContentType.Simulation).ToList();
        var uniqueCompletedSimulationIds = simulationProgress.Where(p => p.SimulationTaskId.HasValue).Select(p => p.SimulationTaskId!.Value).Distinct().ToHashSet();
        var completedSimulations = uniqueCompletedSimulationIds.Count;

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

        var lessonDates = lessonProgress.Where(p => p.LessonId.HasValue)
            .GroupBy(p => p.LessonId!.Value).Select(g => g.Min(CompletionDateUtc)).OrderBy(date => date).ToList();
        var challengeDates = solvedAt.Values.OrderBy(date => date).ToList();
        var simulationDates = simulationProgress.Where(p => p.SimulationTaskId.HasValue)
            .GroupBy(p => p.SimulationTaskId!.Value).Select(g => g.Min(CompletionDateUtc)).OrderBy(date => date).ToList();
        var pointThresholdDate = GetPointThresholdDate(solvedAt, pointsByChallenge, 100);
        var courseCompletionDatesSorted = courseCompletionDates.OrderBy(date => date).ToList();

        // Full-Spectrum requires all three activity types in one course.
        DateTime? fullSpectrumDate = null;
        var fullSpectrumProgress = 0;
        foreach (var enrollment in enrollments)
        {
            var courseProgress = progress.Where(p => p.EnrollmentId == enrollment.EnrollmentId).ToList();
            var lessonDate = courseProgress.Where(p => p.ContentType == ProgressContentType.Lesson && p.LessonId.HasValue)
                .GroupBy(p => p.LessonId!.Value).Select(g => g.Min(CompletionDateUtc)).DefaultIfEmpty().Min();
            var challengeDate = courseProgress.Where(p => p.ContentType == ProgressContentType.Challenge && p.ChallengeId.HasValue)
                .GroupBy(p => p.ChallengeId!.Value).Select(g => g.Min(CompletionDateUtc)).DefaultIfEmpty().Min();
            var labDate = courseProgress.Where(p => p.ContentType == ProgressContentType.Simulation && p.SimulationTaskId.HasValue)
                .GroupBy(p => p.SimulationTaskId!.Value).Select(g => g.Min(CompletionDateUtc)).DefaultIfEmpty().Min();
            var covered = (lessonDate != default ? 1 : 0) + (challengeDate != default ? 1 : 0) + (labDate != default ? 1 : 0);
            fullSpectrumProgress = Math.Max(fullSpectrumProgress, covered);
            if (lessonDate == default || challengeDate == default || labDate == default) continue;
            var candidate = new[] { lessonDate, challengeDate, labDate }.Max();
            if (fullSpectrumDate is null || candidate < fullSpectrumDate) fullSpectrumDate = candidate;
        }

        var streakEvents = progress.Select(CompletionDateUtc).Concat(enrollments.Where(e => e.FinishedUtc.HasValue).Select(e => AsUtc(e.FinishedUtc!.Value)));
        var streak = CalculateStreakMetrics(streakEvents);
        DateTime? AtThreshold(IReadOnlyList<DateTime> dates, int threshold) => dates.Count >= threshold ? dates[threshold - 1] : null;
        DateTime? StreakThresholdDate(int threshold) => streak.ThresholdDates.TryGetValue(threshold, out var date) ? date : null;
        var awardDates = new Dictionary<string, DateTime?>
        {
            ["cyber_recruit"] = enrollments.Count == 0 ? null : enrollments.Min(e => AsUtc(e.EnrolledDate)),
            ["first_step"] = lessonDates.Count > 0 ? lessonDates[0] : null,
            ["diligent_learner"] = lessonDates.Count >= 5 ? lessonDates[4] : null,
            ["first_blood"] = challengeDates.Count > 0 ? challengeDates[0] : null,
            ["flag_hunter"] = challengeDates.Count >= 3 ? challengeDates[2] : null,
            ["century_club"] = pointThresholdDate,
            ["terminal_novice"] = simulationDates.Count > 0 ? simulationDates[0] : null,
            ["academy_graduate"] = courseCompletionDates.Count == 0 ? null : courseCompletionDates.Min(),
            ["full_spectrum_operator"] = fullSpectrumDate,
            ["lab_regular_10"] = AtThreshold(simulationDates, 10),
            ["lab_veteran_25"] = AtThreshold(simulationDates, 25),
            ["ctf_veteran_10"] = AtThreshold(challengeDates, 10),
            ["flag_master_25"] = AtThreshold(challengeDates, 25),
            ["master_learner_10"] = AtThreshold(courseCompletionDatesSorted, 10),
            ["course_champion_25"] = AtThreshold(courseCompletionDatesSorted, 25),
            ["dedicated_scholar_25"] = AtThreshold(lessonDates, 25),
            ["cyber_scholar_100"] = AtThreshold(lessonDates, 100),
            ["week_streak_7"] = StreakThresholdDate(7),
            ["month_streak_30"] = StreakThresholdDate(30),
            ["century_streak_100"] = StreakThresholdDate(100)
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
            },
            Make("full_spectrum_operator", "Full-Spectrum Operator", "Complete a lesson, correctly solve a CTF, and finish a lab in the same course.", "Mission Sets", "Uncommon", "#7c3aed", 3,
                fullSpectrumProgress, fullSpectrumDate is not null),
            Make("lab_regular_10", "Lab Regular", "Complete 10 distinct hands-on labs.", "Simulation", "Uncommon", "#0284c7", 10, completedSimulations, completedSimulations >= 10),
            Make("lab_veteran_25", "Lab Veteran", "Complete 25 distinct hands-on labs.", "Simulation", "Rare", "#0369a1", 25, completedSimulations, completedSimulations >= 25),
            Make("ctf_veteran_10", "CTF Veteran", "Correctly solve 10 distinct CTF challenges.", "CTF", "Uncommon", "#d9534f", 10, solvedChallengeIds.Count, solvedChallengeIds.Count >= 10),
            Make("flag_master_25", "Flag Master", "Correctly solve 25 distinct CTF challenges.", "CTF", "Rare", "#b91c1c", 25, solvedChallengeIds.Count, solvedChallengeIds.Count >= 25),
            Make("master_learner_10", "Master Learner", "Complete 10 courses.", "Mastery", "Rare", "#624080", 10, completedCoursesCount, completedCoursesCount >= 10),
            Make("course_champion_25", "Course Champion", "Complete 25 courses.", "Mastery", "Epic", "#5b21b6", 25, completedCoursesCount, completedCoursesCount >= 25),
            Make("dedicated_scholar_25", "Dedicated Scholar", "Complete 25 distinct lessons.", "Learning", "Uncommon", "#28545a", 25, completedLessons, completedLessons >= 25),
            Make("cyber_scholar_100", "Cyber Scholar", "Complete 100 distinct lessons.", "Learning", "Rare", "#134e4a", 100, completedLessons, completedLessons >= 100),
            Make("week_streak_7", "Week Streak", "Complete at least one learning event on 7 consecutive local days.", "Consistency", "Uncommon", "#ea580c", 7, streak.CurrentStreakDays, awardDates["week_streak_7"] is not null),
            Make("month_streak_30", "Month Streak", "Complete at least one learning event on 30 consecutive local days.", "Consistency", "Rare", "#c2410c", 30, streak.CurrentStreakDays, awardDates["month_streak_30"] is not null),
            Make("century_streak_100", "Century Streak", "Complete at least one learning event on 100 consecutive local days.", "Consistency", "Epic", "#9a3412", 100, streak.CurrentStreakDays, awardDates["century_streak_100"] is not null)
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

            // Reads and historical backfills stay silent. A completion handler
            // supplies its event time, so only badges crossed by that activity
            // produce a notification.
            if (activityEventUtc is { } eventUtc && badge.IsUnlocked
                && awardDates[badge.Id] is { } requirementReachedUtc
                && Math.Abs((AsUtc(requirementReachedUtc) - AsUtc(eventUtc)).TotalSeconds) <= 2)
            {
                await notifications.Notify(userId, $"badge:{badge.Id}", $"Badge earned: {badge.Title}",
                    $"You earned the {badge.Title} badge. View your badge collection.", "/Identity/Account/Badges");
            }
        }

        return badges;
    }

    private static DateTime CompletionDateUtc(Progress progress) =>
        AsUtc(progress.CompletedUtc ?? progress.LastUpdatedDate);

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static BadgeItem Make(string id, string title, string description, string category, string rarity, string color, int target, int progress, bool unlocked) => new()
    {
        Id = id, Title = title, Description = description, Category = category, Rarity = rarity,
        ColorHex = color, TargetProgress = target, CurrentProgress = progress, IsUnlocked = unlocked
    };

    private sealed record StreakMetrics(int CurrentStreakDays, Dictionary<int, DateTime> ThresholdDates);

    private StreakMetrics CalculateStreakMetrics(IEnumerable<DateTime> timestamps)
    {
        var dayEvents = timestamps.Select(AsUtc)
            .GroupBy(t => DateOnly.FromDateTime(userTime.Convert(t).DateTime))
            .ToDictionary(g => g.Key, g => g.Min());
        var days = dayEvents.Keys.OrderBy(d => d).ToList();
        var thresholds = new Dictionary<int, DateTime>();
        var run = 0;
        DateOnly? previous = null;
        foreach (var day in days)
        {
            run = previous.HasValue && previous.Value.AddDays(1) == day ? run + 1 : 1;
            foreach (var target in new[] { 7, 30, 100 })
                if (run == target) thresholds[target] = dayEvents[day];
            previous = day;
        }

        var today = DateOnly.FromDateTime(userTime.Convert(clock.GetUtcNow().UtcDateTime).DateTime);
        var lastDay = days.LastOrDefault();
        var current = 0;
        if (days.Count > 0 && (lastDay == today || lastDay.AddDays(1) == today))
        {
            current = 1;
            for (var i = days.Count - 1; i > 0 && days[i - 1].AddDays(1) == days[i]; i--) current++;
        }
        return new StreakMetrics(current, thresholds);
    }

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
