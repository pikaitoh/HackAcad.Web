using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

// ADMIN - Bring account checks and relevant sign-in evidence into support and alert details.
public partial class AdminController
{
    private async Task<AdminAccountContextModel?> AccountContextData(string userId)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId);
        if (user is null) return null;
        var now = DateTime.UtcNow;
        var attempts = db.SecurityEvents.AsNoTracking().Where(e => e.UserId == userId);
        var model = new AdminAccountContextModel
        {
            UserId = userId,
            DisplayName = user.DisplayName,
            Email = user.Email ?? "",
            Disabled = user.IsDisabled,
            EmailConfirmed = user.EmailConfirmed,
            LockedUntilUtc = user.LockoutEnabled && user.LockoutEnd > new DateTimeOffset(now)
                ? user.LockoutEnd.Value.UtcDateTime : null,
            FailedAttempts = user.AccessFailedCount,
            TwoFactorMethod = !user.TwoFactorEnabled ? "Not enabled" : user.SmsTwoFactorEnabled ? "SMS verification" : "Authenticator app",
            LastPasswordChangedUtc = user.LastPasswordChangedUtc,
            LastSuccessfulSignInUtc = await attempts.Where(e => e.Success).MaxAsync(e => (DateTime?)e.OccurredUtc),
            LastFailedSignInUtc = await attempts.Where(e => !e.Success).MaxAsync(e => (DateTime?)e.OccurredUtc),
            ActiveSessions = user.IsDisabled ? 0 : await db.AccountSessions.CountAsync(s => s.UserId == userId
                && s.RevokedUtc == null && s.ExpiresUtc > now && s.SecurityStamp == user.SecurityStamp),
            Activity = new AdminSignInActivityModel
            {
                UserId = userId,
                Total = await attempts.CountAsync(),
                Records = await attempts.OrderByDescending(e => e.OccurredUtc).ThenByDescending(e => e.Id).Take(8)
                    .Select(e => new LoginLogRow { Id = e.Id, UserId = userId, OccurredUtc = e.OccurredUtc,
                        Outcome = e.Outcome, Device = e.Device, IpAddress = e.IpAddress }).ToListAsync()
            }
        };
        if (user.PasswordHash is not null)
            model.SignInMethods.Add(user.PhoneNumberConfirmed ? "Email or verified phone + password" : "Email + password");
        model.SignInMethods.AddRange(await db.UserLogins.AsNoTracking().Where(l => l.UserId == userId)
            .Select(l => l.LoginProvider).Distinct().OrderBy(p => p).ToListAsync());
        if (user.IsDisabled)
            model.DisabledReason = await db.AdminActivities.AsNoTracking()
                .Where(a => a.TargetType == "User" && a.TargetId == userId && a.Action == "Disabled account")
                .OrderByDescending(a => a.OccurredUtc).ThenByDescending(a => a.Id).Select(a => a.Details).FirstOrDefaultAsync();
        return model;
    }

    private async Task LoadAlertContext(SecurityAlert alert)
    {
        ViewData["AccountContext"] = alert.UserId is null ? null : await AccountContextData(alert.UserId);
        // ADMIN - Use the detector's ten-minute window. Session creation can precede its successful-login record.
        var from = alert.CreatedUtc.AddMinutes(-10);
        var to = alert.EventKey.StartsWith("device:", StringComparison.Ordinal) ? alert.CreatedUtc.AddMinutes(1) : alert.CreatedUtc;
        var events = db.SecurityEvents.AsNoTracking().Where(e => e.OccurredUtc >= from && e.OccurredUtc <= to);
        string description;
        if (alert.UserId is not null)
        {
            events = events.Where(e => e.UserId == alert.UserId);
            description = "Sign-in attempts for this account around the alert. Other accounts are excluded.";
            if (alert.EventKey.StartsWith("device:", StringComparison.Ordinal))
                events = events.Where(e => e.IpAddress == alert.IpAddress);
        }
        else if (alert.EventKey.StartsWith("account:", StringComparison.Ordinal) && alert.EventKey.Split(':') is { Length: 3 } parts)
        {
            var subject = parts[1];
            events = events.Where(e => e.SubjectHash == subject);
            description = "Attempts using the same account identifier as this alert. The identifier could not be linked to a registered account.";
        }
        else
        {
            events = events.Where(e => e.IpAddress == alert.IpAddress);
            description = "Attempts from this IP address around the alert, including attempts for unrecognised accounts.";
        }
        ViewData["AlertEvidence"] = new AdminSignInActivityModel
        {
            Title = "Related sign-in activity",
            Description = description,
            UserId = alert.UserId,
            ShowUsers = alert.UserId is null,
            FromUtc = from,
            ToUtc = to,
            Total = await events.CountAsync(),
            Records = await (from entry in events
                join user in db.Users.AsNoTracking() on entry.UserId equals user.Id into matchingUsers
                from user in matchingUsers.DefaultIfEmpty()
                orderby entry.OccurredUtc descending, entry.Id descending
                select new LoginLogRow { Id = entry.Id, UserId = entry.UserId ?? "",
                    Username = user == null ? entry.UserId == null ? "Unrecognised account" : "Account no longer available" : user.DisplayName,
                    Email = user == null ? "" : user.Email ?? "", OccurredUtc = entry.OccurredUtc,
                    Outcome = entry.Outcome, Device = entry.Device, IpAddress = entry.IpAddress }).Take(10).ToListAsync()
        };
    }
}
