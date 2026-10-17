using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;

namespace HackAcad.Web.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("Identity/Account/[action]")]
public partial class AccountController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    UserAccounts accounts, ApplicationDbContext db, EmailVerification verification, PhoneVerification phones,
    AccountRecords records, Microsoft.Extensions.Caching.Memory.IMemoryCache securityCache) : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Register() => User.Identity?.IsAuthenticated == true ? RedirectToAction(nameof(Dashboard)) : View(new RegisterModel());

    [AllowAnonymous, HttpPost, EnableRateLimiting("verification-email")]
    public async Task<IActionResult> Register(RegisterModel input)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction(nameof(Dashboard));
        if (!ModelState.IsValid) return View(input);
        var user = new ApplicationUser { UserName = input.Email.Trim(), Email = input.Email.Trim(), DisplayName = input.DisplayName.Trim() };
        // The public form cannot choose or bind a privileged role.
        var result = await accounts.CreateAsync(user, input.Password, AccountRoles.Student);
        if (!result.Succeeded) { AccountValidation.AddIdentityErrors(ModelState, result); return View(input); }
        TempData["VerificationDeliveryFailed"] = !await verification.SendAsync(user);
        return RedirectToAction(nameof(VerificationPending));
    }

    [AllowAnonymous, HttpGet]
    public IActionResult VerificationPending() => View();

    [AllowAnonymous, HttpGet]
    public IActionResult ResendVerification() => View(new ResendVerificationModel());

    [AllowAnonymous, HttpPost, EnableRateLimiting("verification-email")]
    public async Task<IActionResult> ResendVerification(ResendVerificationModel input)
    {
        if (!ModelState.IsValid) return View(input);
        var user = await users.FindByEmailAsync(input.Email);
        if (user is not null) await verification.SendAsync(user);
        // Same response for unknown, disabled, confirmed and pending accounts.
        TempData["VerificationRequested"] = true;
        return RedirectToAction(nameof(ResendVerification));
    }

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> ConfirmEmail(string? userId, string? email, string? code)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var confirmed = false;
        if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(email)
            && !string.IsNullOrWhiteSpace(code) && code.Length <= 4096)
        {
            var user = await users.FindByIdAsync(userId);
            if (user is not null && !user.IsDisabled && string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
                    confirmed = (await users.ConfirmEmailAsync(user, token)).Succeeded;
                }
                catch (FormatException) { /* Malformed links use the same recovery screen. */ }
            }
        }
        return View(confirmed);
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
                await records.LoginAsync(user.Id, "Signed in");
                if (Url.IsLocalUrl(input.ReturnUrl)) return LocalRedirect(input.ReturnUrl!);
                return RedirectToAction(nameof(Dashboard));
            }
            if (result.RequiresTwoFactor)
            {
                await BeginTwoFactorLogin(user);
                return RedirectToAction(nameof(LoginTwoFactor), new { input.RememberMe, input.ReturnUrl });
            }
            await records.LoginAsync(user.Id, result.IsLockedOut ? "Locked out" : "Failed");
        }
        ModelState.AddModelError("", "Unable to sign in. Check your email and password, verify your email, or try again later if access is temporarily locked. Contact an administrator if access is disabled.");
        return View(input);
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        var sessionId = User.FindFirst(AccountSessions.ClaimName)?.Value;
        await db.AccountSessions.Where(s => s.Id == sessionId && s.UserId == users.GetUserId(User))
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.RevokedUtc, DateTime.UtcNow));
        await signIn.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public async Task<IActionResult> Dashboard()
    {
        if (User.IsInRole(AccountRoles.Admin)) return RedirectToAction("Index", "Admin");
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
        var user = await users.GetUserAsync(User);
        return user is null ? Challenge() : View(await ProfileData(user));
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
        input.HasAvatar = user.AvatarImage is not null;
        input.EmailConfirmed = user.EmailConfirmed;
        if (!ModelState.IsValid) return View(input);
        user.DisplayName = input.DisplayName.Trim();
        user.FullName = input.FullName;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) { AccountValidation.AddIdentityErrors(ModelState, result); return View(input); }
        await signIn.RefreshSignInAsync(user);
        TempData["Success"] = "Your profile has been updated.";
        return RedirectToAction(nameof(Profile));
    }

    private async Task<ProfileModel> ProfileData(ApplicationUser user) => new()
    {
        DisplayName = user.DisplayName, FullName = user.FullName, Email = user.Email ?? "", JoinedDate = user.JoinedDate,
        HasAvatar = user.AvatarImage is not null, EmailConfirmed = user.EmailConfirmed,
        Role = await accounts.Query().Where(u => u.Id == user.Id).Select(u => u.Role).SingleAsync()
    };

    [HttpGet]
    public async Task<IActionResult> Avatar()
    {
        var bytes = await db.Users.Where(u => u.Id == users.GetUserId(User)).Select(u => u.AvatarImage).SingleOrDefaultAsync();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return bytes is null ? NotFound() : File(bytes, "image/jpeg");
    }

    [HttpPost, RequestSizeLimit(2_200_000)]
    public async Task<IActionResult> UploadAvatar(IFormFile? avatar)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (avatar is null) ModelState.AddModelError("avatar", "Choose a JPG, PNG or WebP image up to 2 MB.");
        else
        {
            try { user.AvatarImage = await AvatarImages.ReadAsync(avatar); }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            { ModelState.AddModelError("avatar", error is ArgumentException ? error.Message : "This image could not be read. Choose another JPG, PNG or WebP image."); }
        }
        if (!ModelState.IsValid) return View("Profile", await ProfileData(user));
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("avatar", "The picture could not be saved. Reload the page and try again.");
            return View("Profile", await ProfileData(user));
        }
        TempData["Success"] = "Your profile picture has been updated.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost]
    public async Task<IActionResult> RemoveAvatar()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        user.AvatarImage = null;
        var result = await users.UpdateAsync(user);
        TempData["Success"] = result.Succeeded ? "Your profile picture has been removed." : "The picture could not be removed. Reload and try again.";
        return RedirectToAction(nameof(Profile));
    }

    private AccountSettingsModel SettingsData(ApplicationUser user)
    {
        var pending = phones.PendingNumber(user.Id, user.SecurityStamp ?? "");
        return new()
        {
            Email = user.Email ?? "", EmailConfirmed = user.EmailConfirmed,
            CurrentPhone = user.PhoneNumber, PhoneConfirmed = user.PhoneNumberConfirmed,
            PendingPhone = pending, IsSmsTestMode = phones.IsTestMode,
            Phone = new() { PhoneNumber = pending ?? "" }, TwoFactorEnabled = user.TwoFactorEnabled
        };
    }

    [HttpGet]
    public async Task<IActionResult> Settings()
    {
        var user = await users.GetUserAsync(User);
        return user is null ? Challenge() : View(SettingsData(user));
    }

    [HttpGet]
    public async Task<IActionResult> PhoneNumber()
    {
        var user = await users.GetUserAsync(User);
        return user is null ? Challenge() : View(SettingsData(user));
    }

    [HttpPost, EnableRateLimiting("phone-otp-send")]
    public async Task<IActionResult> SendPhoneCode([Bind(Prefix = "Phone")] PhoneNumberInput input)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (ModelState.IsValid && user.PhoneNumberConfirmed && user.PhoneNumber == input.PhoneNumber)
            ModelState.AddModelError("Phone.PhoneNumber", "This phone number is already verified.");
        if (ModelState.IsValid)
        {
            var error = await phones.SendAsync(user.Id, user.SecurityStamp ?? "", input.PhoneNumber);
            if (error is null)
            {
                TempData["Success"] = phones.IsTestMode ? "A test verification code was captured locally. No SMS was sent." : "Verification SMS requested. Enter the code when it arrives.";
                return RedirectToAction(nameof(PhoneNumber));
            }
            ModelState.AddModelError("Phone.PhoneNumber", error);
        }
        var model = SettingsData(user); model.Phone = input;
        return View("PhoneNumber", model);
    }

    [HttpPost, EnableRateLimiting("phone-otp-check")]
    public async Task<IActionResult> VerifyPhone([Bind(Prefix = "Confirmation")] PhoneCodeInput input)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (ModelState.IsValid)
        {
            var check = await phones.CheckAsync(user.Id, user.SecurityStamp ?? "", input.Code);
            if (check.Number is not null)
            {
                user.PhoneNumber = check.Number; user.PhoneNumberConfirmed = true;
                var result = await users.UpdateSecurityStampAsync(user);
                if (result.Succeeded)
                {
                    await signIn.RefreshSignInAsync(user);
                    TempData["Success"] = "Your phone number is verified. Other existing sessions will need to log in again.";
                    return RedirectToAction(nameof(PhoneNumber));
                }
                ModelState.AddModelError("", "The account changed before the phone number could be saved. Reload and request another code.");
            }
            else ModelState.AddModelError("Confirmation.Code", check.Error!);
        }
        ModelState.SetModelValue("Confirmation.Code", "", "");
        return View("PhoneNumber", SettingsData(user));
    }

    [HttpPost]
    public async Task<IActionResult> CancelPhoneVerification()
    {
        phones.Cancel(users.GetUserId(User)!);
        return RedirectToAction(nameof(PhoneNumber));
    }

    [HttpPost]
    public async Task<IActionResult> RemovePhone()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        user.PhoneNumber = null; user.PhoneNumberConfirmed = false;
        var result = await users.UpdateSecurityStampAsync(user);
        if (result.Succeeded)
        {
            phones.Cancel(user.Id);
            await signIn.RefreshSignInAsync(user);
        }
        TempData["Success"] = result.Succeeded ? "Your phone number has been removed." : "The phone number could not be removed. Reload and try again.";
        return RedirectToAction(nameof(PhoneNumber));
    }

    [HttpGet]
    public async Task<IActionResult> ChangeEmail()
    {
        var user = await users.GetUserAsync(User);
        return user is null ? Challenge() : View(new ChangeEmailModel { CurrentEmail = user.Email ?? "" });
    }

    [HttpPost, EnableRateLimiting("email-change")]
    public async Task<IActionResult> ChangeEmail(ChangeEmailModel input)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        input.CurrentEmail = user.Email ?? "";
        if (!ModelState.IsValid) return View(input);
        if (!await ConfirmCurrentPassword(user, input.CurrentPassword)) return View(input);
        if (string.Equals(user.Email, input.NewEmail, StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError(nameof(input.NewEmail), "Enter an email different from your current address.");
        else if (await users.FindByEmailAsync(input.NewEmail) is not null || await users.FindByNameAsync(input.NewEmail) is not null)
            ModelState.AddModelError(nameof(input.NewEmail), "That email cannot be used. Choose another address.");
        else if (await verification.SendAsync(user, input.NewEmail))
        {
            TempData["EmailChangeRequested"] = input.NewEmail;
            return RedirectToAction(nameof(ChangeEmail));
        }
        else ModelState.AddModelError("", "We couldn't send the confirmation email. Wait a minute and try again, or contact an administrator. Your current email is unchanged.");
        return View(input);
    }

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> ConfirmEmailChange(string? userId, string? email, string? code)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var confirmed = false;
        if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(email) && email.Length <= 256
            && System.Text.RegularExpressions.Regex.IsMatch(email, AccountValidation.EmailPattern)
            && !string.IsNullOrWhiteSpace(code) && code.Length <= 4096)
        {
            var user = await users.FindByIdAsync(userId);
            if (user is not null && !user.IsDisabled && !string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
                    confirmed = (await accounts.ChangeEmailAsync(user, email, token)).Succeeded;
                    if (confirmed && users.GetUserId(User) == user.Id) await signIn.SignOutAsync();
                }
                catch (FormatException) { /* Invalid links show the same recovery screen. */ }
            }
        }
        return View(confirmed);
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
        if (!result.Succeeded) { AccountValidation.AddIdentityErrors(ModelState, result, "NewPassword"); return View(input); }
        await signIn.RefreshSignInAsync(user);
        TempData["Success"] = "Your password has been changed. Other existing sessions will need to sign in again.";
        return RedirectToAction(nameof(Settings));
    }

    [AllowAnonymous, HttpGet]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

}
