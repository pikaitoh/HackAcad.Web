using Microsoft.AspNetCore.Mvc;

namespace HackAcad.Web.Controllers;

public class HomeController : Controller
{
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Index() => User.Identity?.IsAuthenticated == true
        ? RedirectToAction("Dashboard", "Account") : View();

    // Shared component reference is only available in the local development environment.
    public IActionResult UiGuide([FromServices] IWebHostEnvironment environment)
        => environment.IsDevelopment() ? View() : NotFound();

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View();
    }
}
