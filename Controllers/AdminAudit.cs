using HackAcad.Web.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

// ADMIN - Audit logs menu: read management/security history without exposing record mutation endpoints.

public partial class AdminController
{
    // Route the personal activity shortcut to the same audit query with the current-actor filter.
    [HttpGet]
    public IActionResult Activity() => RedirectToAction(nameof(AuditLog), new { mine = true });


    // Read paged audit history and resolve current names for account actors and targets.
    [HttpGet]
    public async Task<IActionResult> AuditLog(string? search, bool mine = false, int page = 1, string? kind = null)
    {
        var query = db.AdminActivities.AsNoTracking();
        search = search?.Trim();
        kind = string.IsNullOrEmpty(kind) ? "all" : kind;
        if (kind == "admin")
            query = query.Where(a => a.TargetType != "Account security");
        else if (kind == "account")
            query = query.Where(a => a.TargetType == "Account security");
        else if (kind != "all")
            return BadRequest();
        if (mine)
        {
            var actor = users.GetUserId(User)!;
            query = query.Where(r => r.ActorUserId == actor);
        }
        if (!string.IsNullOrEmpty(search))
            query = query.Where(r => r.Action.Contains(search) || r.TargetName.Contains(search) || r.Details.Contains(search)
            || (AuditPresentation.AccountTargetTypes.Contains(r.TargetType)
                && db.Users.Any(u => u.Id == r.TargetId && u.DisplayName.Contains(search))));
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 20d)));
        var rows = await query.OrderByDescending(r => r.OccurredUtc).ThenByDescending(r => r.Id).Skip((page - 1) * 20).Take(20).ToListAsync();
        // ADMIN - Resolve display names for account references only; stored audit snapshots are never rewritten.
        var accountIds = rows.Select(r => r.ActorUserId)
            .Concat(rows.Where(r => AuditPresentation.AccountTargetTypes.Contains(r.TargetType)).Select(r => r.TargetId))
            .Distinct().ToList();
        return View(new AuditLogModel
        {
            Kind = kind,
            Search = search,
            Mine = mine,
            Page = page,
            Total = total,
            Records = rows,
            UserNames = await db.Users.AsNoTracking().Where(u => accountIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName)
        });
    }
}
