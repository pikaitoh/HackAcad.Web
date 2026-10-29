using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

// ADMIN - Users menu: inspect accounts, disable/reactivate access, and preserve the last active manager.

public partial class AdminController
{
    // Apply account filters before paging; the account projection supplies the current effective role.
    public async Task<IActionResult> Users(string? search, string? role, string? status, int page = 1)
    {
        var query = accounts.Query();
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(u => u.DisplayName.Contains(search) || u.Email.Contains(search));
        if (AccountRoles.All.Contains(role))
            query = query.Where(u => u.Role == role);
        if (status == "active")
            query = query.Where(u => !u.IsDisabled);
        if (status == "disabled")
            query = query.Where(u => u.IsDisabled);
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 20d)));
        return View(new UserListModel
        {
            Search = search,
            Role = role,
            Status = status,
            Total = total,
            Page = page,
            Users = await query.OrderByDescending(u => u.JoinedDate).ThenBy(u => u.Id).Skip((page - 1) * 20).Take(20).ToListAsync()
        });
    }

    // Load one account for inspection; the Admin interface does not edit personal credentials.
    [HttpGet]
    public async Task<IActionResult> UserDetails(string id)
    {
        var row = await accounts.Query().SingleOrDefaultAsync(u => u.Id == id);
        return row is null ? NotFound() : View(row);
    }

    // Change another account's access, revoke active sessions and record the reason atomically.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AccountStatus(string id, AccountStatusInput input)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null)
            return NotFound();
        if (id == users.GetUserId(User))
            ModelState.AddModelError("", "You cannot change your own account access.");
        if (!ModelState.IsValid)
        {
            ViewData["StatusInput"] = input;
            return View("UserDetails", await accounts.Query().SingleAsync(u => u.Id == id));
        }
        if (user.IsDisabled == input.Disable)
            return RedirectToAction(nameof(UserDetails), new { id });
        await using var transaction = await db.Database.BeginTransactionAsync();
        // ADMIN - Lock administrator role rows before checking the last-manager invariant.
        var administrators = await db.Admins.FromSqlRaw("SELECT * FROM `admins` FOR UPDATE").ToListAsync();
        var actorId = users.GetUserId(User)!;
        var actorStamp = User.FindFirst("AspNet.Identity.SecurityStamp")?.Value;
        if (!administrators.Any(a => a.UserId == actorId && !a.IsReadOnly)
            || !await db.Users.AsNoTracking().AnyAsync(u => u.Id == actorId && !u.IsDisabled && u.EmailConfirmed && u.TwoFactorEnabled && u.SecurityStamp == actorStamp))
            return Forbid();
        await db.Entry(user).ReloadAsync();
        if (user.IsDisabled == input.Disable)
            return RedirectToAction(nameof(UserDetails), new { id });
        if (input.Disable && administrators.Any(a => a.UserId == id && !a.IsReadOnly)
            && !await db.Admins.AnyAsync(a => a.UserId != id && !a.IsReadOnly && !a.User!.IsDisabled))
            return Conflict("Keep at least one active administrator with management access.");
        user.IsDisabled = input.Disable;
        var result = await users.UpdateAsync(user);
        // ADMIN - A changed security stamp invalidates existing credentials; session revocation removes active browser access.
        if (result.Succeeded)
            result = await users.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            AccountValidation.AddIdentityErrors(ModelState, result);
            return View("UserDetails", await accounts.Query().SingleAsync(u => u.Id == id));
        }
        await db.AccountSessions.Where(s => s.UserId == id && s.RevokedUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedUtc, DateTime.UtcNow));
        records.AddActivity(users.GetUserId(User)!, input.Disable ? "Disabled account" : "Reactivated account", "User", id, user.DisplayName, input.Reason.Trim(), input.Disable ? "Active" : "Disabled", input.Disable ? "Disabled" : "Active");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["Success"] = input.Disable ? "Account disabled. Existing sessions have been revoked; records are preserved." : "Account reactivated. The user can log in again.";
        return RedirectToAction(nameof(UserDetails), new { id });
    }
}
