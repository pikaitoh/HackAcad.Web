using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HackAcad.Web.Controllers;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using MimeKit;

[assembly: WebApplicationFactoryContentRoot("HackAcad.Web", "../../../../../", "HackAcad.Web.csproj", "0")]
if (!(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ?? "").Contains("database=hackacad_uiresearch_20261016", StringComparison.OrdinalIgnoreCase)
    || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Email__DevelopmentPickupDirectory"))
    || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Sms__DevelopmentPickupDirectory"))) throw new Exception("Use the isolated database and local message captures only.");
Environment.SetEnvironmentVariable("Authentication__GitHub__ClientId", "test-client");
Environment.SetEnvironmentVariable("Authentication__GitHub__ClientSecret", "test-secret");
var clock = new TestClock();
await using var factory = new WebApplicationFactory<AccountController>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
{
    services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
    services.AddSingleton<IStartupFilter, TestClientAddress>();
    services.PostConfigure<GoogleOptions>("Google", o => o.Backchannel = new HttpClient(new ProviderBackchannel()));
    services.PostConfigure<OAuthOptions>("GitHub", o => o.Backchannel = new HttpClient(new ProviderBackchannel()));
}));
const string account = "/Identity/Account/", password = "HackAcad@123!";
var prefix = "connections." + Guid.NewGuid().ToString("N")[..8];
int checks = 0, clientAddress = 0; long nextKey = DateTime.UtcNow.Ticks;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
HttpClient Client()
{
    var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
    client.DefaultRequestHeaders.Add("X-Test-Address", "198.51.100." + (++clientAddress));
    return client;
}
async Task<(string Path, string Body)> Follow(HttpClient c, HttpResponseMessage r)
{
    var path = r.RequestMessage!.RequestUri!.PathAndQuery;
    for (int i = 0; i < 10 && r.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther; i++)
    { var next = new Uri(c.BaseAddress!, r.Headers.Location!); if (next.Host != "localhost") throw new Exception("Unexpected external redirect"); path = next.PathAndQuery; r = await c.GetAsync(path); }
    if (!r.IsSuccessStatusCode) throw new Exception($"HTTP {(int)r.StatusCode} at {path}: " + (await r.Content.ReadAsStringAsync())[..Math.Min(600, (await r.Content.ReadAsStringAsync()).Length)]);
    return (path, await r.Content.ReadAsStringAsync());
}
async Task<HttpResponseMessage> RawPost(HttpClient c, string action, Dictionary<string, string> data, string source = "Settings")
{
    var html = await c.GetStringAsync(account + source);
    data["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]+value=\"([^\"]+)\"").Groups[1].Value);
    return await c.PostAsync(account + action, new FormUrlEncodedContent(data));
}
async Task<(string Path, string Body)> Post(HttpClient c, string action, Dictionary<string, string> data, string source = "Settings") => await Follow(c, await RawPost(c, action, data, source));
Task<(string Path, string Body)> Login(HttpClient c, string email) => Post(c, "Login", new() { { "Email", email }, { "Password", password } }, "Login");
async Task<(string Path, string Body)> OAuth(HttpClient c, string provider, string email, string key, string action, Dictionary<string, string>? fields = null)
{
    var r = await RawPost(c, action, fields ?? new(), action.StartsWith("ConfirmDisconnect") ? "Disconnect?provider=GitHub" : "Login");
    if (r.StatusCode != HttpStatusCode.Redirect || r.Headers.Location?.IsAbsoluteUri != true) throw new Exception("No OAuth challenge");
    var state = QueryHelpers.ParseQuery(r.Headers.Location.Query)["state"].ToString();
    var code = WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { sub = key, id = key, email, email_verified = true, name = "Provider Owner", login = "github-owner" }));
    return await Follow(c, await c.GetAsync("/signin-" + provider.ToLowerInvariant() + "?state=" + Uri.EscapeDataString(state) + "&code=" + code));
}
async Task<(string Id, string Email, string Google, string GitHub)> Fixture(string label, string role = AccountRoles.Student, bool localPassword = true)
{
    using var scope = factory.Services.CreateScope();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var user = new ApplicationUser { Email = prefix + "." + label + "@example.edu", UserName = prefix + "." + label + "@example.edu", EmailConfirmed = true, DisplayName = "Connection " + label };
    var created = await scope.ServiceProvider.GetRequiredService<UserAccounts>().CreateAsync(user, password, role);
    if (!created.Succeeded) throw new Exception("Cannot create fixture");
    if (!localPassword) { user.PasswordHash = null; await users.UpdateAsync(user); }
    var google = (nextKey++).ToString(); var github = (nextKey++).ToString();
    await users.AddLoginAsync(user, new UserLoginInfo("Google", google, "Google"));
    await users.AddLoginAsync(user, new UserLoginInfo("GitHub", github, "GitHub"));
    return (user.Id, user.Email!, google, github);
}
async Task<int> Events(string userId, string? action = null)
{
    using var scope = factory.Services.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AdminActivities.CountAsync(a => a.TargetType == "Account security" && a.TargetId == userId && (action == null || a.Action == action));
}
IEnumerable<MimeMessage> Mails() => Directory.GetFiles(Environment.GetEnvironmentVariable("Email__DevelopmentPickupDirectory")!, "*.eml").Select(path => MimeMessage.Load(path));
string Totp(string key)
{
    var bytes = new List<byte>(); int buffer = 0, bits = 0;
    foreach (var letter in key) { buffer = (buffer << 5) | "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(letter); bits += 5; if (bits >= 8) { bits -= 8; bytes.Add((byte)(buffer >> bits)); } }
    var counter = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
    var hash = HMACSHA1.HashData(bytes.ToArray(), counter); var offset = hash[^1] & 15;
    return ((BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff) % 1000000).ToString("D6");
}
try
{
    Check((await Client().GetAsync(account + "Disconnect?provider=Google")).StatusCode == HttpStatusCode.Redirect, "disconnect requires an authenticated owner");
    foreach (var role in AccountRoles.All)
    {
        var f = await Fixture(role.ToLowerInvariant(), role); using var owner = Client(); await Login(owner, f.Email);
        var page = await owner.GetStringAsync(account + "Settings");
        Check(page.Contains("Disconnect Google") && page.Contains("Disconnect GitHub"), role + " sees both disconnect controls");
        var verifyPage = await owner.GetStringAsync(account + "Disconnect?provider=GitHub");
        Check(verifyPage.Contains("Verify your identity") && verifyPage.Contains("Enter your HackAcad password") && !verifyPage.Contains("Any enabled two-factor"), role + " sees the current verification step in plain language");
        Check(Regex.IsMatch(verifyPage, @"<div class=""form-actions""><button[^>]*>Verify password</button><button[^>]*form=""disconnect-cancel""[^>]*>Cancel</button></div>"), role + " password verification and independent Cancel share an action row");
        Check(page.Contains("Account details will appear after your next"), role + " legacy bindings show an honest metadata fallback");
        Check((await owner.PostAsync(account + "Disconnect?provider=GitHub", new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode == HttpStatusCode.BadRequest, role + " final disconnect requires CSRF");
        var bypass = await Post(owner, "Disconnect?provider=GitHub", new() { { "Confirm", "true" }, { "Ready", "true" }, { "VerifiedMethod", "Password" } });
        Check(bypass.Body.Contains("Verify a remaining sign-in method again") && await Events(f.Id) == 0, role + " forged proof flags cannot disconnect or create audit noise");
        var wrong = await Post(owner, "VerifyDisconnect?provider=GitHub", new() { { "CurrentPassword", "wrong" } });
        Check(wrong.Body.Contains("current password is incorrect"), role + " wrong password cannot create a grant");
        var verified = await Post(owner, "VerifyDisconnect?provider=GitHub", new() { { "CurrentPassword", password } });
        Check(verified.Body.Contains("Verification complete. Finish disconnecting GitHub before") && Regex.IsMatch(verified.Body, @"<time datetime=""[^""]+"">[0-9]{2}:[0-9]{2}:[0-9]{2}</time>"), role + " sees the real verification deadline in their time zone");
        Check(verified.Body.Contains("Verified with Password"), role + " verifies a remaining local login");
        var noConfirm = await Post(owner, "Disconnect?provider=GitHub", new());
        Check(noConfirm.Body.Contains("Confirm that you understand") && await Events(f.Id) == 0, role + " requires explicit confirmation and records no-op attempts nowhere");
        var done = await Post(owner, "Disconnect?provider=GitHub", new() { { "Confirm", "true" } });
        Check(done.Path.EndsWith("Login") && done.Body.Contains("GitHub disconnected"), role + " successful disconnect signs the current browser out");
        Check(await Events(f.Id, "GitHub disconnected") == 1, role + " disconnect records exactly one persistent security event");
        Check(Mails().Any(m => m.Subject == "HackAcad security: GitHub disconnected" && m.To.Mailboxes.Any(a => a.Address == f.Email)), role + " security email is captured for the verified HackAcad mailbox");
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Check(await users.FindByLoginAsync("GitHub", f.GitHub) == null && (await users.FindByLoginAsync("Google", f.Google))?.Id == f.Id, role + " removes only the selected provider");
            Check((await scope.ServiceProvider.GetRequiredService<UserAccounts>().Query().SingleAsync(u => u.Id == f.Id)).Role == role, role + " role is preserved");
            Check(await db.UserNotifications.AnyAsync(n => n.UserId == f.Id && n.Title == "GitHub disconnected"), role + " gets an in-app security notification");
        }
        using var returning = Client(); await Login(returning, f.Email);
        Check((await returning.GetStringAsync(account + "SecurityHistory")).Contains("GitHub disconnected"), role + " can read its own saved security activity");
    }

    var external = await Fixture("external", localPassword: false); using var externalOwner = Client();
    await OAuth(externalOwner, "Google", external.Email, external.Google, "ExternalLogin?provider=Google");
    var details = await externalOwner.GetStringAsync(account + "Settings");
    Check(details.Contains(external.Email) && details.Contains("Provider Owner"), "a verified provider callback persists its own identity snapshot");
    var blockedMethod = await RawPost(externalOwner, "ConfirmDisconnectWithProvider?provider=GitHub&retainedProvider=GitHub", new());
    Check(blockedMethod.StatusCode == HttpStatusCode.BadRequest, "the provider being removed cannot be used as the remaining proof");
    var wrongIdentity = await OAuth(externalOwner, "Google", external.Email, (nextKey++).ToString(), "ConfirmDisconnectWithProvider?provider=GitHub&retainedProvider=Google");
    Check(wrongIdentity.Path.EndsWith("Settings") && !wrongIdentity.Body.Contains("Verified with Google"), "a different provider identity cannot authorize someone else's disconnection");
    var verifiedExternal = await OAuth(externalOwner, "Google", external.Email, external.Google, "ConfirmDisconnectWithProvider?provider=GitHub&retainedProvider=Google");
    Check(verifiedExternal.Body.Contains("Verified with Google"), "passwordless owner can prove a remaining linked Google identity");
    using var differentBrowser = Client(); await OAuth(differentBrowser, "Google", external.Email, external.Google, "ExternalLogin?provider=Google");
    Check((await Post(differentBrowser, "Disconnect?provider=GitHub", new() { { "Confirm", "true" } })).Body.Contains("Verify a remaining sign-in method again"), "proof cannot be reused by another browser session");
    await Post(externalOwner, "CancelDisconnect?provider=GitHub", new());
    Check((await Post(externalOwner, "Disconnect?provider=GitHub", new() { { "Confirm", "true" } })).Body.Contains("Verify a remaining sign-in method again"), "cancel destroys the browser-bound verification grant");
    await OAuth(externalOwner, "Google", external.Email, external.Google, "ConfirmDisconnectWithProvider?provider=GitHub&retainedProvider=Google");
    clock.Advance(TimeSpan.FromMinutes(6));
    Check((await Post(externalOwner, "Disconnect?provider=GitHub", new() { { "Confirm", "true" } })).Body.Contains("Verify a remaining sign-in method again"), "disconnect proof expires after five minutes");
    await OAuth(externalOwner, "Google", external.Email, external.Google, "ConfirmDisconnectWithProvider?provider=GitHub&retainedProvider=Google");
    await Post(externalOwner, "Disconnect?provider=GitHub", new() { { "Confirm", "true" } });
    using var oneMethod = Client(); await OAuth(oneMethod, "Google", external.Email, external.Google, "ExternalLogin?provider=Google");
    var last = await oneMethod.GetStringAsync(account + "Disconnect?provider=Google");
    Check(last.Contains("Keep at least one available sign-in method") && !last.Contains(">Verify password<"), "cannot disconnect the only remaining passwordless login");
    Check((await Post(oneMethod, "Disconnect?provider=Google", new() { { "Confirm", "true" }, { "Ready", "true" } })).Body.Contains("Keep at least one available"), "last-method guard is enforced on the server");

    var mail = await Fixture("email"); using var mailOwner = Client(); await Login(mailOwner, mail.Email);
    var githubCallback = await OAuth(Client(), "GitHub", mail.Email, mail.GitHub, "ExternalLogin?provider=GitHub");
    var newEmail = prefix + ".changed@example.net";
    var change = await Post(mailOwner, "ChangeEmail", new() { { "NewEmail", newEmail }, { "CurrentPassword", password } }, "ChangeEmail");
    Check(change.Body.Contains("does not change or disconnect"), "change-email screen explains retained provider connections");
    var confirmation = Mails().Single(m => m.Subject == "Confirm your new HackAcad email" && m.To.Mailboxes.Any(a => a.Address == newEmail));
    var link = new Uri(Regex.Match(confirmation.TextBody!, @"https?://\S+/Identity/Account/ConfirmEmailChange\?\S+").Value);
    Check((await Follow(mailOwner, await mailOwner.GetAsync(link.PathAndQuery))).Body.Contains("Email changed"), "new mailbox must confirm the requested email change");
    Check(await Events(mail.Id, "Account email changed") == 1, "confirmed email change creates one audit event");
    Check(Mails().Count(m => m.Subject == "HackAcad security: Account email changed" && m.To.Mailboxes.Any(a => a.Address == mail.Email || a.Address == newEmail)) == 2, "email change notifies both old and new verified addresses");
    using var afterChange = Client(); await Login(afterChange, newEmail);
    Check((await afterChange.GetStringAsync(account + "Settings")).Contains("Disconnect GitHub"), "email change preserves connected methods");
    Check((await OAuth(Client(), "Google", mail.Email, mail.Google, "ExternalLogin?provider=Google")).Path.EndsWith("Dashboard"), "original Google identity still works after local email changes");
    Check(!(await afterChange.GetStringAsync(account + "SecurityHistory")).Contains("GitHub disconnected"), "security activity never exposes another user's records");
    Check((await Follow(Client(), await Client().GetAsync(link.PathAndQuery))).Body.Contains("This link isn't available") && await Events(mail.Id, "Account email changed") == 1, "replayed confirmation cannot create duplicate email-change records");

    var two = await Fixture("twofactor"); using var twoOwner = Client();
    string authKey;
    using (var scope = factory.Services.CreateScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var u = (await users.FindByIdAsync(two.Id))!;
        await users.ResetAuthenticatorKeyAsync(u); authKey = (await users.GetAuthenticatorKeyAsync(u))!; await users.SetTwoFactorEnabledAsync(u, true);
    }
    await Login(twoOwner, two.Email);
    await Post(twoOwner, "LoginTwoFactor", new() { { "Code", Totp(authKey) } }, "LoginTwoFactor");
    var pending = await Post(twoOwner, "VerifyDisconnect?provider=GitHub", new() { { "CurrentPassword", password } });
    Check(pending.Body.Contains("Verification or recovery code") && !pending.Body.Contains("name=\"Confirm\""), "password alone cannot bypass enabled authenticator 2FA");
    var invalidCode = await Post(twoOwner, "VerifyDisconnect?provider=GitHub", new() { { "Code", "000000" } });
    Check(invalidCode.Body.Contains("Enter a current verification code"), "invalid authenticator code does not authorize removal");
    var verifiedCode = await Post(twoOwner, "VerifyDisconnect?provider=GitHub", new() { { "Code", Totp(authKey) } });
    Check(verifiedCode.Body.Contains("name=\"Confirm\""), "correct authenticator proof enables explicit final confirmation");
    var twoDone = await Post(twoOwner, "Disconnect?provider=GitHub", new() { { "Confirm", "true" } });
    Check(twoDone.Path.EndsWith("Login") && await Events(two.Id, "GitHub disconnected") == 1, "2FA-protected disconnect completes after both checks");

    var sms = await Fixture("sms"); var number = "+1555" + Random.Shared.Next(1000000, 9999999); using var smsOwner = Client();
    string SmsCode() => Directory.GetFiles(Environment.GetEnvironmentVariable("Sms__DevelopmentPickupDirectory")!, "*.json").Select(path => JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone()).Where(v => v.GetProperty("number").GetString() == number).OrderBy(v => v.GetProperty("expires").GetDateTimeOffset()).Last().GetProperty("code").GetString()!;
    using (var scope = factory.Services.CreateScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var u = (await users.FindByIdAsync(sms.Id))!;
        u.PhoneNumber = number; u.PhoneNumberConfirmed = true; u.TwoFactorEnabled = true; u.SmsTwoFactorEnabled = true; await users.UpdateAsync(u);
    }
    await Login(smsOwner, sms.Email); await Post(smsOwner, "LoginSms", new() { { "Code", SmsCode() } }, "LoginSms");
    clock.Advance(TimeSpan.FromMinutes(2));
    var smsPending = await Post(smsOwner, "VerifyDisconnect?provider=GitHub", new() { { "CurrentPassword", password } });
    Check(smsPending.Body.Contains("SMS code sent to your verified number") && !smsPending.Body.Contains("name=\"Confirm\""), "SMS-enabled users need a new purpose-bound code after password proof");
    var smsCode = SmsCode();
    Check((await Post(smsOwner, "VerifyDisconnect?provider=GitHub", new() { { "Code", smsCode } })).Body.Contains("name=\"Confirm\""), "the intended SMS code enables final confirmation");
    Check((await Post(smsOwner, "Disconnect?provider=GitHub", new() { { "Confirm", "true" } })).Path.EndsWith("Login"), "SMS-protected disconnection completes without any live SMS");

    var race = await Fixture("race", localPassword: false);
    using (var scope = factory.Services.CreateScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var u = (await users.FindByIdAsync(race.Id))!;
        var error = await scope.ServiceProvider.GetRequiredService<AccountConnections>().DisconnectAsync(u, "Google", race.Google, "GitHub", race.GitHub, u.SecurityStamp!, new HashSet<string> { "Google" });
        Check(error != null && (await users.GetLoginsAsync(u)).Count == 2, "an unconfigured remaining provider cannot satisfy the last-method guard");
    }
    string raceStamp;
    using (var scope = factory.Services.CreateScope()) raceStamp = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(race.Id))!.SecurityStamp!;
    async Task<string?> Race(string target, string key, string retained, string retainedKey)
    {
        using var scope = factory.Services.CreateScope(); var u = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(race.Id))!;
        return await scope.ServiceProvider.GetRequiredService<AccountConnections>().DisconnectAsync(u, target, key, retained, retainedKey, raceStamp, new HashSet<string> { "Google", "GitHub" });
    }
    var outcomes = await Task.WhenAll(Race("Google", race.Google, "GitHub", race.GitHub), Race("GitHub", race.GitHub, "Google", race.Google));
    using (var scope = factory.Services.CreateScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var u = (await users.FindByIdAsync(race.Id))!;
        Check(outcomes.Count(e => e == null) == 1 && (await users.GetLoginsAsync(u)).Count == 1, "concurrent disconnects cannot remove both remaining login methods");
        Check(await Events(race.Id) == 1, "only the committed concurrent action creates a security record");
    }
    var failure = await Fixture("mailfailure"); using var failureOwner = Client(); await Login(failureOwner, failure.Email);
    await Post(failureOwner, "VerifyDisconnect?provider=GitHub", new() { { "CurrentPassword", password } });
    var config = factory.Services.GetRequiredService<IConfiguration>(); var capture = config["Email:DevelopmentPickupDirectory"];
    try
    {
        config["Email:DevelopmentPickupDirectory"] = Path.Combine(factory.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().ContentRootPath, "wwwroot");
        var failedDelivery = await Post(failureOwner, "Disconnect?provider=GitHub", new() { { "Confirm", "true" } });
        Check(failedDelivery.Path.EndsWith("Login") && failedDelivery.Body.Contains("email notice could not be delivered"), "mail delivery failure reports the saved change truthfully");
        Check(await Events(failure.Id, "GitHub disconnected") == 1, "failed email delivery does not lose the committed security event");
    }
    finally { config["Email:DevelopmentPickupDirectory"] = capture; }
    foreach (var count in new[] { 0, 1, 2, 7, 8, 30, int.MaxValue })
    foreach (var requested in new[] { int.MinValue, 1, 4, 15, int.MaxValue })
    {
        var pager = new PaginationModel(requested, count); var numbers = pager.Numbers.ToList();
        Check(numbers.Count <= 7 && numbers.First() == 1 && numbers.Last() == pager.PageCount
            && numbers.Contains(pager.Page) && numbers.SequenceEqual(numbers.Distinct().Order()), "bounded page window includes current and boundary pages: " + count + "/" + requested);
    }
    var paged = await Fixture("pagination", AccountRoles.Admin); using var pageOwner = Client(); await Login(pageOwner, paged.Email);
    using (var scope = factory.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        for (var index = 0; index < 41; index++)
        {
            db.LoginRecords.Add(new LoginRecord { UserId = paged.Id, Outcome = "Signed in", Device = "Pagination fixture", IpAddress = "127.0.0.1" });
            db.AdminActivities.Add(new AdminActivity { ActorUserId = paged.Id, TargetId = paged.Id, TargetType = "Account security", TargetName = prefix, Action = "Pagination fixture", Details = "Page check " + index });
            db.UserNotifications.Add(new UserNotification { UserId = paged.Id, EventKey = prefix + ":" + index, Title = "Pagination fixture", Message = "Page check", Link = "/Notifications", CreatedUtc = DateTime.UtcNow });
        }
        await db.SaveChangesAsync();
    }
    foreach (var path in new[] { account + "LoginHistory", account + "SecurityHistory", "/Notifications", "/Admin/AuditLog?mine=true&search=" + prefix, "/Admin/LoginLogs?search=" + paged.Email + "&result=success&from=2020-01-01&to=2099-01-01" })
    {
        var first = await pageOwner.GetStringAsync(path);
        var nav = Regex.Match(first, @"<nav class=""pagination-bar""[\s\S]*?</nav>").Value;
        Check(nav.Contains("aria-current=\"page\"") && nav.Contains("aria-label=\"Page 2\"") && nav.Contains("aria-disabled=\"true\">Previous"), path + " renders numbered navigation with disabled Previous");
        var pageLink = WebUtility.HtmlDecode(Regex.Match(nav, @"href=""([^""]+)"" aria-label=""Page 2""").Groups[1].Value);
        var parsed = QueryHelpers.ParseQuery(new Uri(pageOwner.BaseAddress!, pageLink).Query);
        var original = QueryHelpers.ParseQuery(new Uri(pageOwner.BaseAddress!, path).Query);
        Check(parsed["page"] == "2" && original.All(pair => parsed[pair.Key] == pair.Value), path + " numbered links retain every active filter");
        var middle = await pageOwner.GetStringAsync(pageLink);
        Check(middle.Contains("aria-current=\"page\" aria-label=\"Page 2\"") && middle.Contains("rel=\"prev\"") && middle.Contains("rel=\"next\""), path + " supports direct navigation and both directions");
        var lastPage = await pageOwner.GetStringAsync(path + (path.Contains('?') ? "&" : "?") + "page=999999");
        Check(lastPage.Contains("aria-current=\"page\" aria-label=\"Page 3\"") && lastPage.Contains("aria-disabled=\"true\">Next"), path + " clamps out-of-range pages and disables Next");
    }
    foreach (var path in new[] { "/Admin/Users?search=" + paged.Email + "&role=Admin&status=active", "/Admin/Courses?search=" + prefix + "&status=draft", "/Admin/Applications?search=" + prefix })
    {
        var html = await pageOwner.GetStringAsync(path);
        Check(html.Contains("aria-current=\"page\" aria-label=\"Page 1\"") && html.Contains("aria-disabled=\"true\">Previous") && html.Contains("aria-disabled=\"true\">Next"), path + " uses the shared pager for single-page or empty filtered lists");
    }
    Check((await pageOwner.GetStringAsync("/Admin/Users?search=" + paged.Email)).Contains("1 user(s)"), "user pagination includes the filtered total");
    Check((await pageOwner.GetStringAsync("/Admin/Courses?search=" + prefix)).Contains("0 course(s)"), "course pagination includes the filtered total even for no matches");
    Check(!(await pageOwner.GetStringAsync(account + "SecurityHistory")).Contains("Back to connected accounts"), "security history omits the redundant return button");
    Console.WriteLine($"All {checks} account connection checks passed. No real OAuth, SMS or email sent.");
}
finally
{
    using var scope = factory.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var fixtures = await users.Users.Where(u => u.Email != null && u.Email.StartsWith(prefix)).ToListAsync();
    var ids = fixtures.Select(u => u.Id).ToList();
    await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AdminActivities.Where(a => a.TargetType == "Account security" && ids.Contains(a.TargetId)).ExecuteDeleteAsync();
    foreach (var user in fixtures) await users.DeleteAsync(user);
}

// Distinct simulated clients retain the production per-IP rate limiter.
sealed class TestClientAddress : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, proceed) => { context.Connection.RemoteIpAddress = IPAddress.Parse(context.Request.Headers["X-Test-Address"].FirstOrDefault() ?? "198.51.100.254"); await proceed(); });
        next(app);
    };
}

sealed class TestClock : TimeProvider
{
    private TimeSpan offset;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + offset;
    public void Advance(TimeSpan time) => offset += time;
}
sealed class ProviderBackchannel : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        object payload;
        if (request.Method == HttpMethod.Post)
        {
            var form = QueryHelpers.ParseQuery("?" + await request.Content!.ReadAsStringAsync(token));
            payload = new { access_token = form["code"].ToString(), token_type = "Bearer", expires_in = 3600 };
        }
        else
        {
            var data = JsonSerializer.Deserialize<JsonElement>(WebEncoders.Base64UrlDecode(request.Headers.Authorization!.Parameter!));
            if (request.RequestUri!.Host == "api.github.com")
                payload = request.RequestUri.AbsolutePath == "/user/emails" ? new[] { new { email = data.GetProperty("email").GetString(), primary = true, verified = true } } : new { id = long.Parse(data.GetProperty("id").GetString()!), login = "github-owner", email = (string?)null };
            else payload = data;
        }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
    }
}
