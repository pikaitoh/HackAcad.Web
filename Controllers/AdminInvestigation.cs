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
        // ADMIN - Use the same complete history as Login logs, including older records without a security event.
        var attempts = await db.LoginRecords.AsNoTracking().Where(e => e.UserId == userId)
            .OrderByDescending(e => e.OccurredUtc).ThenByDescending(e => e.Id)
            .Select(e => new LoginLogRow { Id = e.Id, UserId = userId, OccurredUtc = e.OccurredUtc,
                Outcome = e.Outcome, Device = e.Device, IpAddress = e.IpAddress }).ToListAsync();
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
            LastSuccessfulSignInUtc = attempts.FirstOrDefault(e => e.Outcome.StartsWith("Signed in", StringComparison.Ordinal))?.OccurredUtc,
            LastFailedSignInUtc = attempts.FirstOrDefault(e => !e.Outcome.StartsWith("Signed in", StringComparison.Ordinal))?.OccurredUtc,
            ActiveSessions = user.IsDisabled ? 0 : await db.AccountSessions.CountAsync(s => s.UserId == userId
                && s.RevokedUtc == null && s.ExpiresUtc > now && s.SecurityStamp == user.SecurityStamp),
            Activity = new AdminSignInActivityModel
            {
                UserId = userId,
                Total = attempts.Count,
                Records = attempts
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
        var account = alert.UserId is null ? null : await AccountContextData(alert.UserId);
        ViewData["AccountContext"] = account;
        // ADMIN - Show the complete account history, using the same records and total as Login logs.
        if (account is not null)
        {
            account.Activity.Title = "Related sign-in activity";
            account.Activity.Description = "All recorded sign-ins for this account, including activity before and after the alert. Other accounts are excluded.";
            ViewData["AlertEvidence"] = account.Activity;
            return;
        }
        // ADMIN - Alerts without an available account use all matching security events for their identifier or IP.
        var events = db.SecurityEvents.AsNoTracking();
        string description;
        if (alert.UserId is not null)
        {
            events = events.Where(e => e.UserId == alert.UserId);
            description = "All recorded attempts for this account. The account is no longer available.";
        }
        else if (alert.EventKey.StartsWith("account:", StringComparison.Ordinal) && alert.EventKey.Split(':') is { Length: 3 } parts)
        {
            var subject = parts[1];
            events = events.Where(e => e.SubjectHash == subject);
            description = "All recorded attempts using the same account identifier as this alert. The identifier could not be linked to a registered account.";
        }
        else
        {
            events = events.Where(e => e.IpAddress == alert.IpAddress);
            description = "All recorded security events from this IP address, including attempts for unrecognised accounts.";
        }
        var evidence = new AdminSignInActivityModel
        {
            Title = "Related sign-in activity",
            Description = description,
            UserId = alert.UserId,
            IpAddress = alert.IpAddress,
            ShowUsers = alert.UserId is null,
            Records = await (from entry in events
                join user in db.Users.AsNoTracking() on entry.UserId equals user.Id into matchingUsers
                from user in matchingUsers.DefaultIfEmpty()
                orderby entry.OccurredUtc descending, entry.Id descending
                select new LoginLogRow { Id = entry.Id, UserId = user == null ? "" : user.Id,
                    Username = user == null ? entry.UserId == null ? "Unrecognised account" : "Account no longer available" : user.DisplayName,
                    Email = user == null ? "" : user.Email ?? "", OccurredUtc = entry.OccurredUtc,
                    Outcome = entry.Outcome, Device = entry.Device, IpAddress = entry.IpAddress }).ToListAsync()
        };
        evidence.Total = evidence.Records.Count;
        ViewData["AlertEvidence"] = evidence;
    }
}
