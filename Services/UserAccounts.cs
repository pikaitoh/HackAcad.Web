using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Services;

public static class AccountRoles
{
    public const string Student = "Student";
    public const string Teacher = "Teacher";
    public const string Admin = "Admin";
    public static readonly string[] All = [Student, Teacher, Admin];
}

// Account creation and role changes share one transaction with Identity.
// The existing Students / Teachers / Admins tables remain authoritative.
public class UserAccounts(ApplicationDbContext db, UserManager<ApplicationUser> users)
{
    public IQueryable<UserRow> Query() => db.Users.AsNoTracking().Select(u => new UserRow
    {
        Id = u.Id, DisplayName = u.DisplayName, Email = u.Email ?? "",
        IsDisabled = u.IsDisabled, JoinedDate = u.JoinedDate,
        Role = db.Admins.Any(a => a.UserId == u.Id) ? AccountRoles.Admin
            : db.Teachers.Any(t => t.UserId == u.Id) ? AccountRoles.Teacher
            : db.Students.Any(s => s.UserId == u.Id) ? AccountRoles.Student : "Unassigned"
    });

    public async Task<IdentityResult> CreateAsync(ApplicationUser user, string password, string role)
    {
        if (!AccountRoles.All.Contains(role)) return Error("Choose a valid role.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded) return result;
        AddRole(user.Id, role);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(ApplicationUser user, EditUserModel input, string actorId)
    {
        if (!AccountRoles.All.Contains(input.Role)) return Error("Choose a valid role.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var currentRole = await Query().Where(u => u.Id == user.Id).Select(u => u.Role).SingleAsync();
        if (user.Id == actorId && (input.IsDisabled || input.Role != AccountRoles.Admin))
            return Error("You cannot disable your own account or remove your own Admin role.");
        if (input.Role != AccountRoles.Teacher && await db.Courses.AnyAsync(c => c.TeacherId == user.Id))
            return Error("This teacher owns courses. Transfer those courses before changing their role, or disable the account.");

        var revokeSessions = currentRole != input.Role || user.IsDisabled != input.IsDisabled;
        user.DisplayName = input.DisplayName.Trim();
        user.Email = input.Email.Trim();
        user.UserName = user.Email;
        user.IsDisabled = input.IsDisabled;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) return result;
        // Normalize any old overlapping role markers to the selected role.
        if (input.Role != AccountRoles.Student) db.Students.RemoveRange(await db.Students.Where(s => s.UserId == user.Id).ToListAsync());
        if (input.Role != AccountRoles.Teacher) db.Teachers.RemoveRange(await db.Teachers.Where(t => t.UserId == user.Id).ToListAsync());
        if (input.Role != AccountRoles.Admin) db.Admins.RemoveRange(await db.Admins.Where(a => a.UserId == user.Id).ToListAsync());
        await db.SaveChangesAsync();
        var hasRole = input.Role switch
        {
            AccountRoles.Student => await db.Students.AnyAsync(s => s.UserId == user.Id),
            AccountRoles.Teacher => await db.Teachers.AnyAsync(t => t.UserId == user.Id),
            _ => await db.Admins.AnyAsync(a => a.UserId == user.Id)
        };
        if (!hasRole) AddRole(user.Id, input.Role);
        await db.SaveChangesAsync();
        if (revokeSessions)
        {
            result = await users.UpdateSecurityStampAsync(user);
            if (!result.Succeeded) return result;
        }
        await transaction.CommitAsync();
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(ApplicationUser user, string actorId)
    {
        if (user.Id == actorId) return Error("You cannot delete your own account.");
        if (await db.Courses.AnyAsync(c => c.TeacherId == user.Id))
            return Error("This teacher owns courses. Transfer those courses first, or disable the account to keep their content.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        // These legacy entities have scalar UserId fields without user FKs.
        db.Enrollments.RemoveRange(await db.Enrollments.Where(e => e.UserId == user.Id).ToListAsync());
        db.ChallengeSubmissions.RemoveRange(await db.ChallengeSubmissions.Where(s => s.UserId == user.Id).ToListAsync());
        await db.SaveChangesAsync();
        var result = await users.DeleteAsync(user);
        if (!result.Succeeded) return result;
        await transaction.CommitAsync();
        return IdentityResult.Success;
    }

    private void AddRole(string userId, string role)
    {
        switch (role)
        {
            case AccountRoles.Student: db.Students.Add(new Student { UserId = userId }); break;
            case AccountRoles.Teacher: db.Teachers.Add(new Teacher { UserId = userId }); break;
            case AccountRoles.Admin: db.Admins.Add(new Admin { UserId = userId }); break;
        }
    }

    private static IdentityResult Error(string message) => IdentityResult.Failed(new IdentityError { Description = message });
}
