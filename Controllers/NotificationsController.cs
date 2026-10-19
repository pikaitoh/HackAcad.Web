using System.Security.Claims;
using HackAcad.Web.Data;
using HackAcad.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace HackAcad.Web.Controllers;
[Authorize, AutoValidateAntiforgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class NotificationsController(ApplicationDbContext db, TimeProvider clock) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int page = 1)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var query = db.UserNotifications.AsNoTracking().Where(n => n.UserId == userId);
        var total = await query.CountAsync(); page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 20d)));
        return View(new NotificationListModel { Total = total, Page = page, Unread = await query.CountAsync(n => n.ReadUtc == null),
            Items = await query.OrderByDescending(n => n.CreatedUtc).ThenByDescending(n => n.Id).Skip((page - 1) * 20).Take(20).ToListAsync() });
    }
    [HttpPost]
    public async Task<IActionResult> Open(long id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var notification = await db.UserNotifications.SingleOrDefaultAsync(n => n.Id == id && n.UserId == userId);
        if (notification is null) return NotFound();
        notification.ReadUtc ??= clock.GetUtcNow().UtcDateTime; await db.SaveChangesAsync();
        return LocalRedirect(Url.IsLocalUrl(notification.Link) ? notification.Link : "/Notifications");
    }
    [HttpPost]
    public async Task<IActionResult> Read(long id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!; var now = clock.GetUtcNow().UtcDateTime;
        var query = db.UserNotifications.Where(n => n.Id == id && n.UserId == userId);
        if (!await query.AnyAsync()) return NotFound();
        await query.Where(n => n.ReadUtc == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadUtc, now));
        return RedirectToAction(nameof(Index));
    }
    [HttpPost]
    public async Task<IActionResult> ReadAll(string? returnUrl)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!; var now = clock.GetUtcNow().UtcDateTime;
        await db.UserNotifications.Where(n => n.UserId == userId && n.ReadUtc == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadUtc, now));
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/Notifications");
    }
}
