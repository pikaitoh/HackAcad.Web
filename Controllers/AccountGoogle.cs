using System.Security.Claims;
using System.Text.RegularExpressions;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Services;
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
    private bool HasRecentGoogleLogin() => long.TryParse(User.FindFirstValue(AccountSessions.GoogleTimeClaim), out var value)
        && clock.GetUtcNow().ToUnixTimeSeconds() - value is >= 0 and <= 300;

    [AllowAnonymous, HttpPost, EnableRateLimiting("two-factor-login")]
    public async Task<IActionResult> GoogleLogin(string? returnUrl = null, bool connect = false, bool reauthenticate = false)
    {
        if (!(await signIn.GetExternalAuthenticationSchemesAsync()).Any(s => s.Name == "Google"))
            return RedirectToAction(nameof(Login), new { googleError = "unavailable" });
        if ((connect || reauthenticate) && User.Identity?.IsAuthenticated != true) return Challenge();
        if (!connect && !reauthenticate && User.Identity?.IsAuthenticated == true) return RedirectToAction(nameof(Dashboard));
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        var properties = signIn.ConfigureExternalAuthenticationProperties("Google",
            Url.Action(nameof(GoogleCallback), new { returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null, connect, reauthenticate })!,
            (connect || reauthenticate) ? users.GetUserId(User) : null);
        properties.Items["hackacad:connect"] = connect ? users.GetUserId(User) : "";
        properties.Items["hackacad:reauthenticate"] = reauthenticate ? users.GetUserId(User) : "";
        properties.Items["prompt"] = "select_account";
        return Challenge(properties, "Google");
    }

    private static bool ValidGoogleIdentity(ExternalLoginInfo? info) => info?.LoginProvider == "Google"
        && !string.IsNullOrWhiteSpace(info.ProviderKey)
        && info.Principal.FindFirstValue("google:email_verified")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true
        && info.Principal.FindFirstValue(ClaimTypes.Email) is { Length: <= 256 } email
        && Regex.IsMatch(email, AccountValidation.EmailPattern);

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> GoogleCallback(string? returnUrl = null, bool connect = false, bool reauthenticate = false)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var id = (connect || reauthenticate) ? users.GetUserId(User) : null;
        var info = await signIn.GetExternalLoginInfoAsync(id);
        if (!ValidGoogleIdentity(info) || (connect && (id is null || info!.AuthenticationProperties?.Items["hackacad:connect"] != id)))
            return await GoogleFailure();
        if (reauthenticate)
        {
            var linked = await users.FindByLoginAsync("Google", info!.ProviderKey);
            if (id is null || linked?.Id != id || info.AuthenticationProperties?.Items["hackacad:reauthenticate"] != id)
                return await GoogleFailure();
            return await CompleteGoogleLogin(linked, returnUrl);
        }
        if (connect)
        {
            var user = await users.GetUserAsync(User);
            if (user is null || user.IsDisabled || !user.EmailConfirmed || await users.IsLockedOutAsync(user)
                || !string.Equals(user.Email, info!.Principal.FindFirstValue(ClaimTypes.Email), StringComparison.OrdinalIgnoreCase))
                return await GoogleFailure();
            var result = await users.AddLoginAsync(user, info!);
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
                ? "Google is connected. You can now use it to log in." : "Google could not be connected. It may already be linked to an account.";
            return RedirectToAction(nameof(Settings));
        }
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction(nameof(Dashboard));
        // A provider's immutable ID is the binding. Never auto-link by matching email.
        var existing = await users.FindByLoginAsync("Google", info!.ProviderKey);
        if (existing is not null) return await CompleteGoogleLogin(existing, returnUrl);
        if (await users.FindByEmailAsync(info.Principal.FindFirstValue(ClaimTypes.Email)!) is not null)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            TempData["GoogleNotice"] = "This email already has a HackAcad account. Log in with your email and password, then connect Google in Manage account.";
            return RedirectToAction(nameof(Login));
        }
        var name = info.Principal.FindFirstValue(ClaimTypes.Name) ?? "";
        return View("GoogleRegister", new GoogleRegisterModel { Email = info.Principal.FindFirstValue(ClaimTypes.Email)!,
            DisplayName = name.Length is >= 2 and <= 50 && Regex.IsMatch(name, AccountValidation.NamePattern) ? name : "",
            ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });
    }

    [AllowAnonymous, HttpPost, EnableRateLimiting("verification-email")]
    public async Task<IActionResult> GoogleRegister(GoogleRegisterModel input)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction(nameof(Dashboard));
        var info = await signIn.GetExternalLoginInfoAsync();
        if (!ValidGoogleIdentity(info) || !string.IsNullOrEmpty(info!.AuthenticationProperties?.Items["hackacad:connect"]))
            return await GoogleFailure();
        input.Email = info!.Principal.FindFirstValue(ClaimTypes.Email)!;
        if (!ModelState.IsValid) return View(input);
        if (await users.FindByEmailAsync(input.Email) is not null || await users.FindByLoginAsync("Google", info.ProviderKey) is not null)
            return await GoogleFailure();
        // Google is authoritative for Gmail/Workspace; third-party mailboxes need our own verification.
        var trustedEmail = input.Email.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(info.Principal.FindFirstValue("google:hosted_domain"));
        var user = new ApplicationUser { UserName = input.Email, Email = input.Email,
            DisplayName = input.DisplayName, EmailConfirmed = trustedEmail };
        try
        {
            var result = await accounts.CreateGoogleAsync(user, info);
            if (!result.Succeeded) { AccountValidation.AddIdentityErrors(ModelState, result); return View(input); }
        }
        catch (DbUpdateException) { return await GoogleFailure(); }
        if (!trustedEmail)
        {
            TempData["VerificationEmail"] = user.Email;
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            TempData["VerificationDeliveryFailed"] = !await verification.SendAsync(user);
            return RedirectToAction(nameof(VerificationPending));
        }
        return await CompleteGoogleLogin(user, input.ReturnUrl);
    }

    private async Task<IActionResult> CompleteGoogleLogin(ApplicationUser user, string? returnUrl)
    {
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        if (user.IsDisabled || !await signIn.CanSignInAsync(user) || await users.IsLockedOutAsync(user))
        {
            await records.LoginAsync(user.Id, "Failed");
            return RedirectToAction(nameof(Login), new { googleError = "blocked" });
        }
        if (await users.GetTwoFactorEnabledAsync(user))
        {
            await BeginTwoFactorLogin(user);
            return RedirectToAction(user.SmsTwoFactorEnabled ? nameof(LoginSms) : nameof(LoginTwoFactor), new { returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });
        }
        await users.ResetAccessFailedCountAsync(user);
        await signIn.SignInWithClaimsAsync(user, false, [new Claim(ClaimTypes.AuthenticationMethod, "Google"),
            new Claim(AccountSessions.GoogleTimeClaim, clock.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture))]);
        await records.LoginAsync(user.Id, "Signed in with Google");
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction(nameof(Dashboard));
    }

    private async Task<IActionResult> GoogleFailure()
    {
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        if (User.Identity?.IsAuthenticated == true)
        {
            TempData["Error"] = "Google could not be confirmed for this account. Try again with the Google account that belongs to you.";
            return RedirectToAction(nameof(Settings));
        }
        return RedirectToAction(nameof(Login), new { googleError = "failed" });
    }
}
