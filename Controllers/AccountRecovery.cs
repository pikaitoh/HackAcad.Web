using System.Text;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

public partial class AccountController
{
    [AllowAnonymous, HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordModel());

    [AllowAnonymous, HttpPost, EnableRateLimiting("verification-email")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordModel input)
    {
        if (!ModelState.IsValid) return View(input);
        var user = await users.FindByEmailAsync(input.Email);
        if (user is not null && !user.IsDisabled && user.EmailConfirmed && await users.HasPasswordAsync(user))
            await verification.SendPasswordResetAsync(user);
        // Never reveal whether the submitted email exists or whether delivery was accepted.
        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [AllowAnonymous, HttpGet]
    public IActionResult ForgotPasswordConfirmation() => View();

    private void ClearPasswordResetGrant()
    {
        foreach (var key in new[] { "PasswordResetUser", "PasswordResetEmail", "PasswordResetToken" }) TempData.Remove(key);
    }

    private async Task<ApplicationUser?> PasswordResetUser(string? id, string? email, string? token)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 255 || string.IsNullOrEmpty(email) || email.Length > 256
            || string.IsNullOrEmpty(token) || token.Length > 4096) return null;
        var user = await users.FindByIdAsync(id);
        return user is not null && !user.IsDisabled && user.EmailConfirmed && string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)
            && await users.HasPasswordAsync(user) && await users.VerifyUserTokenAsync(user, users.Options.Tokens.PasswordResetTokenProvider,
                UserManager<ApplicationUser>.ResetPasswordTokenPurpose, token) ? user : null;
    }

    private Task<ApplicationUser?> PasswordResetUser() => PasswordResetUser(TempData.Peek("PasswordResetUser")?.ToString(),
        TempData.Peek("PasswordResetEmail") as string, TempData.Peek("PasswordResetToken") as string);

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> ResetPassword(string? userId = null, string? email = null, string? code = null)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (userId is not null || email is not null || code is not null)
        {
            ClearPasswordResetGrant();
            string? token = null;
            if (!string.IsNullOrWhiteSpace(code) && code.Length <= 4096)
            {
                try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code)); }
                catch (FormatException) { /* Malformed links use the same invalid-link screen. */ }
            }
            var user = await PasswordResetUser(userId, email, token);
            if (user is null) return View(new ResetPasswordModel { InvalidLink = true });
            // Protected, HttpOnly TempData keeps the token out of form fields and the final page URL.
            TempData["PasswordResetUser"] = user.Id;
            TempData["PasswordResetEmail"] = user.Email;
            TempData["PasswordResetToken"] = token;
            return RedirectToAction(nameof(ResetPassword));
        }
        return await PasswordResetUser() is null ? RedirectToAction(nameof(ForgotPassword)) : View(new ResetPasswordModel());
    }

    [AllowAnonymous, HttpPost, EnableRateLimiting("security-action")]
    public async Task<IActionResult> ResetPassword(ResetPasswordModel input)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var user = await PasswordResetUser();
        if (user is null) { ClearPasswordResetGrant(); return View(new ResetPasswordModel { InvalidLink = true }); }
        if (!ModelState.IsValid) return View(input);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await users.ResetPasswordAsync(user, (string)TempData.Peek("PasswordResetToken")!, input.NewPassword);
        if (!result.Succeeded)
        { ClearPasswordResetGrant(); return View(new ResetPasswordModel { InvalidLink = true }); }
        // Email ownership permits recovery from a password lockout without disabling the second factor.
        if (!(await users.ResetAccessFailedCountAsync(user)).Succeeded || !(await users.SetLockoutEndDateAsync(user, null)).Succeeded) return Conflict();
        user.LastPasswordChangedUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
        await db.AccountSessions.Where(s => s.UserId == user.Id && s.RevokedUtc == null).ExecuteUpdateAsync(set => set.SetProperty(s => s.RevokedUtc, clock.GetUtcNow().UtcDateTime));
        await transaction.CommitAsync();
        ClearPasswordResetGrant();
        await signIn.SignOutAsync();
        TempData["Success"] = "Your password has been reset. Log in with your new password. Your two-factor authentication settings are unchanged.";
        return RedirectToAction(nameof(Login));
    }
}
