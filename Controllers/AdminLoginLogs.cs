using HackAcad.Web.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

// ADMIN - Login logs menu: search recorded sign-in outcomes using the viewer's saved time zone.

public partial class AdminController
{
    // Filter sign-in records by identity, outcome and inclusive local dates using a half-open UTC range.
    [HttpGet]
    public async Task<IActionResult> LoginLogs(string? search, string? result, DateOnly? from, DateOnly? to, int page = 1, string? userId = null, string? ipAddress = null)
    {
        ViewData["ReadOnlyAdmin"] = await db.Admins.AnyAsync(a => a.UserId == users.GetUserId(User) && a.IsReadOnly);
        var model = new LoginLogsModel { Search = search?.Trim(), Result = result, From = from, To = to, UserId = userId,
            IpAddress = string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress.Trim() };
        if (from > to)
            ModelState.AddModelError("To", "Choose an end date on or after the start date.");
        DateTime? start = null, end = null;
        try
        {
            if (from.HasValue)
                start = userTime.StartOfDayUtc(from.Value);
        }
        catch (ArgumentException) { ModelState.AddModelError("From", "Choose a valid start date."); }
        try
        {
            if (to.HasValue)
                end = userTime.StartOfDayUtc(to.Value.AddDays(1));
        }
        catch (ArgumentException) { ModelState.AddModelError("To", "Choose a valid end date before 31 Dec 9999."); }
        if (!ModelState.IsValid)
            return View(model);
        var query = from record in db.LoginRecords.AsNoTracking()
                    join user in db.Users on record.UserId equals user.Id
                    select new LoginLogRow
                    {
                        Id = record.Id,
                        UserId = user.Id,
                        Username = user.DisplayName,
                        Email = user.Email ?? "",
                        OccurredUtc = record.OccurredUtc,
                        Outcome = record.Outcome,
                        Device = record.Device,
                        IpAddress = record.IpAddress
                    };
        // ADMIN - IP investigations use the same events as the alert, including attempts without a registered account.
        if (model.IpAddress is not null)
            query = from record in db.SecurityEvents.AsNoTracking().Where(e => e.IpAddress == model.IpAddress)
                    join user in db.Users on record.UserId equals user.Id into matchingUsers
                    from user in matchingUsers.DefaultIfEmpty()
                    select new LoginLogRow
                    {
                        Id = record.Id,
                        UserId = user == null ? "" : user.Id,
                        Username = user == null ? record.UserId == null ? "Unrecognised account" : "Account no longer available" : user.DisplayName,
                        Email = user == null ? "" : user.Email ?? "",
                        OccurredUtc = record.OccurredUtc,
                        Outcome = record.Outcome,
                        Device = record.Device,
                        IpAddress = record.IpAddress
                    };
        if (!string.IsNullOrEmpty(userId))
            query = query.Where(r => r.UserId == userId);
        if (!string.IsNullOrEmpty(model.Search))
            query = query.Where(r => r.Username.Contains(model.Search) || r.Email.Contains(model.Search));
        if (result == "success")
            query = query.Where(r => r.Outcome.StartsWith("Signed in"));
        if (result == "failed")
            query = query.Where(r => r.Outcome == "Failed" || r.Outcome == "Verification failed");
        if (result == "locked")
            query = query.Where(r => r.Outcome == "Locked out");
        if (start.HasValue)
            query = query.Where(r => r.OccurredUtc >= start.Value);
        if (end.HasValue)
            query = query.Where(r => r.OccurredUtc < end.Value);
        model.Total = await query.CountAsync();
        model.Page = Math.Clamp(page, 1, model.PageCount);
        model.Records = await query.OrderByDescending(r => r.OccurredUtc).ThenByDescending(r => r.Id)
            .Skip((model.Page - 1) * 20).Take(20).ToListAsync();
        return View(model);
    }
}
