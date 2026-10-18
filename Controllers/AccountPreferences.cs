using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HackAcad.Web.Controllers;

public partial class AccountController
{
    [HttpGet]
    public async Task<IActionResult> TimeZone()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        var time = HttpContext.RequestServices.GetRequiredService<UserTimeZone>();
        return View(new TimeZonePreferenceModel { TimeZoneId = time.Id });
    }

    [HttpPost]
    public async Task<IActionResult> TimeZone(TimeZonePreferenceModel input)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (!UserTimeZone.IsSupported(input.TimeZoneId))
            ModelState.AddModelError(nameof(input.TimeZoneId), "Choose a time zone from the list.");
        if (!ModelState.IsValid) return View(input);
        user.TimeZoneId = input.TimeZoneId;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", "Your time zone could not be saved. Reload and try again.");
            return View(input);
        }
        TempData["Success"] = "Time zone saved. Times across HackAcad now use your preference.";
        return RedirectToAction(nameof(TimeZone));
    }
}
