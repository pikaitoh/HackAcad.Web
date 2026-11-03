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
    for (var i = 0; i < 12; i++)
    {
        var occurred = alertTime.AddMinutes(-9).AddSeconds(i * 30);
        var outcome = i == 11 ? "Signed in with 2FA" : "Failed";
        db.SecurityEvents.Add(new SecurityEvent { UserId = owner.Id, SubjectHash = SecurityMonitor.Hash(owner.Id),
            IpAddress = ip, Device = "Owner device " + i, Outcome = outcome, Success = i == 11, OccurredUtc = occurred });
        db.LoginRecords.Add(new LoginRecord { UserId = owner.Id, IpAddress = ip, Device = "Owner device " + i,
            Outcome = outcome, OccurredUtc = occurred });
    }
    db.SecurityEvents.AddRange(
        new SecurityEvent { UserId = owner.Id, SubjectHash = SecurityMonitor.Hash(owner.Id), IpAddress = ip, Device = "OUTSIDE_TIME_WINDOW", Outcome = "Failed", OccurredUtc = alertTime.AddMinutes(-20) },
        new SecurityEvent { UserId = other.Id, SubjectHash = SecurityMonitor.Hash(other.Id), IpAddress = ip, Device = "OTHER_ACCOUNT_SAME_IP", Outcome = "Failed", OccurredUtc = alertTime.AddMinutes(-2) },
        new SecurityEvent { SubjectHash = "unknown-subject-one", IpAddress = ip, Device = "UNKNOWN_MATCHING_IDENTIFIER", Outcome = "Failed", OccurredUtc = alertTime.AddMinutes(-1) },
        new SecurityEvent { SubjectHash = "unknown-subject-two", IpAddress = ip, Device = "UNKNOWN_DIFFERENT_IDENTIFIER", Outcome = "Failed", OccurredUtc = alertTime.AddMinutes(-1) },
        new SecurityEvent { UserId = other.Id, SubjectHash = SecurityMonitor.Hash(other.Id), IpAddress = otherIp, Device = "OTHER_IP", Outcome = "Failed", OccurredUtc = alertTime.AddMinutes(-1) },
        new SecurityEvent { UserId = owner.Id, SubjectHash = SecurityMonitor.Hash(owner.Id), IpAddress = ip, Device = "DEVICE_LOGIN_AFTER_ALERT", Outcome = "Signed in", Success = true, OccurredUtc = alertTime.AddSeconds(10) });
    db.LoginRecords.AddRange(
        new LoginRecord { UserId = owner.Id, Outcome = "Failed", IpAddress = ip, Device = "OUTSIDE_TIME_WINDOW", OccurredUtc = alertTime.AddMinutes(-20) },
        new LoginRecord { UserId = owner.Id, Outcome = "Signed in", IpAddress = ip, Device = "DEVICE_LOGIN_AFTER_ALERT", OccurredUtc = alertTime.AddSeconds(10) },
        new LoginRecord { UserId = other.Id, Outcome = "Failed", IpAddress = ip, Device = "UNRELATED_LOGIN_LOG" });
    // Older Login logs need not have a matching security event. The support table must retain all nine.
    for (var i = 0; i < 9; i++) db.LoginRecords.Add(new LoginRecord { UserId = owner.Id, Outcome = "Signed in", IpAddress = i == 8 ? otherIp : ip,
        Device = i == 0 ? "Owner login log" : "LEGACY_LOGIN_ONLY_" + i, OccurredUtc = alertTime.AddDays(-1).AddMinutes(-i) });
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
Check(supportHtml.Contains("Recent sign-in attempts") && supportHtml.Contains("23 recorded attempts"), "support counts the same complete history as Login logs, including legacy records");
Check(supportHtml.Contains("Owner device 0") && supportHtml.Contains("LEGACY_LOGIN_ONLY_8"), "support renders all twenty-three account records rather than an eight-row preview");
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
Check(accountHtml.Contains("Related sign-in activity") && accountHtml.Contains("23 recorded attempts") && accountHtml.Contains("LEGACY_LOGIN_ONLY_8")
    && accountHtml.Contains("OUTSIDE_TIME_WINDOW") && accountHtml.Contains("DEVICE_LOGIN_AFTER_ALERT"), "account alerts show the full Login logs history before and after the alert, including older records and other IPs");
Check(!accountHtml.Contains("OTHER_ACCOUNT_SAME_IP") && !accountHtml.Contains("UNRELATED_LOGIN_LOG") && !accountHtml.Contains("Time window:"), "full account history excludes other accounts and no longer claims a restricted time window");
var networkHtml = await Html(admin, "/Admin/Alert/" + ipAlert);
Check(networkHtml.Contains("OTHER_ACCOUNT_SAME_IP") && networkHtml.Contains("Unrecognised account"), "IP alerts include relevant registered and unrecognised accounts");
Check(!networkHtml.Contains("OTHER_IP") && networkHtml.Contains("OUTSIDE_TIME_WINDOW"), "IP history includes older events from the same IP while excluding a different IP");
var ipLogLink = WebUtility.HtmlDecode(Regex.Match(networkHtml, "<a[^>]+href=\"([^\"]+)\"[^>]*>Open login logs</a>").Groups[1].Value);
Check(ipLogLink == "/Admin/LoginLogs?ipAddress=" + ip, "IP alert provides an exact IP-filtered Open login logs link");
Check(networkHtml.Contains("href=\"/Admin/UserDetails/" + owner.Id + "\"") && networkHtml.Contains("href=\"/Admin/UserDetails/" + other.Id + "\""), "registered accounts in IP evidence link to their own account details");
var unknownRow = Regex.Matches(networkHtml, "<tr>.*?</tr>", RegexOptions.Singleline).Select(m => m.Value)
    .Single(row => row.Contains("UNKNOWN_MATCHING_IDENTIFIER"));
Check(unknownRow.Contains("Unrecognised account") && !unknownRow.Contains("/Admin/UserDetails/"), "unrecognised accounts remain plain text without a broken account link");
var unknownHtml = await Html(admin, "/Admin/Alert/" + unknownAlert);
Check(unknownHtml.Contains("UNKNOWN_MATCHING_IDENTIFIER") && !unknownHtml.Contains("UNKNOWN_DIFFERENT_IDENTIFIER"), "unregistered-account alerts use the original subject hash rather than unrelated attempts from the IP");
var deviceHtml = await Html(admin, "/Admin/Alert/" + deviceAlert);
Check(deviceHtml.Contains("DEVICE_LOGIN_AFTER_ALERT") && deviceHtml.Contains("LEGACY_LOGIN_ONLY_8"), "new-device alerts show all account history rather than only the alert's IP or time");
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
Check(history.Contains("23 sign-in attempts match your filters."), "full Login logs and support agree on the total despite different security-event counts");
Check(history.Contains("name=\"UserId\""), "applying additional history filters retains the selected account");
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.LoginRecords.Add(new LoginRecord { UserId = owner.Id, Outcome = "Failed", IpAddress = ip,
        Device = "RECENT_LOGIN_RECORD_ONLY", OccurredUtc = DateTime.UtcNow });
    await db.SaveChangesAsync();
}
var refreshedSupport = await Html(admin, "/Admin/SupportThread/" + ticketId);
var refreshedHistory = await Html(admin, "/Admin/LoginLogs?userId=" + owner.Id);
var refreshedAlert = await Html(admin, "/Admin/Alert/" + accountAlert);
Check(refreshedSupport.Contains("24 recorded attempts") && refreshedSupport.Contains("RECENT_LOGIN_RECORD_ONLY")
    && refreshedHistory.Contains("24 sign-in attempts match your filters.") && refreshedHistory.Contains("RECENT_LOGIN_RECORD_ONLY"),
    "both pages include a newly recorded attempt and refresh the same total");
Check(refreshedAlert.Contains("24 recorded attempts") && refreshedAlert.Contains("RECENT_LOGIN_RECORD_ONLY"), "Security also refreshes the same complete account history and total");
var ipHistory = await Html(admin, ipLogLink);
Check(ipHistory.Contains("17 sign-in attempts match your filters.") && ipHistory.Contains("UNKNOWN_MATCHING_IDENTIFIER")
    && ipHistory.Contains("OTHER_ACCOUNT_SAME_IP"), "IP-filtered Login logs includes the same registered and unrecognised events as the IP alert");
Check(!ipHistory.Contains("OTHER_IP") && !ipHistory.Contains("LEGACY_LOGIN_ONLY_8"), "IP logs exclude records from a different IP");
Check(ipHistory.Contains("name=\"IpAddress\"") && ipHistory.Contains("value=\"" + ip + "\"") && ipHistory.Contains("IP address: <strong>" + ip + "</strong>"),
    "the selected IP is visible and retained when submitting more filters");
var successfulIpHistory = await Html(admin, ipLogLink + "&result=success");
Check(successfulIpHistory.Contains("2 sign-in attempts match your filters.") && successfulIpHistory.Contains("DEVICE_LOGIN_AFTER_ALERT")
    && !successfulIpHistory.Contains("Owner device 0"), "result filters stay within the selected IP");
var noPrefixMatch = await Html(admin, "/Admin/LoginLogs?ipAddress=192.0.2.3");
Check(noPrefixMatch.Contains("No sign-ins to show"), "IP matching is exact rather than a partial address match");
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    for (var i = 0; i < 5; i++) db.SecurityEvents.Add(new SecurityEvent { SubjectHash = "ip-pagination-" + i,
        IpAddress = ip, Device = "IP_PAGE_" + i, Outcome = "Failed", OccurredUtc = alertTime.AddDays(-2).AddMinutes(-i) });
    await db.SaveChangesAsync();
}
var pagedIpHistory = await Html(admin, ipLogLink);
var ipNextLink = WebUtility.HtmlDecode(Regex.Match(pagedIpHistory, "<a[^>]+href=\"([^\"]+)\"[^>]+rel=\"next\"").Groups[1].Value);
Check(pagedIpHistory.Contains("22 sign-in attempts match your filters.") && ipNextLink.Contains("ipAddress=" + ip) && ipNextLink.Contains("page=2"),
    "IP history pagination preserves the exact IP filter");
var nextIpPage = await Html(admin, ipNextLink);
Check(nextIpPage.Contains("IP_PAGE_4") && !nextIpPage.Contains("OTHER_IP"), "the next page includes older events from the same IP only");
using var audit = Client(auditor);
var auditHtml = await Html(audit, "/Admin/Alert/" + accountAlert);
Check(auditHtml.Contains("Related sign-in activity") && !auditHtml.Contains("Save investigation") && !auditHtml.Contains("Review sessions"), "read-only auditors see evidence without management controls");
var auditNetwork = await Html(audit, "/Admin/Alert/" + ipAlert);
Check(auditNetwork.Contains("Open login logs") && !auditNetwork.Contains("href=\"/Admin/UserDetails/"), "auditors retain the log link without links to restricted account-management pages");
var auditIpLogs = await Html(audit, ipLogLink);
Check(auditIpLogs.Contains("UNKNOWN_MATCHING_IDENTIFIER") && !auditIpLogs.Contains("href=\"/Admin/UserDetails/"), "auditors can inspect IP logs without gaining account-management navigation");
using var student = Client(other);
Check((await student.GetAsync("/Admin/Alert/" + accountAlert)).StatusCode != HttpStatusCode.OK, "students cannot access another account's investigation context");
Check((await student.GetAsync(ipLogLink)).StatusCode != HttpStatusCode.OK, "students cannot access Admin IP-filtered login logs");
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
await File.WriteAllTextAsync(Path.Combine(rendered, "ip-logs.html"), ipHistory);
Console.WriteLine($"All {checks} investigation-context HTTP checks passed. Isolated database only.");
