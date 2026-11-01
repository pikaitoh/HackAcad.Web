// ADMIN - Verify resolved conversations through real HTTP handlers using an isolated database and local email capture.
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using HackAcad.Web.Controllers;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MimeKit;
using MySqlConnector;

[assembly: WebApplicationFactoryContentRoot("HackAcad.Web", "../../../../../", "HackAcad.Web.csproj", "0")]
var connection = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ?? "");
var pickup = Environment.GetEnvironmentVariable("Email__DevelopmentPickupDirectory");
if (!connection.Database.StartsWith("hackacad_support_closed_", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(pickup))
    throw new Exception("Use a generated isolated support-check database and local email capture.");
await using var factory = new WebApplicationFactory<AccountController>();
var checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); checks++; Console.WriteLine("PASS " + label); }

async Task<ApplicationUser> User(string name, string role)
{
    using var scope = factory.Services.CreateScope();
    var address = name.Replace(' ', '-').ToLowerInvariant() + "@support-check.local";
    var user = new ApplicationUser { UserName = address, Email = address, EmailConfirmed = true, DisplayName = name };
    var result = await scope.ServiceProvider.GetRequiredService<UserAccounts>().CreateAsync(user, "SupportCheck@123!", role);
    if (!result.Succeeded) throw new Exception("Could not create an isolated fixture account.");
    if (role == AccountRoles.Admin) await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().SetTwoFactorEnabledAsync(user, true);
    return user;
}
var admin = await User("Closure Admin", AccountRoles.Admin);
var owner = await User("Closure Owner", AccountRoles.Student);
var outsider = await User("Closure Other", AccountRoles.Student);
var sessionId = Guid.NewGuid().ToString("N");
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var now = DateTime.UtcNow;
    db.AccountSessions.Add(new AccountSession { Id = sessionId, UserId = admin.Id, SecurityStamp = admin.SecurityStamp!, CreatedUtc = now, LastSeenUtc = now, ExpiresUtc = now.AddHours(1), MfaVerifiedUtc = now });
    await db.SaveChangesAsync();
}
// Test cookies represent already verified fixture sessions; login/MFA behavior is covered separately.
HttpClient Client(ApplicationUser user, bool administrator = false)
{
    var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
    var scheme = administrator ? IdentityConstants.ApplicationScheme : SupportAccessService.Scheme;
    var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.Id), new(ClaimTypes.Name, user.UserName!), new(ClaimTypes.Email, user.Email!) };
    if (administrator) { claims.Add(new("AspNet.Identity.SecurityStamp", user.SecurityStamp!)); claims.Add(new(AccountSessions.ClaimName, sessionId)); }
    else claims.Add(new("support:stamp", user.SecurityStamp!));
    var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
    var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
    var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(25) }, scheme);
    client.DefaultRequestHeaders.Add("Cookie", options.Cookie.Name + "=" + options.TicketDataFormat.Protect(ticket));
    return client;
}
using var manager = Client(admin, true);
using var userClient = Client(owner);
using var foreignClient = Client(outsider);
async Task<string> Html(HttpClient client, string page)
{
    var response = await client.GetAsync(page);
    if (!response.IsSuccessStatusCode) throw new Exception($"GET {page}: {response.StatusCode}");
    return await response.Content.ReadAsStringAsync();
}
async Task<HttpResponseMessage> Post(HttpClient client, string action, Dictionary<string, string> fields, string page)
{
    var html = await Html(client, page);
    fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]+value=\"([^\"]+)\"").Groups[1].Value);
    if (fields["__RequestVerificationToken"].Length == 0) throw new Exception("Missing fixture CSRF token.");
    return await client.PostAsync(action, new FormUrlEncodedContent(fields));
}
async Task<SupportTicket> Ticket(long id)
{
    using var scope = factory.Services.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SupportTickets.AsNoTracking().SingleAsync(t => t.Id == id);
}
long id;
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var ticket = new SupportTicket { UserId = owner.Id, Subject = "Resolve a controlled account question", Category = SupportCategory.General, CreatedUtc = DateTime.UtcNow.AddDays(-1), UpdatedUtc = DateTime.UtcNow.AddDays(-1) };
    db.SupportTickets.Add(ticket); await db.SaveChangesAsync(); id = ticket.Id;
    db.SupportMessages.Add(new SupportMessage { TicketId = id, AuthorId = owner.Id, AuthorName = owner.DisplayName, Body = "Please help me review this controlled account question.", CreatedUtc = DateTime.UtcNow.AddDays(-1) });
    await db.SaveChangesAsync();
    scope.ServiceProvider.GetRequiredService<AdminSecurity>().GrantAccess(admin, sessionId, "SupportReply:" + id);
}
var adminPage = "/Admin/SupportThread/" + id;
var userPage = "/Support/Thread/" + id;
var adminAction = "/Admin/SupportReply/" + id;
var userAction = "/Support/Reply/" + id;
Dictionary<string, string> Reply(SupportTicket ticket, string body, string status, bool internalOnly = false) => new() { ["Version"] = ticket.Version, ["Body"] = body, ["Status"] = status, ["Internal"] = internalOnly.ToString().ToLowerInvariant() };

Check(new SupportTicket { Status = "Awaiting user" }.CanUserReply(true), "an Admin public reply still enables the user's next turn");
Check(!new SupportTicket { Status = "Resolved" }.CanUserReply(true) && !new SupportTicket { Status = "Resolved" }.CanUserReply(false), "resolved tickets deny replies regardless of the last message author");
var ticketState = await Ticket(id);
Check((await Post(manager, adminAction, Reply(ticketState, "PRIVATE review note retained for administrators only.", "Awaiting user", true), adminPage)).StatusCode == HttpStatusCode.Redirect, "an open ticket still accepts an internal note");
Check((await Ticket(id)).Status == "Awaiting admin" && !(await Html(userClient, userPage)).Contains("PRIVATE review note"), "internal notes keep the open status and remain private");
ticketState = await Ticket(id);
Check((await Post(manager, adminAction, Reply(ticketState, "Please provide the details needed to investigate this request.", "Awaiting user"), adminPage)).StatusCode == HttpStatusCode.Redirect, "an open ticket still accepts a public reply");
Check((await Html(userClient, userPage)).Contains("Send reply"), "the owner can reply to an awaiting-user ticket");
ticketState = await Ticket(id);
Check((await Post(userClient, userAction, Reply(ticketState, "Here are the requested details for the administrator.", "Resolved"), "/Support/Create")).StatusCode == HttpStatusCode.Redirect, "the owner can complete one normal reply turn");
ticketState = await Ticket(id);
Check(ticketState.Status == "Awaiting admin", "a user cannot forge the resolved status");
var staleVersion = ticketState.Version;
Check((await Post(manager, adminAction, Reply(ticketState, "The account question has been answered. This request is now resolved.", "Resolved"), adminPage)).StatusCode == HttpStatusCode.Redirect, "an Admin final public reply resolves the ticket normally");
var adminHtml = await Html(manager, adminPage);
var userHtml = await Html(userClient, userPage);
Check(adminHtml.Contains("The conversation is read-only.") && !adminHtml.Contains("action=\"" + adminAction + "\""), "resolved Admin pages retain history and omit both reply and internal-note controls");
Check(userHtml.Contains("Please create a new request") && userHtml.Contains("Create a new request") && !userHtml.Contains("Send reply") && !userHtml.Contains("Sending another message reopens") && !userHtml.Contains("Please wait for an administrator"), "resolved user pages show closure, retain history and omit the composer");
Check(adminHtml.Contains("PRIVATE review note") && !userHtml.Contains("PRIVATE review note") && userHtml.Contains("The account question has been answered"), "closing the thread preserves public history and internal-note privacy");
var closingMail = Directory.GetFiles(pickup!, "*.eml").Select(file => MimeMessage.Load(file)).Single(message => message.Subject.Contains("Reply to support request") && (message.TextBody ?? "").Contains("Request status: Resolved"));
Check((closingMail.TextBody ?? "").Contains("read the final reply") && !(closingMail.TextBody ?? "").Contains("read and reply"), "the closing email no longer invites a reply to the resolved ticket");

async Task<string> Snapshot()
{
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var ticket = await Ticket(id);
    return string.Join('|', ticket.Status, ticket.Version, ticket.UpdatedUtc.ToString("O"), await db.SupportMessages.CountAsync(m => m.TicketId == id), await db.UserNotifications.CountAsync(), await db.AdminActivities.CountAsync(), Directory.GetFiles(pickup!, "*.eml").Length);
}
var before = await Snapshot();
ticketState = await Ticket(id);
foreach (var internalOnly in new[] { false, true })
{
    var response = await Post(manager, adminAction, Reply(ticketState, "Forged attempt to change a resolved conversation.", "Awaiting user", internalOnly), adminPage);
    Check(response.StatusCode == HttpStatusCode.Conflict && (await response.Content.ReadAsStringAsync()).Contains("has been resolved"), internalOnly ? "the backend rejects internal notes after resolution" : "the backend rejects public Admin replies and reopening after resolution");
}
foreach (var version in new[] { ticketState.Version, staleVersion })
{
    var fields = Reply(ticketState, "Forged attempt to reopen this closed request.", "Awaiting user", true); fields["Version"] = version;
    var response = await Post(userClient, userAction, fields, "/Support/Create");
    Check(response.StatusCode == HttpStatusCode.Conflict && (await response.Content.ReadAsStringAsync()).Contains("has been resolved"), "direct user posts and stale browser forms cannot reopen a resolved ticket");
}
Check((await Post(foreignClient, userAction, Reply(ticketState, "An unrelated account attempts to write to this ticket.", "Awaiting admin"), "/Support/Create")).StatusCode == HttpStatusCode.NotFound, "another user still cannot write to the owner's ticket");
Check(before == await Snapshot(), "rejected posts do not alter status, version, messages, audits, notifications or email delivery");
var newRequest = await Post(userClient, "/Support/Create", new() { ["Category"] = SupportCategory.General, ["Subject"] = "A separate support question after resolution", ["Body"] = "Please help with a different question in a new conversation." }, "/Support/Create");
Check(newRequest.StatusCode == HttpStatusCode.Redirect && newRequest.Headers.Location?.ToString() != userPage, "the owner can create a separate request after resolution");
Check((await Ticket(id)).Status == "Resolved" && (await Ticket(id)).Version == ticketState.Version, "creating a new request leaves the resolved history unchanged");
Console.WriteLine($"All {checks} resolved-support HTTP checks passed. Isolated database; local email capture only.");
