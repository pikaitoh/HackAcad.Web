using System.Security.Claims;
using HackAcad.Web.Data;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace HackAcad.Web.Controllers;
[Authorize, AutoValidateAntiforgeryToken]
public class PreferencesController(ApplicationDbContext db, Personalization personalization) : Controller
{
    [HttpPost]
    public async Task<IActionResult> Appearance(string value, string? returnUrl)
    {
        if (value is not ("light" or "dark" or "system")) return BadRequest();
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Appearance, value));
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return NoContent();
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/Identity/Account/Profile");
    }
    [HttpPost]
    public async Task<IActionResult> Banner(string key)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await personalization.SyncRewards(id);
        var choices = await personalization.Banners(id, User.IsInRole(AccountRoles.Student));
        if (!choices.Any(c => c.Key == key && c.Unlocked)) return BadRequest("Complete the required course before selecting this banner.");
        await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.BannerKey, key));
        TempData["Success"] = "Profile banner updated.";
        return LocalRedirect("/Identity/Account/Profile");
    }
}
