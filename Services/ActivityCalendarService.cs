using System.Globalization;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Services;

public sealed class ActivityCalendarService(ApplicationDbContext db, UserTimeZone userTime, TimeProvider clock)
{
    public async Task<ActivityCalendarViewModel> BuildAsync(
        ApplicationUser user,
        IReadOnlyCollection<int> enrollmentIds,
        int? requestedYear)
    {
        var today = DateOnly.FromDateTime(userTime.Convert(clock.GetUtcNow().UtcDateTime).DateTime);
        var joinedUtc = DateTime.SpecifyKind(user.JoinedDate, DateTimeKind.Utc);
        var joinedDate = DateOnly.FromDateTime(userTime.Convert(joinedUtc).DateTime);
        var firstYear = Math.Min(joinedDate.Year, today.Year);
        var currentYear = today.Year;
        var years = Enumerable.Range(firstYear, currentYear - firstYear + 1).Reverse().ToList();
        var year = requestedYear is { } requested && years.Contains(requested) ? requested : currentYear;

        var yearStart = new DateOnly(year, 1, 1);
        var nextYearStart = yearStart.AddYears(1);
        var firstActivityDate = year == joinedDate.Year ? joinedDate : yearStart;
        var lastActivityDate = year == currentYear ? today : nextYearStart.AddDays(-1);
        var queryStartUtc = userTime.StartOfDayUtc(yearStart);
        if (year == joinedDate.Year && joinedUtc > queryStartUtc) queryStartUtc = joinedUtc;
        var queryEndDate = year == currentYear ? today.AddDays(1) : nextYearStart;
        var queryEndUtc = userTime.StartOfDayUtc(queryEndDate);

        var counts = new Dictionary<DateOnly, ActivityCounts>();
        if (enrollmentIds.Count > 0 && queryEndUtc > queryStartUtc)
        {
            var completions = await db.ProgressRecords.AsNoTracking()
                .Where(p => enrollmentIds.Contains(p.EnrollmentId)
                    && p.IsCompleted
                    && p.CompletedUtc.HasValue
                    && p.CompletedUtc.Value >= queryStartUtc
                    && p.CompletedUtc.Value < queryEndUtc)
                .Select(p => new { p.ContentType, p.CompletedUtc })
                .ToListAsync();

            foreach (var completion in completions)
            {
                var date = DateOnly.FromDateTime(userTime.Convert(completion.CompletedUtc!.Value).DateTime);
                if (date < firstActivityDate || date > lastActivityDate) continue;
                var day = GetCounts(counts, date);
                switch (completion.ContentType)
                {
                    case ProgressContentType.Lesson: day.Lessons++; break;
                    case ProgressContentType.Challenge: day.CtfSolves++; break;
                    case ProgressContentType.Simulation: day.Labs++; break;
                }
            }

            var courseFinishes = await db.Enrollments.AsNoTracking()
                .Where(e => enrollmentIds.Contains(e.EnrollmentId)
                    && e.FinishedUtc.HasValue
                    && e.FinishedUtc.Value >= queryStartUtc
                    && e.FinishedUtc.Value < queryEndUtc)
                .Select(e => e.FinishedUtc)
                .ToListAsync();

            foreach (var finishUtc in courseFinishes)
            {
                var date = DateOnly.FromDateTime(userTime.Convert(finishUtc!.Value).DateTime);
                if (date < firstActivityDate || date > lastActivityDate) continue;
                GetCounts(counts, date).CoursesFinished++;
            }
        }

        var calendarStart = yearStart.AddDays(-(int)yearStart.DayOfWeek);
        var calendarEnd = lastActivityDate < nextYearStart.AddDays(-1) ? lastActivityDate : nextYearStart.AddDays(-1);
        var weekCount = ((calendarEnd.DayNumber - calendarStart.DayNumber) / 7) + 1;
        var calendar = new ActivityCalendarViewModel
        {
            SelectedYear = year,
            TotalEvents = counts.Values.Sum(day => day.TotalEvents),
            TimeZoneId = userTime.Id,
            AvailableYears = years
        };

        var lastVisibleMonth = year == currentYear ? today.Month : 12;
        for (var month = 1; month <= lastVisibleMonth; month++)
        {
            var monthStart = new DateOnly(year, month, 1);
            calendar.Months.Add(new ActivityCalendarMonthViewModel
            {
                Label = monthStart.ToString("MMM", CultureInfo.InvariantCulture),
                WeekColumn = ((monthStart.DayNumber - calendarStart.DayNumber) / 7) + 1
            });
        }

        for (var week = 0; week < weekCount; week++)
        {
            var weekModel = new ActivityCalendarWeekViewModel();
            for (var weekday = 0; weekday < 7; weekday++)
            {
                var date = calendarStart.AddDays(week * 7 + weekday);
                var inRange = date >= firstActivityDate && date <= calendarEnd && date.Year == year;
                counts.TryGetValue(date, out var dayCounts);
                weekModel.Days.Add(new ActivityCalendarDayViewModel
                {
                    Date = date,
                    IsInActivityRange = inRange,
                    Lessons = inRange ? dayCounts?.Lessons ?? 0 : 0,
                    CtfSolves = inRange ? dayCounts?.CtfSolves ?? 0 : 0,
                    Labs = inRange ? dayCounts?.Labs ?? 0 : 0,
                    CoursesFinished = inRange ? dayCounts?.CoursesFinished ?? 0 : 0
                });
            }
            calendar.Weeks.Add(weekModel);
        }

        return calendar;
    }

    private static ActivityCounts GetCounts(IDictionary<DateOnly, ActivityCounts> counts, DateOnly date)
    {
        if (!counts.TryGetValue(date, out var day)) counts[date] = day = new ActivityCounts();
        return day;
    }

    private sealed class ActivityCounts
    {
        public int Lessons { get; set; }
        public int CtfSolves { get; set; }
        public int Labs { get; set; }
        public int CoursesFinished { get; set; }
        public int TotalEvents => Lessons + CtfSolves + Labs + CoursesFinished;
    }
}
