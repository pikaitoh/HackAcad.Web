using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace HackAcad.Web.Services;

public class BadgeService(ApplicationDbContext db, TimeProvider clock, Personalization notifications, UserTimeZone userTime)
{
    private const int WeekStreakTarget = 7;
    private const int MonthStreakTarget = 30;
    private const int CenturyStreakTarget = 100;

    // Calculate earned badge progress and synchronize permanent awards.
    public async Task<List<BadgeItem>> GetUserBadgesAsync(string userId, DateTime? activityEventUtc = null)
    {
        // Load learner enrollments and completed work.
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
        var progressByEnrollment = progress
            .GroupBy(p => p.EnrollmentId)
            .ToDictionary(group => group.Key, group => group.ToList());

        List<Progress> ProgressFor(int enrollmentId) =>
            progressByEnrollment.TryGetValue(enrollmentId, out var records) ? records : [];

        var lessonProgress = progress.Where(p => p.ContentType == ProgressContentType.Lesson).ToList();
        var completedLessons = lessonProgress
            .Where(p => p.LessonId.HasValue)
            .Select(p => p.LessonId!.Value)
            .Distinct()
            .Count();
        var simulationProgress = progress.Where(p => p.ContentType == ProgressContentType.Simulation).ToList();
        var completedSimulations = simulationProgress
            .Where(p => p.SimulationTaskId.HasValue)
            .Select(p => p.SimulationTaskId!.Value)
            .Distinct()
            .Count();

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

        // Count courses only after every published activity is complete.
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
                var courseProgress = ProgressFor(enrollment.EnrollmentId);
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

        var lessonDates = CompletionDatesByItem(lessonProgress, p => p.LessonId);
        var challengeDates = solvedAt.Values.OrderBy(date => date).ToList();
        var simulationDates = CompletionDatesByItem(simulationProgress, p => p.SimulationTaskId);
        var pointThresholdDate = GetPointThresholdDate(solvedAt, pointsByChallenge, 100);
        var courseCompletionDatesSorted = courseCompletionDates.OrderBy(date => date).ToList();

        // Track the three mission types within each enrolled course.
        DateTime? fullSpectrumDate = null;
        var fullSpectrumProgress = 0;
        foreach (var enrollment in enrollments)
        {
            var courseProgress = ProgressFor(enrollment.EnrollmentId);
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

        var streakEvents = progress.Select(CompletionDateUtc)
            .Concat(enrollments.Where(e => e.FinishedUtc.HasValue)
                .Select(e => AsUtc(e.FinishedUtc!.Value)));
        var streak = CalculateStreakMetrics(streakEvents);
        DateTime? AtThreshold(IReadOnlyList<DateTime> dates, int threshold) => dates.Count >= threshold ? dates[threshold - 1] : null;
        DateTime? StreakThresholdDate(int threshold) => streak.ThresholdDates.TryGetValue(threshold, out var date) ? date : null;

        // Record when each badge requirement was first reached.
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
            ["week_streak_7"] = StreakThresholdDate(WeekStreakTarget),
            ["month_streak_30"] = StreakThresholdDate(MonthStreakTarget),
            ["century_streak_100"] = StreakThresholdDate(CenturyStreakTarget)
        };

        var snapshot = new BadgeProgressSnapshot(
            enrollmentCount,
            completedLessons,
            completedSimulations,
            solvedChallengeIds.Count,
            totalPoints,
            completedCoursesCount,
            fullSpectrumProgress,
            streak,
            awardDates);
        var badges = BuildBadges(snapshot);
        await SynchronizeAwardsAsync(userId, badges, snapshot.AwardDates, activityEventUtc);
        return badges;
    }

    // Define the badge catalog and each badge's current progress.
    private static List<BadgeItem> BuildBadges(BadgeProgressSnapshot progress)
    {
        var solvedChallenges = progress.SolvedChallengeCount;
        var streaks = progress.Streak;
        var awardDates = progress.AwardDates;

        return
        [
            CreateBadge(
                "cyber_recruit", "Cyber Recruit", "Enrolled in your first HackAcad cybersecurity course.",
                "Enrollment", "Common", "#33796e", 1, progress.EnrollmentCount, progress.EnrollmentCount >= 1),
            CreateBadge(
                "first_step", "First Step", "Read and completed your first guided security lesson.",
                "Learning", "Common", "#1d694b", 1, progress.CompletedLessons, progress.CompletedLessons >= 1),
            CreateBadge(
                "diligent_learner", "Diligent Learner", "Completed 5 or more theory and technical lessons.",
                "Learning", "Uncommon", "#28545a", 5, progress.CompletedLessons, progress.CompletedLessons >= 5),
            CreateBadge(
                "first_blood", "First Blood", "Captured and verified your first CTF challenge flag.",
                "CTF", "Common", "#b12e3f", 1, solvedChallenges, solvedChallenges >= 1),
            CreateBadge(
                "flag_hunter", "Flag Hunter", "Successfully solved 3 or more CTF cybersecurity challenges.",
                "CTF", "Uncommon", "#d9534f", 3, solvedChallenges, solvedChallenges >= 3),
            CreateBadge(
                "century_club", "Century Club", "Accumulated 100+ points on the HackAcad CTF scoreboard.",
                "Scoring", "Rare", "#f2cf45", 100, progress.TotalPoints, progress.TotalPoints >= 100),
            CreateBadge(
                "terminal_novice", "Terminal Apprentice", "Completed your first hands-on simulated Linux CLI exercise.",
                "Simulation", "Common", "#0284c7", 1, progress.CompletedSimulations, progress.CompletedSimulations >= 1),
            CreateBadge(
                "academy_graduate", "Academy Graduate", "Achieved 100% completion in a full cybersecurity course.",
                "Mastery", "Epic", "#624080", 1, progress.CompletedCourses, progress.CompletedCourses >= 1),
            CreateBadge(
                "full_spectrum_operator", "Full-Spectrum Operator", "Complete a lesson, correctly solve a CTF, and finish a lab in the same course.",
                "Mission Sets", "Uncommon", "#7c3aed", 3, progress.FullSpectrumProgress, awardDates["full_spectrum_operator"] is not null),
            CreateBadge(
                "lab_regular_10", "Lab Regular", "Complete 10 distinct hands-on labs.",
                "Simulation", "Uncommon", "#0284c7", 10, progress.CompletedSimulations, progress.CompletedSimulations >= 10),
            CreateBadge(
                "lab_veteran_25", "Lab Veteran", "Complete 25 distinct hands-on labs.",
                "Simulation", "Rare", "#0369a1", 25, progress.CompletedSimulations, progress.CompletedSimulations >= 25),
            CreateBadge(
                "ctf_veteran_10", "CTF Veteran", "Correctly solve 10 distinct CTF challenges.",
                "CTF", "Uncommon", "#d9534f", 10, solvedChallenges, solvedChallenges >= 10),
            CreateBadge(
                "flag_master_25", "Flag Master", "Correctly solve 25 distinct CTF challenges.",
                "CTF", "Rare", "#b91c1c", 25, solvedChallenges, solvedChallenges >= 25),
            CreateBadge(
                "master_learner_10", "Master Learner", "Complete 10 courses.",
                "Mastery", "Rare", "#624080", 10, progress.CompletedCourses, progress.CompletedCourses >= 10),
            CreateBadge(
                "course_champion_25", "Course Champion", "Complete 25 courses.",
                "Mastery", "Epic", "#5b21b6", 25, progress.CompletedCourses, progress.CompletedCourses >= 25),
            CreateBadge(
                "dedicated_scholar_25", "Dedicated Scholar", "Complete 25 distinct lessons.",
                "Learning", "Uncommon", "#28545a", 25, progress.CompletedLessons, progress.CompletedLessons >= 25),
            CreateBadge(
                "cyber_scholar_100", "Cyber Scholar", "Complete 100 distinct lessons.",
                "Learning", "Rare", "#134e4a", 100, progress.CompletedLessons, progress.CompletedLessons >= 100),
            CreateBadge(
                "week_streak_7", "Week Streak", "Complete at least one learning event on 7 consecutive local days.",
                "Consistency", "Uncommon", "#ea580c", WeekStreakTarget, streaks.CurrentStreakDays, awardDates["week_streak_7"] is not null),
            CreateBadge(
                "month_streak_30", "Month Streak", "Complete at least one learning event on 30 consecutive local days.",
                "Consistency", "Rare", "#c2410c", MonthStreakTarget, streaks.CurrentStreakDays, awardDates["month_streak_30"] is not null),
            CreateBadge(
                "century_streak_100", "Century Streak", "Complete at least one learning event on 100 consecutive local days.",
                "Consistency", "Epic", "#9a3412", CenturyStreakTarget, streaks.CurrentStreakDays, awardDates["century_streak_100"] is not null)
        ];
    }

    // Persist permanent awards and notify learners when they earn one.
    private async Task SynchronizeAwardsAsync(
        string userId,
        List<BadgeItem> badges,
        IReadOnlyDictionary<string, DateTime?> awardDates,
        DateTime? activityEventUtc)
    {
        var existingAwards = await db.UserBadgeAwards
            .Where(award => award.UserId == userId)
            .ToDictionaryAsync(award => award.BadgeId);
        var newAwards = badges
            .Where(badge => badge.IsUnlocked && !existingAwards.ContainsKey(badge.Id))
            .Select(badge => new UserBadgeAward
            {
                UserId = userId,
                BadgeId = badge.Id,
                AwardedUtc = AsUtc(awardDates[badge.Id] ?? clock.GetUtcNow().UtcDateTime)
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
                // A concurrent request may have inserted the same award.
                foreach (var entry in db.ChangeTracker.Entries<UserBadgeAward>()
                    .Where(entry => entry.State == EntityState.Added))
                {
                    entry.State = EntityState.Detached;
                }
            }
        }

        var awardDatesByBadge = await db.UserBadgeAwards
            .AsNoTracking()
            .Where(award => award.UserId == userId)
            .ToDictionaryAsync(award => award.BadgeId, award => award.AwardedUtc);

        foreach (var badge in badges)
        {
            if (!awardDatesByBadge.TryGetValue(badge.Id, out var awardedUtc)) continue;

            badge.IsUnlocked = true;
            badge.UnlockedDate = AsUtc(awardedUtc);
            badge.CurrentProgress = Math.Max(badge.CurrentProgress, badge.TargetProgress);

            if (!IsNewAwardForActivity(badge.Id, awardDates, activityEventUtc)) continue;

            await notifications.Notify(
                userId,
                $"badge:{badge.Id}",
                $"Badge earned: {badge.Title}",
                $"You earned the {badge.Title} badge. View your badge collection.",
                "/Identity/Account/Badges");
        }
    }

    private static bool IsNewAwardForActivity(
        string badgeId,
        IReadOnlyDictionary<string, DateTime?> awardDates,
        DateTime? activityEventUtc) => activityEventUtc is { } eventUtc
        && awardDates[badgeId] is { } requirementReachedUtc
        && Math.Abs((AsUtc(requirementReachedUtc) - AsUtc(eventUtc)).TotalSeconds) <= 2;

    private static DateTime CompletionDateUtc(Progress progress) =>
        AsUtc(progress.CompletedUtc ?? progress.LastUpdatedDate);

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static List<DateTime> CompletionDatesByItem(
        IEnumerable<Progress> progress,
        Func<Progress, int?> getItemId) => progress
        .Where(item => getItemId(item).HasValue)
        .GroupBy(item => getItemId(item)!.Value)
        .Select(group => group.Min(CompletionDateUtc))
        .OrderBy(date => date)
        .ToList();

    private static BadgeItem CreateBadge(
        string id,
        string title,
        string description,
        string category,
        string rarity,
        string colorHex,
        int targetProgress,
        int currentProgress,
        bool isUnlocked) => new()
    {
        Id = id,
        Title = title,
        Description = description,
        Category = category,
        Rarity = rarity,
        ColorHex = colorHex,
        TargetProgress = targetProgress,
        CurrentProgress = currentProgress,
        IsUnlocked = isUnlocked,
        IconSvg = GetBadgeIconSvg(id)
    };

    // Return a local, recolorable SVG for each badge.
    private static string GetBadgeIconSvg(string id)
    {
        var icon = id switch
        {
            "cyber_recruit" => "<path d=\"M12 2 20 5v6c0 5-3.4 8.5-8 11-4.6-2.5-8-6-8-11V5l8-3Z\"/><path d=\"M12 7v8m-4-4h8\"/>",
            "first_step" => "<path d=\"M12 6C9 4 5 4 3 5v14c2-1 6-1 9 1 3-2 7-2 9-1V5c-2-1-6-1-9 1Z\"/><path d=\"M12 6v14\"/>",
            "diligent_learner" => "<path d=\"M4 5c2-1 5-1 8 1v13c-3-2-6-2-8-1V5Z\"/><path d=\"M12 6c3-2 6-2 8-1v13c-2-1-5-1-8 1V6Z\"/><path d=\"M6 8h3m-3 3h3m6-3h3m-3 3h3\"/>",
            "first_blood" => "<path d=\"M5 21V4\"/><path d=\"M5 5c4-3 8 3 14 0v9c-6 3-10-3-14 0\"/><path d=\"M3 21h5\"/>",
            "flag_hunter" => "<circle cx=\"11\" cy=\"12\" r=\"8\"/><circle cx=\"11\" cy=\"12\" r=\"4\"/><path d=\"m16.5 6.5 4-3m-4 0h4v4\"/>",
            "century_club" => "<path d=\"m12 2 9 7-9 13L3 9l9-7Z\"/><path d=\"m3 9 9 2 9-2M12 11v11m-5-16 5 5 5-5\"/>",
            "terminal_novice" => "<rect x=\"3\" y=\"4\" width=\"18\" height=\"16\" rx=\"2\"/><path d=\"m7 9 3 3-3 3m6 0h4\"/>",
            "academy_graduate" => "<path d=\"m2 9 10-5 10 5-10 5L2 9Z\"/><path d=\"M6 11v5c4 3 8 3 12 0v-5m4-2v7\"/>",
            "full_spectrum_operator" => "<circle cx=\"6\" cy=\"6\" r=\"2.5\"/><circle cx=\"18\" cy=\"6\" r=\"2.5\"/><circle cx=\"12\" cy=\"18\" r=\"2.5\"/><path d=\"m8 7 3 8m5-8-3 8M9 6h6\"/>",
            "lab_regular_10" => "<path d=\"M9 3h6m-5 0v6l-5 9a2 2 0 0 0 2 3h10a2 2 0 0 0 2-3l-5-9V3\"/><path d=\"M7 16h10m-8-3h6\"/>",
            "lab_veteran_25" => "<path d=\"m8 3 5 5m-2-7 4 4m-7 0 5-5m-3 8-5 5a4 4 0 0 0 6 6l5-5m-6-7 7 7m-3-3 4-4 4 4-4 4\"/><path d=\"m5 15 4 4\"/>",
            "ctf_veteran_10" => "<circle cx=\"12\" cy=\"12\" r=\"8\"/><circle cx=\"12\" cy=\"12\" r=\"3\"/><path d=\"M12 2v4m0 12v4M2 12h4m12 0h4\"/>",
            "flag_master_25" => "<path d=\"M5 21V4\"/><path d=\"M5 5c4-3 8 3 14 0v9c-6 3-10-3-14 0\"/><path d=\"m14 7 .6 1.3 1.4.2-1 .9.2 1.4-1.2-.7-1.2.7.2-1.4-1-.9 1.4-.2L14 7Z\"/>",
            "master_learner_10" => "<path d=\"m2 9 10-5 10 5-10 5L2 9Z\"/><path d=\"M6 11v5c4 3 8 3 12 0v-5m-4 6 2 2 4-4\"/>",
            "course_champion_25" => "<path d=\"M8 3h8v5a4 4 0 0 1-8 0V3Z\"/><path d=\"M8 5H4v2a4 4 0 0 0 4 4m8-6h4v2a4 4 0 0 1-4 4m-4 0v5m-4 4h8l-1-4H9l-1 4Z\"/>",
            "dedicated_scholar_25" => "<path d=\"M12 6C9 4 5 4 3 5v14c2-1 6-1 9 1 3-2 7-2 9-1V5c-2-1-6-1-9 1Z\"/><path d=\"M12 6v14m5-9 .5 1.2 1.3.1-1 .9.3 1.3-1.1-.7-1.1.7.3-1.3-1-.9 1.3-.1L17 11Z\"/>",
            "cyber_scholar_100" => "<path d=\"M12 4a4 4 0 0 0-7 3 4 4 0 0 0 0 7 4 4 0 0 0 7 3m0-13a4 4 0 0 1 7 3 4 4 0 0 1 0 7 4 4 0 0 1-7 3m0-13v16m-4-9h4m4-3h-4m-4 6h4m4-1h-4\"/>",
            "week_streak_7" => "<path d=\"M13 2c1 5-4 6-2 10 1-2 3-2 4-5 4 4 5 8 2 12a7 7 0 0 1-12-5c0-4 3-8 8-12Z\"/><path d=\"M12 14c2 2 1 5-1 6a3 3 0 0 1-4-3c0-2 2-3 3-5 0 1 1 2 2 2Z\"/>",
            "month_streak_30" => "<path d=\"m13 2-9 12h7l-1 8 10-13h-7l1-7Z\"/>",
            "century_streak_100" => "<path d=\"M12 3c1 4-3 5-2 8 1-1 2-2 3-4 4 3 6 7 4 11a6 6 0 0 1-11-3c0-4 3-8 6-12Z\"/><path d=\"M7 7 4 5m13 2 3-2M5 11l-3-1m17 1 3-1M8 19l-2 2m10-2 2 2\"/>",
            _ => "<path d=\"m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-3-5.6 3 1.1-6.2L3 9.6l6.2-.9L12 3Z\"/"
        };

        return $"<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">{icon}</svg>";
    }

    private sealed record StreakMetrics(int CurrentStreakDays, Dictionary<int, DateTime> ThresholdDates);

    private sealed record BadgeProgressSnapshot(
        int EnrollmentCount,
        int CompletedLessons,
        int CompletedSimulations,
        int SolvedChallengeCount,
        int TotalPoints,
        int CompletedCourses,
        int FullSpectrumProgress,
        StreakMetrics Streak,
        Dictionary<string, DateTime?> AwardDates);

    // Calculate consecutive learning days in the learner's time zone.
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
            foreach (var target in new[] { WeekStreakTarget, MonthStreakTarget, CenturyStreakTarget })
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
        // Use the solve that first brought the learner to the point target.
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
