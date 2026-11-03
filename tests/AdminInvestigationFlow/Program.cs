// ADMIN - Exercise support and alert evidence through HTTP, using an isolated database and local delivery captures.
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
if (!connection.Database.StartsWith("hackacad_investigation_", StringComparison.Ordinal)
    || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Email__DevelopmentPickupDirectory"))
    || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Sms__DevelopmentPickupDirectory")))
    throw new Exception("Use a generated investigation-check database and local email/SMS capture only.");
await using var factory = new WebApplicationFactory<AccountController>();
var checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL " + label); checks++; Console.WriteLine("PASS " + label); }
async Task<ApplicationUser> Create(string label, string role)
{
    using var scope = factory.Services.CreateScope();
    var address = label.Replace(' ', '-').ToLowerInvariant() + "@investigation-check.local";
    var user = new ApplicationUser { UserName = address, Email = address,
        DisplayName = label, EmailConfirmed = true, TimeZoneId = "Asia/Kuala_Lumpur" };
    var result = await scope.ServiceProvider.GetRequiredService<UserAccounts>().CreateAsync(user, "ContextCheck@123!", role);
    if (!result.Succeeded) throw new Exception("Could not create isolated fixture.");
    if (role == AccountRoles.Admin) await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().SetTwoFactorEnabledAsync(user, true);
    return user;
}
var manager = await Create("Context manager", AccountRoles.Admin);
var auditor = await Create("Context auditor", AccountRoles.Admin);
var owner = await Create("Account owner", AccountRoles.Student);
var other = await Create("Unrelated account", AccountRoles.Student);
var empty = await Create("No activity account", AccountRoles.Student);
var sessionIds = new Dictionary<string, string>();
var alertTime = DateTime.UtcNow.AddHours(-1);
long ticketId, emptyTicketId, accountAlert, ipAlert, unknownAlert, deviceAlert;
const string ip = "192.0.2.35", otherIp = "192.0.2.99";
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    foreach (var user in new[] { manager, auditor })
    {
        var session = new AccountSession { UserId = user.Id, SecurityStamp = user.SecurityStamp!, ExpiresUtc = DateTime.UtcNow.AddHours(1), MfaVerifiedUtc = DateTime.UtcNow };
        db.AccountSessions.Add(session);
        sessionIds[user.Id] = session.Id;
    }
    (await db.Admins.SingleAsync(a => a.UserId == auditor.Id)).IsReadOnly = true;
    var ticket = new SupportTicket { UserId = owner.Id, Category = SupportCategory.SignIn, Subject = "Please investigate my sign-in", CreatedUtc = alertTime };
    var blank = new SupportTicket { UserId = empty.Id, Category = SupportCategory.SignIn, Subject = "No sign-in history yet", CreatedUtc = alertTime };
    db.SupportTickets.AddRange(ticket, blank);
    for (var i = 0; i < 12; i++) db.SecurityEvents.Add(new SecurityEvent { UserId = owner.Id, SubjectHash = SecurityMonitor.Hash(owner.Id),
        IpAddress = ip, Device = "Owner device " + i, Outcome = i == 11 ? "Signed in with 2FA" : "Failed", Success = i == 11,
        OccurredUtc = alertTime.AddMinutes(-9).AddSeconds(i * 30) });
    db.SecurityEvents.AddRange(
        new SecurityEvent { UserId = owner.Id, SubjectHash = SecurityMonitor.Hash(owner.Id), IpAddress = ip, Device = "OUTSIDE_TIME_WINDOW", Outcome = "Failed", OccurredUtc = alertTime.AddMinutes(-20) },
        new SecurityEvent { UserId = other.Id, SubjectHash = SecurityMonitor.Hash(other.Id), IpAddress = ip, Device = "OTHER_ACCOUNT_SAME_IP", Outcome = "Failed", OccurredUtc = alertTime.AddMinutes(-2) },
        new SecurityEvent { SubjectHash = "unknown-subject-one", IpAddress = ip, Device = "UNKNOWN_MATCHING_IDENTIFIER", Outcome = "Failed", OccurredUtc = alertTime.AddMinutes(-1) },
        new SecurityEvent { SubjectHash = "unknown-subject-two", IpAddress = ip, Device = "UNKNOWN_DIFFERENT_IDENTIFIER", Outcome = "Failed", OccurredUtc = alertTime.AddMinutes(-1) },
        new SecurityEvent { UserId = other.Id, SubjectHash = SecurityMonitor.Hash(other.Id), IpAddress = otherIp, Device = "OTHER_IP", Outcome = "Failed", OccurredUtc = alertTime.AddMinutes(-1) },
        new SecurityEvent { UserId = owner.Id, SubjectHash = SecurityMonitor.Hash(owner.Id), IpAddress = ip, Device = "DEVICE_LOGIN_AFTER_ALERT", Outcome = "Signed in", Success = true, OccurredUtc = alertTime.AddSeconds(10) });
    db.LoginRecords.AddRange(new LoginRecord { UserId = owner.Id, Outcome = "Failed", IpAddress = ip, Device = "Owner login log" },
        new LoginRecord { UserId = other.Id, Outcome = "Failed", IpAddress = ip, Device = "UNRELATED_LOGIN_LOG" });
    var a = new SecurityAlert { UserId = owner.Id, EventKey = "account:" + SecurityMonitor.Hash(owner.Id) + ":1", Title = "Repeated failed sign-ins", IpAddress = ip, CreatedUtc = alertTime };
    var b = new SecurityAlert { EventKey = "spray:fixture:1", Title = "One IP tried multiple accounts", IpAddress = ip, CreatedUtc = alertTime };
    var c = new SecurityAlert { EventKey = "account:unknown-subject-one:1", Title = "Repeated failed sign-ins", IpAddress = ip, CreatedUtc = alertTime };
    var d = new SecurityAlert { UserId = owner.Id, EventKey = "device:fixture", Title = "New device", IpAddress = ip, CreatedUtc = alertTime };
    db.SecurityAlerts.AddRange(a, b, c, d);
    await db.SaveChangesAsync();
    ticketId = ticket.Id; emptyTicketId = blank.Id; accountAlert = a.Id; ipAlert = b.Id; unknownAlert = c.Id; deviceAlert = d.Id;
}
HttpClient Client(ApplicationUser user, bool support = false)
{
    var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
    var scheme = support ? SupportAccessService.Scheme : IdentityConstants.ApplicationScheme;
    var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.Id), new(ClaimTypes.Name, user.UserName!), new(ClaimTypes.Email, user.Email!) };
    if (sessionIds.TryGetValue(user.Id, out var session)) { claims.Add(new("AspNet.Identity.SecurityStamp", user.SecurityStamp!)); claims.Add(new(AccountSessions.ClaimName, session)); }
    if (support) claims.Add(new("support:stamp", user.SecurityStamp!));
    var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
    var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
    var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(20) }, scheme);
    client.DefaultRequestHeaders.Add("Cookie", options.Cookie.Name + "=" + options.TicketDataFormat.Protect(ticket));
    return client;
}
using var admin = Client(manager);
async Task<string> Html(HttpClient client, string path)
{
    var response = await client.GetAsync(path);
    Check(response.StatusCode == HttpStatusCode.OK, path + " loads successfully");
    return await response.Content.ReadAsStringAsync();
}
var supportHtml = await Html(admin, "/Admin/SupportThread/" + ticketId);
Check(supportHtml.Contains("Sign-in checks") && supportHtml.Contains("No current account restriction recorded"), "support displays actual current account checks");
Check(supportHtml.Contains("Recent sign-in attempts") && supportHtml.Contains("Showing 8 of 14"), "support bounds recent activity to eight rows");
Check(!supportHtml.Contains("OTHER_ACCOUNT_SAME_IP") && !supportHtml.Contains("UNKNOWN_MATCHING_IDENTIFIER"), "support excludes activity from other or unknown accounts");
Check(supportHtml.Contains("DEVICE_LOGIN_AFTER_ALERT") && supportHtml.Contains("Signed in with 2FA"), "support shows successful outcomes as well as failures");
Check(supportHtml.Contains("userId=" + owner.Id), "full-history link identifies the exact account");
Check(!supportHtml.Contains(owner.PasswordHash!) && !supportHtml.Contains(owner.SecurityStamp!), "account context never renders password hashes or security stamps");
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var user = await db.Users.SingleAsync(u => u.Id == owner.Id);
    user.IsDisabled = true; user.EmailConfirmed = false; user.LockoutEnabled = true; user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(5); user.AccessFailedCount = 3;
    db.AdminActivities.Add(new AdminActivity { Action = "Disabled account", TargetType = "User", TargetId = owner.Id, Details = "Investigating <script>unsafe</script> account activity." });
    await db.SaveChangesAsync();
}
var restricted = await Html(admin, "/Admin/SupportThread/" + ticketId);
Check(restricted.Contains("Sign-in is currently restricted") && restricted.Contains("The account is disabled"), "disabled state is explained before the administrator replies");
Check(restricted.Contains("confirm their email address") && restricted.Contains("temporarily locked until"), "unconfirmed email and active lock are both explained");
Check(restricted.Contains("Recorded reason for disabling") && !restricted.Contains("<script>unsafe</script>"), "recorded disable reason is visible and HTML-encoded");
var blankHtml = await Html(admin, "/Admin/SupportThread/" + emptyTicketId);
Check(blankHtml.Contains("No sign-in attempts have been recorded"), "accounts without history have an explicit empty state");
var accountHtml = await Html(admin, "/Admin/Alert/" + accountAlert);
Check(accountHtml.Contains("Related sign-in activity") && accountHtml.Contains("Showing 10 of 12"), "account alerts show bounded evidence from the detector time window");
Check(!accountHtml.Contains("OTHER_ACCOUNT_SAME_IP") && !accountHtml.Contains("OUTSIDE_TIME_WINDOW") && !accountHtml.Contains("DEVICE_LOGIN_AFTER_ALERT"), "account evidence excludes other users and events outside the alert window");
var networkHtml = await Html(admin, "/Admin/Alert/" + ipAlert);
Check(networkHtml.Contains("OTHER_ACCOUNT_SAME_IP") && networkHtml.Contains("Unrecognised account"), "IP alerts include relevant registered and unrecognised accounts");
Check(!networkHtml.Contains("OTHER_IP") && !networkHtml.Contains("OUTSIDE_TIME_WINDOW"), "IP evidence excludes a different IP and stale events");
var unknownHtml = await Html(admin, "/Admin/Alert/" + unknownAlert);
Check(unknownHtml.Contains("UNKNOWN_MATCHING_IDENTIFIER") && !unknownHtml.Contains("UNKNOWN_DIFFERENT_IDENTIFIER"), "unregistered-account alerts use the original subject hash rather than unrelated attempts from the IP");
var deviceHtml = await Html(admin, "/Admin/Alert/" + deviceAlert);
Check(deviceHtml.Contains("DEVICE_LOGIN_AFTER_ALERT"), "new-device evidence includes the successful login recorded just after session creation");
var csrf = WebUtility.HtmlDecode(Regex.Match(accountHtml, "name=\"__RequestVerificationToken\"[^>]+value=\"([^\"]+)\"").Groups[1].Value);
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var version = await db.SecurityAlerts.Where(a => a.Id == accountAlert).Select(a => a.Version).SingleAsync();
    var invalid = await admin.PostAsync("/Admin/Alert/" + accountAlert, new FormUrlEncodedContent(new Dictionary<string,string> {
        ["__RequestVerificationToken"] = csrf, ["Version"] = version, ["Status"] = "Investigating", ["Notes"] = "" }));
    var invalidHtml = await invalid.Content.ReadAsStringAsync();
    Check(invalid.StatusCode == HttpStatusCode.OK && invalidHtml.Contains("Please enter investigation notes.") && invalidHtml.Contains("Related sign-in activity"), "validation errors preserve investigation evidence");
}
var history = await Html(admin, "/Admin/LoginLogs?userId=" + owner.Id);
Check(history.Contains("Owner login log") && !history.Contains("UNRELATED_LOGIN_LOG"), "full login history is scoped by account ID rather than a partial name match");
Check(history.Contains("name=\"UserId\""), "applying additional history filters retains the selected account");
using var audit = Client(auditor);
var auditHtml = await Html(audit, "/Admin/Alert/" + accountAlert);
Check(auditHtml.Contains("Related sign-in activity") && !auditHtml.Contains("Save investigation") && !auditHtml.Contains("Review sessions"), "read-only auditors see evidence without management controls");
using var student = Client(other);
Check((await student.GetAsync("/Admin/Alert/" + accountAlert)).StatusCode != HttpStatusCode.OK, "students cannot access another account's investigation context");
using var supportClient = Client(owner, support: true);
var userThread = await Html(supportClient, "/Support/Thread/" + ticketId);
Check(!userThread.Contains("Sign-in checks") && !userThread.Contains("Related sign-in activity"), "admin investigation context is not exposed in the user's support conversation");
// Keep rendered fixture pages beside the isolated test log for desktop/mobile visual inspection.
var rendered = Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("Sms__DevelopmentPickupDirectory"))!, "rendered");
Directory.CreateDirectory(rendered);
await File.WriteAllTextAsync(Path.Combine(rendered, "support.html"), supportHtml);
await File.WriteAllTextAsync(Path.Combine(rendered, "restricted.html"), restricted);
await File.WriteAllTextAsync(Path.Combine(rendered, "alert.html"), accountHtml);
await File.WriteAllTextAsync(Path.Combine(rendered, "network.html"), networkHtml);
Console.WriteLine($"All {checks} investigation-context HTTP checks passed. Isolated database only.");
