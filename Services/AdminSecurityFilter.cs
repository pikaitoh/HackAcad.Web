using System.Security.Claims;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Services;

// ADMIN - Request gate shared by Admin actions. Role authorization, MFA, read-only policy and step-up checks are distinct layers.

public class AdminSecurityFilter(ApplicationDbContext db, UserManager<ApplicationUser> users, AdminSecurity security, AdminOperations operations) : IAsyncActionFilter
{
    // Apply shared Admin access policy before any feature controller action is executed.
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (!http.User.IsInRole(AccountRoles.Admin))
        {
            await next();
            return;
        }
        var controller = context.RouteData.Values["controller"]?.ToString() ?? "";
        var action = context.RouteData.Values["action"]?.ToString() ?? "";
        // ADMIN - 1. Allow account recovery/MFA setup routes without creating a verification redirect loop.
        var setup = controller == "Account" && action is "TwoFactor" or "BeginAuthenticator" or "EnableAuthenticator" or "BeginSmsTwoFactor" or "ManageTwoFactor" or "SendManageSms"
            or "EnableSmsTwoFactor" or "ResendSmsSetup" or "CancelSmsSetup" or "ChangePassword" or "ChangePhone" or "PhoneNumber" or "CancelPhoneVerification"
            or "VerifyPhone" or "ExternalLogin" or "ExternalCallback" or "GoogleLogin" or "GoogleCallback" or "Logout" or "Login";
        if (setup || controller == "Support")
        {
            await next();
            return;
        }
        // ADMIN - 2. Require an active, verified account with two-factor authentication enabled.
        var user = await users.GetUserAsync(http.User);
        if (user is null || user.IsDisabled || !user.EmailConfirmed || await users.IsLockedOutAsync(user))
        {
            context.Result = new ForbidResult();
            return;
        }
        if (!user.TwoFactorEnabled)
        {
            context.Result = new RedirectToActionResult("TwoFactor", "Account", null);
            return;
        }
        var sessionId = http.User.FindFirstValue(AccountSessions.ClaimName) ?? "";
        var session = await db.AccountSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == user.Id && s.RevokedUtc == null);
        var isVerification = controller == "Admin" && action is "Verify" or "SendVerifySms";
        if (isVerification)
        {
            await next();
            return;
        }
        // ADMIN - 3. Auditors can inspect selected security pages but cannot execute management actions.
        var readOnly = await db.Admins.AnyAsync(a => a.UserId == user.Id && a.IsReadOnly);
        if (readOnly && controller == "Admin" && (http.Request.Method != "GET" || action is not ("Security" or "Alert" or "LoginLogs" or "AuditLog")))
        {
            context.Result = action == "Index" && http.Request.Method == "GET" ? new RedirectToActionResult("Security", "Admin", null) : new ForbidResult();
            return;
        }
        // ADMIN - 4. Sensitive writes go through preserved-operation verification, not just a hidden UI button.
        if (controller == "Admin" && http.Request.Method != "GET")
        {
            var id = context.ActionArguments.TryGetValue("id", out var target) ? target?.ToString() : "";
            if (session?.MfaVerifiedUtc is not null && await operations.IsNoChangeAsync(action, id ?? "", context))
            {
                var executed = await next();
                if (executed.Result is not null)
                    executed.Result = AdminOperations.Navigation(context, executed.Result);
                if (AdminOperations.IsAsync(http.Request) && executed.Result is ViewResult view)
                    executed.Result = context.ModelState.IsValid && view.ViewData["NoChanges"] is string notice ? new JsonResult(new { notice }) : AdminOperations.Errors(context);
                return;
            }
            await operations.RunAsync(context, next, user, sessionId, action, id ?? "");
            return;
        }
        // ADMIN - 5. Ordinary Admin navigation also requires MFA on the current browser session.
        if (session?.MfaVerifiedUtc is null)
        {
            var back = "/Admin/Security";
            if (http.Request.Method == "GET")
                back = http.Request.PathBase + http.Request.Path + http.Request.QueryString;
            else if (Uri.TryCreate(http.Request.Headers.Referer.ToString(), UriKind.Absolute, out var referer)
                && referer.Scheme == http.Request.Scheme && referer.Authority == http.Request.Host.Value && referer.AbsolutePath.StartsWith("/Admin/", StringComparison.Ordinal))
                back = referer.PathAndQuery;
            var ticket = security.ChallengeTicket(user, sessionId, "session", back);
            context.Result = new RedirectToActionResult("Verify", "Admin", new { ticket });
            return;
        }
        await next();
    }
}
