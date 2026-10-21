using System.Security.Claims;
using HackAcad.Web.Data;
using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace HackAcad.Web.ViewComponents;
public class HeaderToolsViewComponent(ApplicationDbContext db, Personalization personalization) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(ClaimsPrincipal? principal = null)
    {
        principal ??= HttpContext.User;
        ViewData["HeaderUser"] = principal;
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await personalization.SyncRewards(id);
        var user = await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => new { u.DisplayName, u.Appearance }).SingleAsync();
        var query = db.UserNotifications.AsNoTracking().Where(n => n.UserId == id);
        return View(new HeaderToolsModel { Name = user.DisplayName, Appearance = user.Appearance, Unread = await query.CountAsync(n => n.ReadUtc == null),
            Items = await query.OrderByDescending(n => n.CreatedUtc).ThenByDescending(n => n.Id).Take(5).ToListAsync() });
    }
}
