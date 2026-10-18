using System.Security.Claims;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

public partial class AccountController
{
    private Task<ApplicationUser?> VerifiedPhoneUser(string number) => db.Users.SingleOrDefaultAsync(u => u.PhoneNumberConfirmed && u.PhoneNumber == number);

    private SmsChallengeModel SmsScreen(string description, string submit, string resend, string back, int seconds, bool settings = false,
        bool rememberMe = false, string? returnUrl = null) => new()
    { Description = description, SubmitAction = submit, ResendAction = resend, BackAction = back, ResendSeconds = seconds,
        IsTestMode = phones.IsTestMode, SettingsLayout = settings, RememberMe = rememberMe, ReturnUrl = returnUrl };

    [HttpPost, EnableRateLimiting("security-action")]
    public async Task<IActionResult> BeginSmsTwoFactor(CurrentPasswordModel input)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (user.TwoFactorEnabled) return RedirectToAction(nameof(TwoFactor));
        if (!user.PhoneNumberConfirmed || user.PhoneNumber is null) ModelState.AddModelError("", "Add and verify your phone number first.");
        if (!ModelState.IsValid || !await ConfirmCurrentPassword(user, input.CurrentPassword)) return View("TwoFactor", await TwoFactorData(user));
        var error = await phones.SendAsync(user.Id, user.SecurityStamp ?? "", user.PhoneNumber!, "sms-setup");
        if (error is not null) { ModelState.AddModelError("", error); return View("TwoFactor", await TwoFactorData(user)); }
        return RedirectToAction(nameof(EnableSmsTwoFactor));
    }

    private SmsChallengeModel SmsSetupScreen(ApplicationUser user) => SmsScreen("Enter the code sent to your verified phone number to turn on SMS two-factor authentication.",
        nameof(EnableSmsTwoFactor), nameof(ResendSmsSetup), nameof(CancelSmsSetup), phones.ResendWaitSeconds(user.Id, user.SecurityStamp ?? "", "sms-setup"), settings: true);

    [HttpGet]
    public async Task<IActionResult> EnableSmsTwoFactor()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        return !user.TwoFactorEnabled && phones.PendingNumber(user.Id, user.SecurityStamp ?? "", "sms-setup") == user.PhoneNumber
            && user.PhoneNumberConfirmed && user.PhoneNumber is not null ? View("SmsChallenge", SmsSetupScreen(user)) : RedirectToAction(nameof(TwoFactor));
    }

    [HttpPost, EnableRateLimiting("security-action")]
    public async Task<IActionResult> EnableSmsTwoFactor(PhoneCodeInput input)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (user.TwoFactorEnabled || !user.PhoneNumberConfirmed || user.PhoneNumber is null) return RedirectToAction(nameof(TwoFactor));
        if (await users.IsLockedOutAsync(user)) ModelState.AddModelError("Code", "Too many failed attempts. Try again in 5 minutes.");
        if (ModelState.IsValid)
        {
            var result = await phones.CheckAsync(user.Id, user.SecurityStamp ?? "", input.Code, "sms-setup");
            if (result.Number == user.PhoneNumber)
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                user.SmsTwoFactorEnabled = true;
                if (!(await users.SetTwoFactorEnabledAsync(user, true)).Succeeded) return Conflict();
                var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))?.ToArray();
                if (codes is null || !(await users.UpdateSecurityStampAsync(user)).Succeeded) return Conflict();
                await users.ResetAccessFailedCountAsync(user);
                await transaction.CommitAsync();
                await signIn.RefreshSignInAsync(user);
                return View("RecoveryCodes", codes);
            }
            await users.AccessFailedAsync(user);
            ModelState.AddModelError("Code", result.Error ?? "The request changed. Start SMS setup again.");
        }
        ModelState.SetModelValue("Code", "", "");
        return View("SmsChallenge", SmsSetupScreen(user));
    }

    [HttpPost, EnableRateLimiting("phone-otp-send")]
    public async Task<IActionResult> ResendSmsSetup()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        var pending = phones.PendingNumber(user.Id, user.SecurityStamp ?? "", "sms-setup");
        if (user.TwoFactorEnabled || !user.PhoneNumberConfirmed || pending is null || pending != user.PhoneNumber) return RedirectToAction(nameof(TwoFactor));
        var error = await phones.SendAsync(user.Id, user.SecurityStamp ?? "", pending, "sms-setup");
        if (error is not null) { ModelState.AddModelError("", error); return View("SmsChallenge", SmsSetupScreen(user)); }
        return RedirectToAction(nameof(EnableSmsTwoFactor));
    }

    [HttpPost]
    public async Task<IActionResult> CancelSmsSetup()
    {
        phones.Cancel(users.GetUserId(User)!, "sms-setup");
        return RedirectToAction(nameof(TwoFactor));
    }

    [HttpPost, EnableRateLimiting("security-action")]
    public async Task<IActionResult> SendManageSms()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (!user.SmsTwoFactorEnabled || !user.TwoFactorEnabled || !user.PhoneNumberConfirmed || user.PhoneNumber is null) return Forbid();
        var error = await phones.SendAsync(user.Id, user.SecurityStamp ?? "", user.PhoneNumber, "sms-manage");
        if (error is not null) ModelState.AddModelError("", error);
        else TempData["Success"] = "Verification code requested. Enter it below to confirm your security action.";
        return View("TwoFactor", await TwoFactorData(user));
    }

    private async Task<string?> SmsLoginPurpose()
    {
        var pending = await HttpContext.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        var request = pending.Principal?.FindFirstValue("hackacad:sms-request");
        return request is null ? null : "sms-login:" + request;
    }

    private SmsChallengeModel SmsLoginScreen(ApplicationUser user, string purpose, bool rememberMe, string? returnUrl) =>
        SmsScreen("Enter the six-digit SMS code to finish signing in. You can also use an unused recovery code.", nameof(LoginSms), nameof(ResendLoginSms),
            nameof(Login), phones.ResendWaitSeconds(user.Id, user.SecurityStamp ?? "", purpose), rememberMe: rememberMe, returnUrl: returnUrl);

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> LoginSms(bool rememberMe = false, string? returnUrl = null)
    {
        var user = await PendingTwoFactorUser(); var purpose = await SmsLoginPurpose();
        if (user is null || purpose is null) return RedirectToAction(nameof(Login));
        if (!user.SmsTwoFactorEnabled) return RedirectToAction(nameof(LoginTwoFactor), new { rememberMe, returnUrl });
        return View("SmsChallenge", SmsLoginScreen(user, purpose, rememberMe, returnUrl));
    }

    [AllowAnonymous, HttpPost, EnableRateLimiting("two-factor-login")]
    public async Task<IActionResult> LoginSms(SmsChallengeModel input)
    {
        var user = await PendingTwoFactorUser(); var purpose = await SmsLoginPurpose();
        if (user is null || purpose is null || !user.SmsTwoFactorEnabled || !user.PhoneNumberConfirmed || user.PhoneNumber is null) return RedirectToAction(nameof(Login));
        if (ModelState.IsValid)
        {
            var result = await phones.CheckAsync(user.Id, user.SecurityStamp ?? "", input.Code, purpose);
            if (result.Number == user.PhoneNumber)
            {
                // Recheck the pre-authentication cookie and security stamp after the provider round-trip.
                var verifiedStamp = user.SecurityStamp;
                await db.Entry(user).ReloadAsync();
                user = user.SecurityStamp == verifiedStamp && user.PhoneNumberConfirmed && user.PhoneNumber == result.Number && user.SmsTwoFactorEnabled ? await PendingTwoFactorUser() : null;
                if (user is null) return RedirectToAction(nameof(Login));
                await users.ResetAccessFailedCountAsync(user);
                await HttpContext.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
                await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                await signIn.SignInWithClaimsAsync(user, input.RememberMe, [new Claim(ClaimTypes.AuthenticationMethod, "SMS")]);
                await records.LoginAsync(user.Id, "Signed in with 2FA");
                return Url.IsLocalUrl(input.ReturnUrl) ? LocalRedirect(input.ReturnUrl!) : RedirectToAction(nameof(Dashboard));
            }
            await users.AccessFailedAsync(user);
            await records.LoginAsync(user.Id, "Verification failed");
            ModelState.AddModelError("Code", await users.IsLockedOutAsync(user) ? "Too many failed attempts. Try again in 5 minutes." : result.Error ?? "That code is incorrect or has expired.");
        }
        ModelState.SetModelValue("Code", "", "");
        return View("SmsChallenge", SmsLoginScreen(user, purpose, input.RememberMe, input.ReturnUrl));
    }

    [AllowAnonymous, HttpPost, EnableRateLimiting("phone-otp-send")]
    public async Task<IActionResult> ResendLoginSms(bool rememberMe = false, string? returnUrl = null)
    {
        var user = await PendingTwoFactorUser(); var purpose = await SmsLoginPurpose();
        if (user is null || purpose is null || !user.SmsTwoFactorEnabled || !user.PhoneNumberConfirmed || user.PhoneNumber is null) return RedirectToAction(nameof(Login));
        var error = await phones.SendAsync(user.Id, user.SecurityStamp ?? "", user.PhoneNumber, purpose);
        if (error is not null) { ModelState.AddModelError("", error); return View("SmsChallenge", SmsLoginScreen(user, purpose, rememberMe, returnUrl)); }
        return RedirectToAction(nameof(LoginSms), new { rememberMe, returnUrl });
    }

}
