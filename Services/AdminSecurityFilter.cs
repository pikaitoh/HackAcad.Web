using System.Security.Claims;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Services;

public class AdminSecurityFilter(ApplicationDbContext db, UserManager<ApplicationUser> users, AdminSecurity security) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (!http.User.IsInRole(AccountRoles.Admin)) { await next(); return; }
        var controller = context.RouteData.Values["controller"]?.ToString() ?? "";
        var action = context.RouteData.Values["action"]?.ToString() ?? "";
        var setup = controller == "Account" && action is "TwoFactor" or "BeginAuthenticator" or "EnableAuthenticator" or "BeginSmsTwoFactor" or "ManageTwoFactor" or "SendManageSms"
            or "EnableSmsTwoFactor" or "ResendSmsSetup" or "CancelSmsSetup" or "ChangePassword" or "ChangePhone" or "PhoneNumber" or "CancelPhoneVerification"
            or "VerifyPhone" or "ExternalLogin" or "ExternalCallback" or "GoogleLogin" or "GoogleCallback" or "Logout" or "Login";
        if (setup || controller == "Support") { await next(); return; }
        var user = await users.GetUserAsync(http.User);
        if (user is null || user.IsDisabled || !user.EmailConfirmed || await users.IsLockedOutAsync(user)) { context.Result = new ForbidResult(); return; }
        if (!user.TwoFactorEnabled) {
            context.Result = new RedirectToActionResult("TwoFactor", "Account", null); return;
        }
        var sessionId = http.User.FindFirstValue(AccountSessions.ClaimName) ?? "";
        var session = await db.AccountSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == user.Id && s.RevokedUtc == null);
        var isVerification = controller == "Admin" && action is "Verify" or "SendVerifySms";
        if (isVerification) { await next(); return; }
        var readOnly = await db.Admins.AnyAsync(a => a.UserId == user.Id && a.IsReadOnly);
        if (readOnly && controller == "Admin" && (http.Request.Method != "GET" || action is not ("Security" or "Alert" or "LoginLogs" or "AuditLog"))) {
            context.Result = action == "Index" && http.Request.Method == "GET" ? new RedirectToActionResult("Security", "Admin", null) : new ForbidResult(); return;
        }
        var scope = "session";
        if (controller == "Admin" && http.Request.Method != "GET") {
            var id = context.ActionArguments.TryGetValue("id", out var target) ? target?.ToString() : "";
            scope = action + ":" + id;
        }
        if (session?.MfaVerifiedUtc is null || scope != "session" && !security.HasGrant(user, sessionId, scope)) {
            var back = "/Admin/Security";
            if (http.Request.Method == "GET") back = http.Request.PathBase + http.Request.Path + http.Request.QueryString;
            else if (Uri.TryCreate(http.Request.Headers.Referer.ToString(), UriKind.Absolute, out var referer)
                && referer.Scheme == http.Request.Scheme && referer.Authority == http.Request.Host.Value && referer.AbsolutePath.StartsWith("/Admin/", StringComparison.Ordinal)) back = referer.PathAndQuery;
            var ticket = security.ChallengeTicket(user, sessionId, scope, back);
            context.Result = new RedirectToActionResult("Verify", "Admin", new { ticket }); return;
        }
        await next();
    }
}
