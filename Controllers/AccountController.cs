using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

[Authorize]
[Route("Identity/Account/[action]")]
public class AccountController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    UserAccounts accounts, ApplicationDbContext db) : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Register() => User.Identity?.IsAuthenticated == true ? RedirectToAction(nameof(Dashboard)) : View(new RegisterModel());

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Register(RegisterModel input)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction(nameof(Dashboard));
        if (!ModelState.IsValid) return View(input);
        var user = new ApplicationUser { UserName = input.Email.Trim(), Email = input.Email.Trim(), DisplayName = input.DisplayName.Trim() };
        // The public form cannot choose or bind a privileged role.
        var result = await accounts.CreateAsync(user, input.Password, AccountRoles.Student);
        if (!result.Succeeded) { AddErrors(result); return View(input); }
        await signIn.SignInAsync(user, isPersistent: false);
        TempData["Success"] = "Your account is ready. You are signed in as a Student.";
        return RedirectToAction(nameof(Dashboard));
    }

    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null) => User.Identity?.IsAuthenticated == true
        ? RedirectToAction(nameof(Dashboard)) : View(new LoginModel { ReturnUrl = returnUrl });

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Login(LoginModel input)
    {
        if (!ModelState.IsValid) return View(input);
        var user = await users.FindByEmailAsync(input.Email.Trim());
        if (user is not null && !user.IsDisabled)
        {
            var result = await signIn.PasswordSignInAsync(user, input.Password, input.RememberMe, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                if (Url.IsLocalUrl(input.ReturnUrl)) return LocalRedirect(input.ReturnUrl!);
                return RedirectToAction(nameof(Dashboard));
            }
        }
        ModelState.AddModelError("", "Unable to sign in. Check your email and password. If access is disabled or temporarily locked, contact an administrator or try again later.");
        return View(input);
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public async Task<IActionResult> Dashboard()
    {
        var id = users.GetUserId(User)!;
        var user = await users.FindByIdAsync(id);
        if (user is null) return Challenge();
        return View(new DashboardModel
        {
            DisplayName = user.DisplayName,
            Role = await accounts.Query().Where(u => u.Id == id).Select(u => u.Role).SingleAsync(),
            EnrollmentCount = await db.Enrollments.CountAsync(e => e.UserId == id),
            CompletedActivities = await db.ProgressRecords.CountAsync(p => p.IsCompleted && db.Enrollments.Any(e => e.EnrollmentId == p.EnrollmentId && e.UserId == id)),
            SolvedChallenges = await db.ChallengeSubmissions.Where(s => s.UserId == id && s.IsCorrect).Select(s => s.ChallengeId).Distinct().CountAsync()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var row = await accounts.Query().SingleOrDefaultAsync(u => u.Id == users.GetUserId(User));
        return row is null ? Challenge() : View(new ProfileModel { DisplayName = row.DisplayName, Email = row.Email, Role = row.Role, JoinedDate = row.JoinedDate });
    }

    [HttpPost]
    public async Task<IActionResult> Profile(ProfileModel input)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        // Email, role and join date are display-only and always reloaded from the server.
        input.Email = user.Email ?? "";
        input.Role = await accounts.Query().Where(u => u.Id == user.Id).Select(u => u.Role).SingleAsync();
        input.JoinedDate = user.JoinedDate;
        if (!ModelState.IsValid) return View(input);
        user.DisplayName = input.DisplayName.Trim();
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) { AddErrors(result); return View(input); }
        await signIn.RefreshSignInAsync(user);
        TempData["Success"] = "Your profile has been updated.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordModel());

    [HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordModel input)
    {
        if (!ModelState.IsValid) return View(input);
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        var result = await users.ChangePasswordAsync(user, input.CurrentPassword, input.NewPassword);
        if (!result.Succeeded) { AddErrors(result); return View(input); }
        await signIn.RefreshSignInAsync(user);
        TempData["Success"] = "Your password has been changed. Other existing sessions will need to sign in again.";
        return RedirectToAction(nameof(Profile));
    }

    [AllowAnonymous, HttpGet]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    private void AddErrors(IdentityResult result)
    {
        foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
    }
}
