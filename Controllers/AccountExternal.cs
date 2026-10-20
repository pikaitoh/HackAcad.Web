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
    private bool HasRecentExternalLogin()
    {
        var proof = User.FindFirstValue(AccountSessions.ExternalTimeClaim) ?? User.FindFirstValue(AccountSessions.GoogleTimeClaim);
        return long.TryParse(proof, out var value) && clock.GetUtcNow().ToUnixTimeSeconds() - value is >= 0 and <= 300;
    }

    private async Task<List<string>> ReauthenticationProviders(ApplicationUser user)
    {
        var configured = (await signIn.GetExternalAuthenticationSchemesAsync()).Select(s => s.Name).ToHashSet();
        return (await users.GetLoginsAsync(user)).Select(l => l.LoginProvider)
            .Where(p => ExternalAuthentication.Providers.Contains(p) && configured.Contains(p)).Distinct().ToList();
    }

    [AllowAnonymous, HttpPost, EnableRateLimiting("two-factor-login")]
    public Task<IActionResult> GoogleLogin(string? returnUrl = null, bool connect = false, bool reauthenticate = false)
        => ExternalLogin("Google", returnUrl, connect, reauthenticate);
    [AllowAnonymous, HttpGet]
    public Task<IActionResult> GoogleCallback(string? returnUrl = null, bool connect = false, bool reauthenticate = false)
        => ExternalCallback("Google", returnUrl, connect, reauthenticate);
    [AllowAnonymous, HttpPost, EnableRateLimiting("verification-email")]
    public Task<IActionResult> GoogleRegister(ExternalRegisterModel input) => ExternalRegister(input, "Google");

    [AllowAnonymous, HttpPost, EnableRateLimiting("two-factor-login")]
    public async Task<IActionResult> ExternalLogin(string provider, string? returnUrl = null, bool connect = false, bool reauthenticate = false)
    {
        if (!ExternalAuthentication.Providers.Contains(provider) || !(await signIn.GetExternalAuthenticationSchemesAsync()).Any(s => s.Name == provider))
            return RedirectToAction(nameof(Login), new { externalError = provider });
        if ((connect || reauthenticate) && User.Identity?.IsAuthenticated != true) return Challenge();
        if (!connect && !reauthenticate && User.Identity?.IsAuthenticated == true) return RedirectToAction(nameof(Dashboard));
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        var properties = signIn.ConfigureExternalAuthenticationProperties(provider,
            Url.Action(nameof(ExternalCallback), new { provider, returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null, connect, reauthenticate })!,
            (connect || reauthenticate) ? users.GetUserId(User) : null);
        properties.Items["hackacad:return"] = Url.IsLocalUrl(returnUrl) ? returnUrl : "";
        properties.Items["hackacad:connect"] = connect ? users.GetUserId(User) : "";
        properties.Items["hackacad:reauthenticate"] = reauthenticate ? users.GetUserId(User) : "";
        properties.Items["prompt"] = "select_account";
        return Challenge(properties, provider);
    }

    private static bool ValidExternalIdentity(ExternalLoginInfo? info, string provider) => ExternalAuthentication.Providers.Contains(provider)
        && info?.LoginProvider == provider && !string.IsNullOrWhiteSpace(info.ProviderKey)
        && (provider != "Google" || info.Principal.FindFirstValue("google:email_verified")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
        && (provider != "GitHub" || info.Principal.FindFirstValue("github:email_verified") == "true")
        && info.Principal.FindFirstValue(ClaimTypes.Email) is { Length: <= 256 } email
        && Regex.IsMatch(email, AccountValidation.EmailPattern);

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> ExternalCallback(string provider, string? returnUrl = null, bool connect = false, bool reauthenticate = false)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var currentId = users.GetUserId(User);
        var info = await signIn.GetExternalLoginInfoAsync(currentId);
        if (!ValidExternalIdentity(info, provider)) return await ExternalFailure(provider);
        if (info!.AuthenticationProperties?.GetString("hackacad:disconnect") is { Length: > 0 } targetProvider)
            return await CompleteDisconnectVerification(info, targetProvider);
        connect = !string.IsNullOrEmpty(info.AuthenticationProperties?.GetString("hackacad:connect"));
        reauthenticate = !string.IsNullOrEmpty(info.AuthenticationProperties?.GetString("hackacad:reauthenticate"));
        returnUrl = info.AuthenticationProperties?.GetString("hackacad:return");
        var id = (connect || reauthenticate) ? currentId : null;
        if (connect && (id is null || info.AuthenticationProperties?.GetString("hackacad:connect") != id))
            return await ExternalFailure(provider);
        if (reauthenticate)
        {
            var linked = await users.FindByLoginAsync(provider, info!.ProviderKey);
            if (id is null || linked?.Id != id || info.AuthenticationProperties?.GetString("hackacad:reauthenticate") != id)
                return await ExternalFailure(provider);
            await connections.RememberAsync(linked, info!);
            return await CompleteExternalLogin(linked, provider, returnUrl);
        }
        if (connect)
        {
            var user = await users.GetUserAsync(User);
            if (user is null || user.IsDisabled || !user.EmailConfirmed || await users.IsLockedOutAsync(user))
                return await ExternalFailure(provider);
            string? error;
            try { error = await connections.ConnectAsync(user, info!); }
            catch (DbUpdateException) { error = "Your connections changed. Reload and try again."; }
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            if (error is not null) TempData["Error"] = error;
            else
            {
                var delivered = await verification.SendSecurityNoticeAsync(user.Email!, $"{provider} connected", $"You can now use {provider} to sign in to your HackAcad account.");
                TempData["Success"] = $"{provider} is connected. You can now use it to log in.";
                if (!delivered) TempData["Error"] = "The account is connected, but the security email could not be delivered. The change is saved in security activity.";
            }
            return RedirectToAction(nameof(Settings));
        }
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction(nameof(Dashboard));
        // A provider's immutable ID is the binding. Never auto-link by matching email.
        var existing = await users.FindByLoginAsync(provider, info!.ProviderKey);
        if (existing is not null)
        {
            if (!existing.IsDisabled && !await users.IsLockedOutAsync(existing)) await connections.RememberAsync(existing, info!);
            return await CompleteExternalLogin(existing, provider, returnUrl);
        }
        if (await users.FindByEmailAsync(info.Principal.FindFirstValue(ClaimTypes.Email)!) is not null)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            TempData["ExternalNotice"] = $"This email already has a HackAcad account. Log in using an existing sign-in method, then connect {provider} in Manage account.";
            return RedirectToAction(nameof(Login));
        }
        var name = info.Principal.FindFirstValue(ClaimTypes.Name) ?? "";
        return View("ExternalRegister", new ExternalRegisterModel { Provider = provider, Email = info.Principal.FindFirstValue(ClaimTypes.Email)!,
            DisplayName = name.Length is >= 2 and <= 50 && Regex.IsMatch(name, AccountValidation.NamePattern) ? name : "",
            ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });
    }

    [AllowAnonymous, HttpPost, EnableRateLimiting("verification-email")]
    public async Task<IActionResult> ExternalRegister(ExternalRegisterModel input, string provider)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction(nameof(Dashboard));
        var info = await signIn.GetExternalLoginInfoAsync();
        if (!ValidExternalIdentity(info, provider) || !string.IsNullOrEmpty(info!.AuthenticationProperties?.GetString("hackacad:connect"))
            || !string.IsNullOrEmpty(info!.AuthenticationProperties?.GetString("hackacad:reauthenticate"))
            || !string.IsNullOrEmpty(info.AuthenticationProperties?.GetString("hackacad:disconnect")))
            return await ExternalFailure(provider);
        input.Provider = provider;
        input.ReturnUrl = info!.AuthenticationProperties?.GetString("hackacad:return");
        input.Email = info!.Principal.FindFirstValue(ClaimTypes.Email)!;
        if (!ModelState.IsValid) return View("ExternalRegister", input);
        if (await users.FindByEmailAsync(input.Email) is not null || await users.FindByLoginAsync(provider, info.ProviderKey) is not null)
            return await ExternalFailure(provider);
        // Google is authoritative for Gmail/Workspace; third-party mailboxes need our own verification.
        var trustedEmail = provider == "Google" && (input.Email.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(info.Principal.FindFirstValue("google:hosted_domain")));
        var user = new ApplicationUser { UserName = input.Email, Email = input.Email,
            DisplayName = input.DisplayName, EmailConfirmed = trustedEmail };
        try
        {
            var result = await accounts.CreateExternalAsync(user, info);
            if (!result.Succeeded) { AccountValidation.AddIdentityErrors(ModelState, result); return View("ExternalRegister", input); }
        }
        catch (DbUpdateException) { return await ExternalFailure(provider); }
        await connections.RememberAsync(user, info);
        if (!trustedEmail)
        {
            TempData["VerificationEmail"] = user.Email;
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            TempData["VerificationDeliveryFailed"] = !await verification.SendAsync(user);
            return RedirectToAction(nameof(VerificationPending));
        }
        return await CompleteExternalLogin(user, provider, input.ReturnUrl);
    }

    private async Task<IActionResult> CompleteExternalLogin(ApplicationUser user, string provider, string? returnUrl)
    {
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        if (user.IsDisabled || await users.IsLockedOutAsync(user))
        {
            await records.LoginAsync(user.Id, "Failed");
            return RedirectToAction(nameof(Login), new { externalError = provider });
        }
        if (!user.EmailConfirmed)
        {
            TempData["VerificationEmail"] = user.Email;
            return RedirectToAction(nameof(VerificationPending));
        }
        if (!await signIn.CanSignInAsync(user)) return RedirectToAction(nameof(Login), new { externalError = provider });
        if (await users.GetTwoFactorEnabledAsync(user))
        {
            await BeginTwoFactorLogin(user);
            return RedirectToAction(user.SmsTwoFactorEnabled ? nameof(LoginSms) : nameof(LoginTwoFactor), new { returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });
        }
        await users.ResetAccessFailedCountAsync(user);
        await signIn.SignInWithClaimsAsync(user, false, [new Claim(ClaimTypes.AuthenticationMethod, provider),
            new Claim(AccountSessions.ExternalProviderClaim, provider),
            new Claim(AccountSessions.ExternalTimeClaim, clock.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture))]);
        await records.LoginAsync(user.Id, $"Signed in with {provider}");
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction(nameof(Dashboard));
    }

    private async Task<IActionResult> ExternalFailure(string provider)
    {
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        if (User.Identity?.IsAuthenticated == true)
        {
            TempData["Error"] = $"{provider} could not be confirmed for this account. Try again with the linked account that belongs to you.";
            return RedirectToAction(nameof(Settings));
        }
        return RedirectToAction(nameof(Login), new { externalError = provider });
    }
}
