using System.Security.Claims;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HackAcad.Web.Controllers;

public partial class AccountController
{
    // ponytail: grants use process memory on this single host; use a distributed cache for multiple instances.
    private sealed record DisconnectProof(string Stamp, string TargetKey, string Method, string MethodKey,
        DateTimeOffset Expires, bool TwoFactorVerified = false);

    private string DisconnectKey(string userId, string provider) => $"disconnect:{userId}:{User.FindFirstValue(AccountSessions.ClaimName)}:{provider}";
    private string DisconnectSmsPurpose(string provider) => "disconnect:" + provider + ":" + User.FindFirstValue(AccountSessions.ClaimName);
    private async Task<HashSet<string>> AvailableProviders() => (await signIn.GetExternalAuthenticationSchemesAsync()).Select(s => s.Name).ToHashSet();

    private DisconnectProof? DisconnectGrant(ApplicationUser user, string provider)
    {
        if (securityCache.TryGetValue<DisconnectProof>(DisconnectKey(user.Id, provider), out var proof)
            && proof!.Stamp == user.SecurityStamp && proof.Expires > clock.GetUtcNow()) return proof;
        securityCache.Remove(DisconnectKey(user.Id, provider));
        return null;
    }

    private async Task<DisconnectModel?> DisconnectData(ApplicationUser user, string provider)
    {
        if (!ExternalAuthentication.Providers.Contains(provider) || user.IsDisabled || !user.EmailConfirmed
            || await users.IsLockedOutAsync(user) || string.IsNullOrEmpty(User.FindFirstValue(AccountSessions.ClaimName))) return null;
        var logins = await users.GetLoginsAsync(user);
        var target = logins.FirstOrDefault(l => l.LoginProvider == provider);
        if (target is null) return null;
        var available = await AvailableProviders();
        var details = (await connections.ListAsync(user, available)).Single(l => l.Provider == provider);
        var proof = DisconnectGrant(user, provider);
        if (proof is not null && proof.TargetKey != target.ProviderKey) { securityCache.Remove(DisconnectKey(user.Id, provider)); proof = null; }
        return new DisconnectModel { Provider = provider, Account = details.Email ?? details.Name,
            CanDisconnect = details.CanDisconnect, HasPassword = await users.HasPasswordAsync(user),
            RemainingProviders = logins.Where(l => l.LoginProvider != provider && available.Contains(l.LoginProvider)).Select(l => l.LoginProvider).ToList(),
            IdentityVerified = proof is not null, Ready = proof is not null && (!user.TwoFactorEnabled || proof.TwoFactorVerified),
            VerifiedMethod = proof?.Method ?? "", VerificationExpiresUtc = proof?.Expires.UtcDateTime, SmsEnabled = user.SmsTwoFactorEnabled };
    }

    [HttpGet]
    public async Task<IActionResult> Disconnect(string provider)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        var model = await DisconnectData(user, provider);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost, EnableRateLimiting("security-action")]
    public async Task<IActionResult> VerifyDisconnect(string provider, DisconnectModel input, bool sendSms = false)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        var model = await DisconnectData(user, provider);
        if (model is null) return NotFound();
        if (!model.CanDisconnect) return View("Disconnect", model);
        if (!ModelState.IsValid) return View("Disconnect", model);
        var proof = DisconnectGrant(user, provider);
        if (proof is null)
        {
            if (string.IsNullOrEmpty(input.CurrentPassword)) ModelState.AddModelError("CurrentPassword", "Enter your current password, or verify another connected account.");
            else if (await ConfirmCurrentPassword(user, input.CurrentPassword))
            {
                var target = (await users.GetLoginsAsync(user)).Single(l => l.LoginProvider == provider);
                proof = new(user.SecurityStamp ?? "", target.ProviderKey, "Password", "", clock.GetUtcNow().AddMinutes(5));
                securityCache.Set(DisconnectKey(user.Id, provider), proof, proof.Expires);
            }
            ModelState.SetModelValue(nameof(input.CurrentPassword), "", "");
            if (proof is null) return View("Disconnect", model);
        }
        if (user.TwoFactorEnabled && !proof.TwoFactorVerified)
        {
            if (user.SmsTwoFactorEnabled && (sendSms || string.IsNullOrWhiteSpace(input.Code)))
            {
                var error = user.PhoneNumberConfirmed && user.PhoneNumber is not null
                    ? await phones.SendAsync(user.Id, user.SecurityStamp ?? "", user.PhoneNumber, DisconnectSmsPurpose(provider))
                    : "Your SMS verification method is unavailable. Use a recovery code or restore access first.";
                if (error is not null) ModelState.AddModelError("Code", error);
                return View("Disconnect", (await DisconnectData(user, provider))!);
            }
            var code = (input.Code ?? "").Replace(" ", "").Replace("-", "");
            var valid = code.Length == 6 && (user.SmsTwoFactorEnabled
                ? (await phones.CheckAsync(user.Id, user.SecurityStamp ?? "", code, DisconnectSmsPurpose(provider))).Number == user.PhoneNumber && user.PhoneNumber is not null
                : await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code));
            if (!valid && !string.IsNullOrWhiteSpace(input.Code)) valid = (await users.RedeemTwoFactorRecoveryCodeAsync(user, input.Code.Replace(" ", ""))).Succeeded;
            if (!valid)
            {
                if (!string.IsNullOrWhiteSpace(input.Code)) await users.AccessFailedAsync(user);
                ModelState.AddModelError("Code", "Enter a current verification code or an unused recovery code.");
                return View("Disconnect", (await DisconnectData(user, provider)) ?? model);
            }
            proof = proof with { TwoFactorVerified = true };
            securityCache.Set(DisconnectKey(user.Id, provider), proof, proof.Expires);
        }
        await users.ResetAccessFailedCountAsync(user);
        return RedirectToAction(nameof(Disconnect), new { provider });
    }

    [HttpPost, EnableRateLimiting("security-action")]
    public async Task<IActionResult> ConfirmDisconnectWithProvider(string provider, string retainedProvider)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        var model = await DisconnectData(user, provider);
        if (model is null) return NotFound();
        if (!model.CanDisconnect || !model.RemainingProviders.Contains(retainedProvider)) return BadRequest();
        var logins = await users.GetLoginsAsync(user);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        var properties = signIn.ConfigureExternalAuthenticationProperties(retainedProvider,
            Url.Action(nameof(ExternalCallback), new { provider = retainedProvider })!, user.Id);
        properties.Items["hackacad:disconnect"] = provider;
        properties.Items["hackacad:disconnect-target"] = logins.Single(l => l.LoginProvider == provider).ProviderKey;
        properties.Items["hackacad:disconnect-retained"] = logins.Single(l => l.LoginProvider == retainedProvider).ProviderKey;
        properties.Items["hackacad:disconnect-stamp"] = user.SecurityStamp;
        properties.Items["hackacad:disconnect-session"] = User.FindFirstValue(AccountSessions.ClaimName);
        properties.Items["prompt"] = "select_account";
        return Challenge(properties, retainedProvider);
    }

    private async Task<IActionResult> CompleteDisconnectVerification(ExternalLoginInfo info, string provider)
    {
        var user = await users.GetUserAsync(User);
        var properties = info.AuthenticationProperties!;
        if (user is null || properties.GetString("hackacad:disconnect-stamp") != user.SecurityStamp
            || properties.GetString("hackacad:disconnect-session") != User.FindFirstValue(AccountSessions.ClaimName)
            || info.ProviderKey != properties.GetString("hackacad:disconnect-retained")
            || (await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey))?.Id != user.Id) return await ExternalFailure(info.LoginProvider);
        var model = await DisconnectData(user, provider);
        var target = (await users.GetLoginsAsync(user)).FirstOrDefault(l => l.LoginProvider == provider);
        if (model is null || !model.CanDisconnect || !model.RemainingProviders.Contains(info.LoginProvider)
            || target?.ProviderKey != properties.GetString("hackacad:disconnect-target")) return await ExternalFailure(info.LoginProvider);
        await connections.RememberAsync(user, info);
        var proof = new DisconnectProof(user.SecurityStamp ?? "", target!.ProviderKey, info.LoginProvider, info.ProviderKey, clock.GetUtcNow().AddMinutes(5));
        securityCache.Set(DisconnectKey(user.Id, provider), proof, proof.Expires);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        if (user.TwoFactorEnabled && user.SmsTwoFactorEnabled && user.PhoneNumberConfirmed && user.PhoneNumber is not null)
        {
            var error = await phones.SendAsync(user.Id, user.SecurityStamp ?? "", user.PhoneNumber, DisconnectSmsPurpose(provider));
            if (error is not null) TempData["Error"] = error;
        }
        return RedirectToAction(nameof(Disconnect), new { provider });
    }

    [HttpPost, ActionName("Disconnect"), EnableRateLimiting("security-action")]
    public async Task<IActionResult> DisconnectConfirmed(string provider, DisconnectModel input)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        var model = await DisconnectData(user, provider);
        if (model is null) return NotFound();
        var proof = DisconnectGrant(user, provider);
        if (!input.Confirm) ModelState.AddModelError("Confirm", "Confirm that you understand this account can no longer be used to sign in.");
        if (proof is null || !model.Ready) ModelState.AddModelError("", "Verify a remaining sign-in method again. Confirmation expires after five minutes.");
        if (!ModelState.IsValid || !model.CanDisconnect) return View("Disconnect", model);
        securityCache.Remove(DisconnectKey(user.Id, provider));
        var error = await connections.DisconnectAsync(user, provider, proof!.TargetKey, proof.Method, proof.MethodKey, proof.Stamp, await AvailableProviders());
        if (error is not null) { ModelState.AddModelError("", error); return View("Disconnect", (await DisconnectData(user, provider)) ?? model); }
        phones.Cancel(user.Id, DisconnectSmsPurpose(provider));
        var emailed = await verification.SendSecurityNoticeAsync(user.Email!, $"{provider} disconnected", $"{provider} can no longer sign in to your HackAcad account. Your profile, role and learning records are unchanged.");
        await signIn.SignOutAsync();
        TempData["Success"] = $"{provider} disconnected. Sign in again using a remaining method." + (emailed ? "" : " The email notice could not be delivered; the change is saved in security activity.");
        return RedirectToAction(nameof(Login));
    }

    [HttpPost]
    public IActionResult CancelDisconnect(string provider)
    {
        var id = users.GetUserId(User)!;
        securityCache.Remove(DisconnectKey(id, provider));
        phones.Cancel(id, DisconnectSmsPurpose(provider));
        return RedirectToAction(nameof(Settings));
    }

    [HttpGet]
    public async Task<IActionResult> SecurityHistory(int page = 1)
    {
        var id = users.GetUserId(User)!;
        var query = db.AdminActivities.AsNoTracking().Where(r => r.TargetType == "Account security" && r.TargetId == id);
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 20d)));
        return View(new AdminActivityModel { Page = page, Total = total,
            Records = await query.OrderByDescending(r => r.OccurredUtc).ThenByDescending(r => r.Id).Skip((page - 1) * 20).Take(20).ToListAsync() });
    }
}
