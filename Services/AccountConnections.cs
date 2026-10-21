using System.Security.Claims;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Services;

public class AccountConnections(ApplicationDbContext db, UserManager<ApplicationUser> users, AccountRecords records)
{
    private const string EmailKey = "AccountEmail";
    private const string NameKey = "AccountName";

    public async Task<List<ConnectedAccount>> ListAsync(ApplicationUser user, ISet<string> available)
    {
        var logins = await users.GetLoginsAsync(user);
        var metadata = await db.UserTokens.AsNoTracking().Where(t => t.UserId == user.Id && (t.Name == EmailKey || t.Name == NameKey)).ToListAsync();
        var password = user.EmailConfirmed && await users.HasPasswordAsync(user);
        return logins.Where(l => ExternalAuthentication.Providers.Contains(l.LoginProvider)).Select(l => new ConnectedAccount
        {
            Provider = l.LoginProvider,
            Email = metadata.FirstOrDefault(t => t.LoginProvider == l.LoginProvider && t.Name == EmailKey)?.Value,
            Name = metadata.FirstOrDefault(t => t.LoginProvider == l.LoginProvider && t.Name == NameKey)?.Value,
            CanDisconnect = user.EmailConfirmed && (password || logins.Any(other => other.LoginProvider != l.LoginProvider && available.Contains(other.LoginProvider)))
        }).ToList();
    }

    // Display snapshots only; OAuth access/refresh tokens are never retained.
    public async Task RememberAsync(ApplicationUser user, ExternalLoginInfo info)
    {
        if (!(await users.GetLoginsAsync(user)).Any(l => l.LoginProvider == info.LoginProvider && l.ProviderKey == info.ProviderKey)) return;
        foreach (var pair in new[] { (EmailKey, info.Principal.FindFirstValue(ClaimTypes.Email)), (NameKey, info.Principal.FindFirstValue(ClaimTypes.Name)) })
        {
            var value = pair.Item2 is { Length: > 0 } text ? text[..Math.Min(text.Length, 256)] : null;
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO `user_tokens` (UserId, LoginProvider, Name, Value) VALUES ({user.Id}, {info.LoginProvider}, {pair.Item1}, {value}) ON DUPLICATE KEY UPDATE Value = VALUES(Value)");
        }
    }

    private async Task LockAsync(ApplicationUser user)
    {
        // Serialize changes to this user's login methods, including concurrent disconnects.
        await db.Users.FromSqlInterpolated($"SELECT * FROM `users` WHERE Id = {user.Id} FOR UPDATE").ToListAsync();
        await db.Entry(user).ReloadAsync();
    }

    public async Task<string?> ConnectAsync(ApplicationUser user, ExternalLoginInfo info)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await LockAsync(user);
        if (user.IsDisabled || !user.EmailConfirmed || await users.IsLockedOutAsync(user))
            return "Your HackAcad account must be active and its email verified before connecting a sign-in account.";
        var selectedEmail = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (!string.Equals(user.Email, selectedEmail, StringComparison.OrdinalIgnoreCase))
            return $"{info.LoginProvider} account not connected. You selected {selectedEmail}, but your verified HackAcad email is {user.Email}. Choose a {info.LoginProvider} account with that email, or change and verify your HackAcad email before connecting.";
        if ((await users.GetLoginsAsync(user)).Any(l => l.LoginProvider == info.LoginProvider)) return $"{info.LoginProvider} is already connected.";
        var result = await users.AddLoginAsync(user, info);
        if (!result.Succeeded) return $"{info.LoginProvider} could not be connected. It may already belong to another account.";
        await RememberAsync(user, info);
        records.AddSecurityActivity(user, $"{info.LoginProvider} connected", $"You can now use {info.LoginProvider} to sign in to this HackAcad account.");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return null;
    }

    public async Task<string?> DisconnectAsync(ApplicationUser user, string provider, string targetKey,
        string method, string methodKey, string stamp, ISet<string> available)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await LockAsync(user);
        if (user.IsDisabled || !user.EmailConfirmed || await users.IsLockedOutAsync(user) || user.SecurityStamp != stamp)
            return "Your account changed or is locked. Sign in again before disconnecting an account.";
        var logins = await users.GetLoginsAsync(user);
        if (!logins.Any(l => l.LoginProvider == provider && l.ProviderKey == targetKey)) return "This connection has already changed. Reload your connected accounts.";
        var password = await users.HasPasswordAsync(user);
        if (!(method == "Password" ? password : method != provider && available.Contains(method) && logins.Any(l => l.LoginProvider == method && l.ProviderKey == methodKey)))
            return "Verify a sign-in method that will remain connected.";
        if (!password && !logins.Any(l => l.LoginProvider != provider && available.Contains(l.LoginProvider)))
            return "Keep at least one available sign-in method. Set a HackAcad password or connect another account first.";
        var result = await users.RemoveLoginAsync(user, provider, targetKey);
        if (!result.Succeeded) return "The connection changed. Reload and try again.";
        await db.UserTokens.Where(t => t.UserId == user.Id && t.LoginProvider == provider && (t.Name == EmailKey || t.Name == NameKey)).ExecuteDeleteAsync();
        if (!(await users.UpdateSecurityStampAsync(user)).Succeeded) return "Your account changed. Sign in again and retry.";
        records.AddSecurityActivity(user, $"{provider} disconnected", $"{provider} can no longer sign in to this HackAcad account. Your profile, role and learning records are unchanged.");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return null;
    }
}
