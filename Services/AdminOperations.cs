using System.Security.Claims;
using System.Text.Json;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HackAcad.Web.Services;

// ADMIN - Preserve the exact submitted operation across verification and safely handle retries on this server instance.

// A pending form belongs to one administrator/session, action, target and exact payload.
public class AdminOperations(ApplicationDbContext db, IMemoryCache cache, AdminSecurity security, TimeProvider clock)
{
    private sealed class Operation
    {
        public required string Stamp { get; init; }
        public required string Hash { get; init; }
        public required string Snapshot { get; init; }
        public required AdminResumeModel Form { get; init; }
        public required DateTimeOffset Expires { get; init; }
        public SemaphoreSlim Gate { get; } = new(1);
        public IActionResult? Completed { get; set; }
    }
    private static readonly object CreationLock = new();
    // Identify the shared fetch flow without changing the normal HTML form fallback.
    public static bool IsAsync(HttpRequest request) => request.Headers["X-Admin-Request"] == "fetch";
    private static string Key(string user, string session, string operation) => $"admin-operation:{user}:{session}:{operation}";
    // Return field-validation messages in the JSON shape expected by the verification dialog.
    public static JsonResult Errors(ActionContext context, string fallback = "Check the form and try again.") =>
        new(new
        {
            errors = context.ModelState.Values.SelectMany(v => v.Errors).Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? fallback : e.ErrorMessage).DefaultIfEmpty(fallback).ToArray()
        })
        {
            StatusCode = 422
        };

    // Return a destination URL so the browser performs the GET and receives TempData once.
    public static IActionResult Navigation(ActionExecutingContext context, IActionResult result)
    {
        if (!IsAsync(context.HttpContext.Request))
            return result;
        var url = ((Controller)context.Controller).Url;
        var destination = result switch
        {
            RedirectToActionResult redirect => url.Action(redirect.ActionName, redirect.ControllerName, redirect.RouteValues),
            RedirectToRouteResult redirect => url.RouteUrl(redirect.RouteName, redirect.RouteValues),
            LocalRedirectResult redirect => redirect.Url,
            RedirectResult redirect => redirect.Url,
            _ => null
        };
        // The browser performs the only GET, so TempData success/error messages survive navigation.
        return destination is null ? result : new JsonResult(new
        {
            redirectUrl = destination
        });
    }

    // Avoid a fresh challenge for unchanged or invalid forms that will not write data.
    public async Task<bool> IsNoChangeAsync(string action, string id, ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid)
            return true; // Existing actions return validation errors before writing.
        context.ActionArguments.TryGetValue("input", out var posted);
        static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (action == "EditCourse" && int.TryParse(id, out var courseId) && posted is EditCourseModel input)
        {
            var course = await db.Courses.AsNoTracking().SingleOrDefaultAsync(c => c.CourseId == courseId);
            return course is not null && course.Title.Trim() == input.Title.Trim() && Optional(course.Category) == Optional(input.Category)
                && Optional(course.Description) == Optional(input.Description) && course.IsPublished == input.IsPublished;
        }
        if (action == "Alert" && long.TryParse(id, out var alertId) && posted is AlertUpdateInput alertInput)
            return await db.SecurityAlerts.AnyAsync(a => a.Id == alertId && a.Version == alertInput.Version && a.Status == alertInput.Status && a.Notes == alertInput.Notes);
        if (action == "AccountStatus" && posted is AccountStatusInput status)
            return await db.Users.AnyAsync(u => u.Id == id && u.IsDisabled == status.Disable);
        if (action == "AdminAccess" && posted is AdminAccessInput access)
            return await db.Admins.AnyAsync(a => a.UserId == id && a.IsReadOnly == access.ReadOnly);
        return false;
    }

    // Hash the current target state to reject records changed during identity verification.
    private async Task<string> Snapshot(string action, string id, IFormCollection form)
    {
        object? record = null;
        if (action is "EditCourse" or "DeleteCourse" or "ReviewCourse" && int.TryParse(id, out var course))
            record = await db.Courses.AsNoTracking().Where(c => c.CourseId == course).Select(c => new { c.Title, c.Description, c.Category, c.IsPublished, c.ReviewStatus, c.ReviewVersion }).SingleOrDefaultAsync();
        else if (action == "AccountStatus")
            record = await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => new { u.IsDisabled, u.SecurityStamp }).SingleOrDefaultAsync();
        else if (action == "AdminAccess")
            record = await db.Admins.AsNoTracking().Where(a => a.UserId == id).Select(a => new { a.IsReadOnly, a.User!.SecurityStamp }).SingleOrDefaultAsync();
        else if (action == "Alert" && long.TryParse(id, out var alert))
            record = await db.SecurityAlerts.AsNoTracking().Where(a => a.Id == alert).Select(a => new { a.Status, a.Notes, a.Version }).SingleOrDefaultAsync();
        else if (action == "ReviewApplication" && long.TryParse(id, out var application))
            record = await db.InstructorApplications.AsNoTracking().Where(a => a.Id == application).Select(a => new { a.Status, a.Revision }).SingleOrDefaultAsync();
        else if (action == "SupportReply" && long.TryParse(id, out var ticket))
            record = await db.SupportTickets.AsNoTracking().Where(t => t.Id == ticket).Select(t => new { t.Status, t.Version }).SingleOrDefaultAsync();
        else if (action == "RevokeSessions")
        {
            var sessionId = form["sessionId"].ToString();
            var now = clock.GetUtcNow().UtcDateTime;
            record = await db.AccountSessions.AsNoTracking().Where(s => s.UserId == id && s.RevokedUtc == null && s.ExpiresUtc > now
                && db.Users.Any(u => u.Id == id && u.SecurityStamp == s.SecurityStamp) && (sessionId == "" || s.Id == sessionId)).OrderBy(s => s.Id).Select(s => new { s.Id, s.SecurityStamp }).ToListAsync();
        }
        return SecurityMonitor.Hash(JsonSerializer.Serialize(record));
    }

    // Recover the original fields only for the same verified user and browser session.
    public AdminResumeModel? Resume(string operationId, ApplicationUser user, string session)
    {
        if (!cache.TryGetValue<Operation>(Key(user.Id, session, operationId), out var operation)
            || operation!.Stamp != user.SecurityStamp || operation.Expires <= clock.GetUtcNow())
            return null;
        return operation.Form;
    }

    // Bind, verify and execute one exact operation; repeated requests reuse its completed result.
    public async Task RunAsync(ActionExecutingContext context, ActionExecutionDelegate next, ApplicationUser user, string session, string action, string id)
    {
        var http = context.HttpContext;
        var scope = action + ":" + id;
        if (!http.Request.HasFormContentType)
        {
            context.Result = new BadRequestObjectResult("Submit a form to perform this operation.");
            return;
        }
        var form = await http.Request.ReadFormAsync();
        // ADMIN - Capture the business fields; CSRF tokens are validated separately and are not the operation identity.
        var fields = form.Where(p => p.Key is not ("__RequestVerificationToken" or "AdminOperationId"))
            .OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value.Select(v => v ?? "").ToArray(), StringComparer.Ordinal);
        var payload = JsonSerializer.Serialize(fields);
        if (payload.Length > 32768 || form.Files.Count != 0)
        {
            context.Result = new BadRequestResult();
            return;
        }
        var hash = SecurityMonitor.Hash(scope + http.Request.PathBase + http.Request.Path + http.Request.QueryString + payload);
        var operationId = form["AdminOperationId"].ToString();
        if (operationId.Length == 0)
            operationId = Guid.NewGuid().ToString("N");
        if (!Guid.TryParseExact(operationId, "N", out _))
        {
            context.Result = new BadRequestResult();
            return;
        }
        var key = Key(user.Id, session, operationId);
        var snapshot = await Snapshot(action, id, form);
        Operation operation;
        lock (CreationLock)
        {
            if (!cache.TryGetValue(key, out operation!))
            {
                fields["AdminOperationId"] = [operationId];
                operation = new Operation
                {
                    Stamp = user.SecurityStamp ?? "",
                    Hash = hash,
                    Snapshot = snapshot,
                    Expires = clock.GetUtcNow().AddMinutes(10),
                    Form = new AdminResumeModel { Path = http.Request.PathBase + http.Request.Path + http.Request.QueryString, Fields = fields }
                };
                // ponytail: pending forms/idempotency live on this single host; use shared durable storage before scaling out.
                cache.Set(key, operation, TimeSpan.FromMinutes(10));
            }
        }
        // ADMIN - Serialize retries for the same operation so the underlying mutation is applied at most once here.
        await operation.Gate.WaitAsync(http.RequestAborted);
        try
        {
            if (operation.Stamp != user.SecurityStamp || operation.Hash != hash || operation.Expires <= clock.GetUtcNow())
            {
                context.Result = new ConflictObjectResult("This operation changed or expired. Review the form and submit again.");
                return;
            }
            if (operation.Completed is not null)
            {
                context.Result = Navigation(context, operation.Completed);
                return;
            }
            // ADMIN - Reject content changed by another tab/user while the Admin was completing verification.
            if (operation.Snapshot != snapshot)
            {
                context.Result = new ConflictObjectResult("This record changed while you were verifying. Reload it and review the latest information before submitting.");
                return;
            }
            // ADMIN - HTTP 428 asks the browser to verify; no business action has run at this point.
            if (!security.HasGrant(user, session, scope))
            {
                var ticket = security.ChallengeTicket(user, session, scope, "/Admin/Security", operationId);
                if (IsAsync(http.Request))
                {
                    context.Result = new JsonResult(new { verificationRequired = true, ticket, hasPassword = !string.IsNullOrEmpty(user.PasswordHash), smsEnabled = user.SmsTwoFactorEnabled }) { StatusCode = 428 };
                }
                else
                    context.Result = new RedirectToActionResult("Verify", "Admin", new { ticket });
                return;
            }
            // ADMIN - Only a matching, verified operation reaches the feature controller.
            var executed = await next();
            if (executed.Exception is null || executed.ExceptionHandled)
            {
                if (executed.Result is RedirectToActionResult or LocalRedirectResult or RedirectResult or RedirectToRouteResult)
                {
                    operation.Completed = executed.Result;
                    executed.Result = Navigation(context, executed.Result);
                }
                if (IsAsync(http.Request) && executed.Result is ViewResult view)
                {
                    executed.Result = context.ModelState.IsValid && view.ViewData["NoChanges"] is string notice
                        ? new JsonResult(new { notice }) : Errors(context);
                }
            }
        }
        finally { operation.Gate.Release(); }
    }
}
