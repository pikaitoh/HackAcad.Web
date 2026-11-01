// ADMIN - Check investigation closure through real HTTP handlers in an isolated database.
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
using MySqlConnector;

[assembly: WebApplicationFactoryContentRoot("HackAcad.Web", "../../../../../", "HackAcad.Web.csproj", "0")]
var connection = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ?? "");
if (!connection.Database.StartsWith("hackacad_alert_closed_", StringComparison.Ordinal))
    throw new Exception("Use a generated isolated alert-check database.");
await using var factory = new WebApplicationFactory<AccountController>();
var checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); checks++; Console.WriteLine("PASS " + label); }
var admin = new ApplicationUser { UserName = "closure-admin@alert-check.local", Email = "closure-admin@alert-check.local", EmailConfirmed = true, DisplayName = "Alert Closure Admin" };
var sessionId = Guid.NewGuid().ToString("N");
long id;
using (var scope = factory.Services.CreateScope())
{
    var result = await scope.ServiceProvider.GetRequiredService<UserAccounts>().CreateAsync(admin, "AlertCheck@123!", AccountRoles.Admin);
    if (!result.Succeeded) throw new Exception("Could not create an isolated fixture account.");
    await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().SetTwoFactorEnabledAsync(admin, true);
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var now = DateTime.UtcNow;
    db.AccountSessions.Add(new AccountSession { Id = sessionId, UserId = admin.Id, SecurityStamp = admin.SecurityStamp!, CreatedUtc = now, LastSeenUtc = now, ExpiresUtc = now.AddHours(1), MfaVerifiedUtc = now });
    var alert = new SecurityAlert { EventKey = "closure-fixture", Title = "Controlled repeated sign-ins", Explanation = "An isolated investigation fixture.", UserId = admin.Id, IpAddress = "192.0.2.10", CreatedUtc = now, UpdatedUtc = now };
    db.SecurityAlerts.Add(alert); await db.SaveChangesAsync(); id = alert.Id;
    scope.ServiceProvider.GetRequiredService<AdminSecurity>().GrantAccess(admin, sessionId, "Alert:" + id);
}
// This cookie represents an already authenticated fixture session; login/MFA have separate checks.
using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
var scheme = IdentityConstants.ApplicationScheme;
var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, admin.Id), new Claim(ClaimTypes.Name, admin.UserName!), new Claim("AspNet.Identity.SecurityStamp", admin.SecurityStamp!), new Claim(AccountSessions.ClaimName, sessionId) }, scheme));
var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
var cookie = new AuthenticationTicket(principal, new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(25) }, scheme);
client.DefaultRequestHeaders.Add("Cookie", options.Cookie.Name + "=" + options.TicketDataFormat.Protect(cookie));
var page = "/Admin/Alert/" + id;
async Task<string> Html(string path)
{
    var response = await client.GetAsync(path);
    if (!response.IsSuccessStatusCode) throw new Exception($"GET {path}: {response.StatusCode}");
    return await response.Content.ReadAsStringAsync();
}
async Task<SecurityAlert> Alert()
{
    using var scope = factory.Services.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SecurityAlerts.AsNoTracking().SingleAsync(a => a.Id == id);
}
var initialHtml = await Html(page);
var token = WebUtility.HtmlDecode(Regex.Match(initialHtml, "name=\"__RequestVerificationToken\"[^>]+value=\"([^\"]+)\"").Groups[1].Value);
if (token.Length == 0) throw new Exception("Missing fixture CSRF token.");
Task<HttpResponseMessage> Post(string status, string notes, string version) => client.PostAsync(page, new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["Status"] = status, ["Notes"] = notes, ["Version"] = version, ["ReturnUrl"] = "/Admin/Security?status=open" }));
Check(initialHtml.Contains("Save investigation"), "a new alert retains its investigation form");
var state = await Alert();
var invalid = await Post("Investigating", "", state.Version);
Check(invalid.StatusCode == HttpStatusCode.OK && (await invalid.Content.ReadAsStringAsync()).Contains("Please enter investigation notes."), "open alerts retain their inline validation messages");
Check((await Alert()).Version == state.Version, "invalid notes leave the investigation unchanged");
Check((await Post("Investigating", "Review the controlled sign-in records with the account owner.", state.Version)).StatusCode == HttpStatusCode.Redirect, "a new alert can enter investigation");
state = await Alert();
var staleVersion = state.Version;
var finalNotes = "False positive confirmed with the account owner. The investigation is complete.";
var resolved = await Post("Resolved", finalNotes, state.Version);
Check(resolved.StatusCode == HttpStatusCode.Redirect && resolved.Headers.Location?.ToString() == "/Admin/Security?status=open", "resolving an open investigation retains its safe list return link");
state = await Alert();
Check(state.Status == "Resolved" && state.Notes == finalNotes && state.Version != staleVersion, "resolution saves final notes and advances the record version");
var html = await Html(page);
Check(html.Contains("The investigation is read-only.") && html.Contains(finalNotes), "resolved details show the closure message and final investigation notes");
Check(!html.Contains("Save investigation") && !html.Contains("name=\"Notes\"") && !html.Contains("name=\"Status\""), "resolved investigations omit status, notes editing and save controls");
Check(html.Contains("Review account") && html.Contains("Back to security"), "read-only investigation details retain normal navigation");
async Task<string> Snapshot()
{
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var alert = await Alert();
    return string.Join('|', alert.Status, alert.Notes, alert.Version, alert.UpdatedUtc.ToString("O"), await db.AdminActivities.CountAsync(), await db.SecurityAlerts.CountAsync());
}
var before = await Snapshot();
foreach (var version in new[] { state.Version, staleVersion })
    foreach (var status in new[] { "New", "Investigating", "Resolved" })
    {
        var response = await Post(status, "A forged attempt to replace the final investigation notes.", version);
        Check(response.StatusCode == HttpStatusCode.Conflict && (await response.Content.ReadAsStringAsync()).Contains("has been resolved"), "direct submissions cannot edit or reopen a resolved alert (" + status + ")");
    }
Check(before == await Snapshot(), "rejected submissions preserve final status, notes, timestamps, version and audit records");
using (var scope = factory.Services.CreateScope())
{
    var monitor = scope.ServiceProvider.GetRequiredService<SecurityMonitor>();
    await monitor.RaiseAsync("closure-fixture", "Duplicate signal", "The existing signal was detected again.", admin.Id, "192.0.2.10");
    Check(before == await Snapshot(), "repeat detection of the same event leaves the resolved investigation unchanged");
    await monitor.RaiseAsync("closure-fixture-new-event", "A later sign-in alert", "A separate later event needs its own investigation.", admin.Id, "192.0.2.10");
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    Check(await db.SecurityAlerts.CountAsync(a => a.Status != "Resolved") == 1 && (await Alert()).Status == "Resolved", "a separate event creates a new open alert without reopening the closed record");
}
var openHtml = await Html("/Admin/Security?status=open");
Check(openHtml.Contains("A later sign-in alert") && !openHtml.Contains("Controlled repeated sign-ins"), "the open-alert filter excludes resolved investigations");
Console.WriteLine($"All {checks} resolved-alert HTTP checks passed. Isolated database only.");
