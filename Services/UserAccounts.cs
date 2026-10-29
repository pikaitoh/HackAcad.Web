using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Services;

// ADMIN - Resolve business roles from role tables and project account data for the Users menu.

public static class AccountRoles
{
    public const string Student = "Student";
    public const string Instructor = "Instructor";
    public const string Admin = "Admin";
    public const string Learner = Student + "," + Instructor;
    public static readonly string[] All = [Student, Instructor, Admin];
}

// Account creation and role changes share one transaction with Identity.
// The existing Students / Instructors / Admins tables remain authoritative.
public class UserAccounts(ApplicationDbContext db, UserManager<ApplicationUser> users, AccountRecords records)
{
    // Project current account details; Admin role takes precedence over any retained learner role marker.
    public IQueryable<UserRow> Query() => db.Users.AsNoTracking().Select(u => new UserRow
    {
        Id = u.Id,
        DisplayName = u.DisplayName,
        FullName = u.FullName,
        Email = u.Email ?? "",
        IsReadOnlyAdmin = db.Admins.Any(a => a.UserId == u.Id && a.IsReadOnly),
        IsDisabled = u.IsDisabled,
        EmailConfirmed = u.EmailConfirmed,
        JoinedDate = u.JoinedDate,
        LastPasswordChangedUtc = u.LastPasswordChangedUtc,
        LastLoginUtc = db.LoginRecords.Where(r => r.UserId == u.Id && r.Outcome.StartsWith("Signed in"))
            .Select(r => (DateTime?)r.OccurredUtc).Max(),
        Role = db.Admins.Any(a => a.UserId == u.Id) ? AccountRoles.Admin
            : db.Instructors.Any(t => t.UserId == u.Id) ? AccountRoles.Instructor
            : db.Students.Any(s => s.UserId == u.Id) ? AccountRoles.Student : "Unassigned"
    });

    public async Task<IdentityResult> CreateAsync(ApplicationUser user, string password, string role, string? actorId = null)
    {
        if (!AccountRoles.All.Contains(role))
            return Error("Choose a valid role.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
            return result;
        AddRole(user.Id, role);
        if (actorId is not null)
            records.AddActivity(actorId, "Created account", "User", user.Id, user.DisplayName, $"Role: {role}");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> CreateExternalAsync(ApplicationUser user, UserLoginInfo login)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await users.CreateAsync(user);
        if (!result.Succeeded)
            return result;
        result = await users.AddLoginAsync(user, login);
        if (!result.Succeeded)
            return result;
        AddRole(user.Id, AccountRoles.Student);
        records.AddSecurityActivity(user, $"{login.LoginProvider} connected", $"{login.LoginProvider} was connected when you created your HackAcad account.");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> ChangeEmailAsync(ApplicationUser user, string newEmail, string token)
    {
        // Keep the login name and confirmed email together, including uniqueness/concurrency failures.
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await users.ChangeEmailAsync(user, newEmail, token);
        if (result.Succeeded)
            result = await users.SetUserNameAsync(user, newEmail);
        if (result.Succeeded)
        {
            records.AddSecurityActivity(user, "Account email changed", $"Your HackAcad email changed to {newEmail}. Existing Google and GitHub connections remain linked.");
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        else
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
        }
        return result;
    }

    private void AddRole(string userId, string role)
    {
        switch (role)
        {
            case AccountRoles.Student:
                db.Students.Add(new Student { UserId = userId });
                break;
            case AccountRoles.Instructor:
                db.Instructors.Add(new Instructor { UserId = userId });
                break;
            case AccountRoles.Admin:
                db.Admins.Add(new Admin { UserId = userId });
                break;
        }
    }

    private static IdentityResult Error(string message) => IdentityResult.Failed(new IdentityError { Description = message });
}
