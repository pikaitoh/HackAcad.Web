using System.Net;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Text.RegularExpressions;
using HackAcad.Web.Controllers;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
[assembly: WebApplicationFactoryContentRoot("HackAcad.Web", "../../../../../", "HackAcad.Web.csproj", "0")]

if (!(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ?? "").Contains("database=hackacad_uiresearch_20261016", StringComparison.OrdinalIgnoreCase)) throw new Exception("Use the isolated test database only.");
var capture = Environment.GetEnvironmentVariable("Sms__DevelopmentPickupDirectory");
if (string.IsNullOrWhiteSpace(capture)) throw new Exception("Local SMS capture is required.");
Directory.CreateDirectory(capture);
var mailCapture = Environment.GetEnvironmentVariable("Email__DevelopmentPickupDirectory");
if (string.IsNullOrWhiteSpace(mailCapture)) throw new Exception("Local email capture is required.");
Directory.CreateDirectory(mailCapture);
var clock = new TestClock();
await using var factory = new WebApplicationFactory<AccountController>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock))));
const string account = "/Identity/Account/", password = "HackAcad@123!", newPassword = "Changed@456!";
var prefix = "phoneauth." + Guid.NewGuid().ToString("N")[..10];
var checks = 0; var nextNumber = Random.Shared.Next(1000000, 9000000);
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
async Task<(string Path, string Body)> Follow(HttpClient client, HttpResponseMessage response)
{
    var path = response.RequestMessage!.RequestUri!.PathAndQuery;
    for (var i = 0; i < 10 && response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther; i++)
    { var url = new Uri(client.BaseAddress!, response.Headers.Location!); if (url.Host != "localhost") throw new Exception("External redirect"); path = url.PathAndQuery; response = await client.GetAsync(path); }
    if (!response.IsSuccessStatusCode) throw new Exception("Unexpected HTTP " + (int)response.StatusCode + " at " + path + ": " + (await response.Content.ReadAsStringAsync())[..Math.Min(800, (await response.Content.ReadAsStringAsync()).Length)]);
    return (path, await response.Content.ReadAsStringAsync());
}
async Task<HttpResponseMessage> RawPost(HttpClient client, string action, Dictionary<string, string> fields, string source)
{
    var page = await client.GetStringAsync(account + source);
    var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]+value=\"([^\"]+)\"").Groups[1].Value;
    if (token.Length == 0) throw new Exception("No CSRF token at " + source);
    fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(token);
    return await client.PostAsync(account + action, new FormUrlEncodedContent(fields));
}
async Task<(string Path, string Body)> Post(HttpClient client, string action, Dictionary<string, string> fields, string source = "TwoFactor") => await Follow(client, await RawPost(client, action, fields, source));
Task<(string Path, string Body)> Login(HttpClient client, string identifier, string secret = password) => Post(client, "Login", new() { { "Email", identifier }, { "Password", secret } }, "Login");
async Task<(string Id, string Email, string Phone)> Fixture(string label, string role = AccountRoles.Student, bool confirmed = true, bool disabled = false, bool sms = false)
{
    var email = prefix + "." + label + "@hackacad.local"; var phone = "+1555" + (++nextNumber);
    using var scope = factory.Services.CreateScope();
    var user = new ApplicationUser { Email = email, UserName = email, DisplayName = "Phone security " + label, EmailConfirmed = true, PhoneNumber = phone, PhoneNumberConfirmed = confirmed, IsDisabled = disabled, SmsTwoFactorEnabled = sms, TwoFactorEnabled = sms };
    var result = await scope.ServiceProvider.GetRequiredService<UserAccounts>().CreateAsync(user, password, role);
    if (!result.Succeeded) throw new Exception("Fixture creation failed");
    return (user.Id, email, phone);
}
(string Code, int Count) Messages(string number)
{
    var values = Directory.GetFiles(capture, "*.json").Select(path => JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone()).Where(value => value.GetProperty("number").GetString() == number).OrderBy(value => value.GetProperty("expires").GetDateTimeOffset()).ToArray();
    return (values.LastOrDefault().ValueKind == JsonValueKind.Undefined ? "" : values[^1].GetProperty("code").GetString()!, values.Length);
}
string[] ResetLinks(string recipient) => Directory.GetFiles(mailCapture, "*.eml").OrderBy(File.GetLastWriteTimeUtc)
    .Select(path => MimeMessage.Load(path)).Where(message => message.Subject == "Reset your HackAcad password" && message.To.Mailboxes.Any(address => address.Address.Equals(recipient, StringComparison.OrdinalIgnoreCase)))
    .Select(message => new Uri(Regex.Match(message.TextBody, @"https?://\S+/Identity/Account/ResetPassword\?\S+").Value).PathAndQuery).ToArray();
string Wrong(string code) => code == "000000" ? "999999" : "000000";
string[] Codes(string body) => Regex.Matches(body, "<li><code>([^<]+)</code></li>").Select(m => m.Groups[1].Value).ToArray();
try
{
    var anonymous = Client();
    Check((await anonymous.GetAsync(account + "EnableSmsTwoFactor")).StatusCode == HttpStatusCode.Redirect, "anonymous users cannot configure SMS 2FA");
    Check((await Follow(anonymous, await anonymous.GetAsync(account + "LoginSms"))).Path.Contains("Login"), "an SMS login challenge requires a password or Google pre-authentication ticket");
    Check((await anonymous.PostAsync(account + "ForgotPassword", new FormUrlEncodedContent(new Dictionary<string, string> { { "Email", "test@example.org" } }))).StatusCode == HttpStatusCode.BadRequest, "email recovery requires CSRF protection");
    var plain = await Fixture("plain"); var owner = Client();
    Check(!(await Login(owner, plain.Phone)).Path.Contains("Login"), "a verified phone number plus password signs in");
    Check((await owner.GetAsync(account + "Profile")).StatusCode == HttpStatusCode.OK, "phone sign-in creates a normal tracked session");
    Check((await Login(Client(), plain.Phone, "wrong")).Body.Contains("Unable to sign in"), "phone sign-in still requires the correct password");
    var unverified = await Fixture("unverified", confirmed: false);
    Check((await Login(Client(), unverified.Phone)).Body.Contains("Unable to sign in"), "an unverified phone cannot identify a login account");
    var unverifiedOwner = Client(); await Login(unverifiedOwner, unverified.Email);
    var smsEntry = await unverifiedOwner.GetStringAsync(account + "TwoFactor");
    Check(!smsEntry.Contains("Add a verified phone number") && Regex.IsMatch(smsEntry, "<a[^>]+href=\"/Identity/Account/PhoneNumber\"[^>]*>Set up SMS</a>"), "without a verified phone, the single Set up SMS control navigates to Phone number without submitting a password");
    Check((await Follow(unverifiedOwner, await unverifiedOwner.GetAsync(account + "PhoneNumber"))).Body.Contains("Your phone number"), "the SMS setup entry opens the existing phone settings page");
    var disabled = await Fixture("disabled", disabled: true);
    var disabledLogin = await Login(Client(), disabled.Phone);
    Check(disabledLogin.Path.EndsWith("Login") && disabledLogin.Body.Contains("This account has been disabled"), "a disabled account cannot log in by phone and is directed to support");
    using (var scope = factory.Services.CreateScope())
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var duplicate = new ApplicationUser { UserName = prefix + ".duplicate@hackacad.local", Email = prefix + ".duplicate@hackacad.local", PhoneNumber = plain.Phone };
        var blocked = false; try { await userManager.CreateAsync(duplicate, password); } catch (DbUpdateException) { blocked = true; }
        Check(blocked, "the database prevents the same number belonging to two accounts");
    }
    var passwordDenied = await Post(owner, "SendPhoneCode", new() { { "Phone.PhoneNumber", "+15551234567" } }, "ChangePhone");
    Check(passwordDenied.Body.Contains("field-validation-error"), "binding a recovery/login phone requires the current password");
    var removeDenied = await Post(owner, "RemovePhone", new() { { "CurrentPassword", "wrong" } }, "RemovePhone");
    Check(removeDenied.Body.Contains("Your current password is incorrect"), "removing a phone requires password confirmation");

    foreach (var role in new[] { AccountRoles.Student, AccountRoles.Instructor, AccountRoles.Admin })
    {
        var user = await Fixture(role.ToLowerInvariant(), role); var browser = Client(); await Login(browser, user.Email);
        var setup = await Post(browser, "BeginSmsTwoFactor", new() { { "CurrentPassword", password } });
        Check(setup.Path.EndsWith("EnableSmsTwoFactor") && setup.Body.Contains("data-phone-verification"), role + " can start SMS 2FA setup with its own password");
        var sent = Messages(user.Phone);
        Check(sent.Count == 1 && !setup.Body.Contains(sent.Code), role + " setup sends a captured code without exposing it in HTML");
        var bad = await Post(browser, "EnableSmsTwoFactor", new() { { "Code", Wrong(sent.Code) } }, "EnableSmsTwoFactor");
        Check(!bad.Body.Contains("<li><code>"), role + " cannot enable SMS 2FA with an incorrect code");
        var enabled = await Post(browser, "EnableSmsTwoFactor", new() { { "Code", sent.Code } }, "EnableSmsTwoFactor"); var recovery = Codes(enabled.Body);
        Check(recovery.Length == 10, role + " receives ten single-use recovery codes after SMS confirmation");
        var blockRemove = await Post(browser, "RemovePhone", new() { { "CurrentPassword", password } }, "RemovePhone");
        Check(blockRemove.Body.Contains("Turn off SMS two-factor"), role + " cannot remove its active SMS second factor");
        clock.Advance(TimeSpan.FromSeconds(61));
        var login = Client(); var challenge = await Login(login, user.Phone);
        Check(challenge.Path.Contains("LoginSms") && (await login.GetAsync(account + "Profile")).StatusCode == HttpStatusCode.Redirect, role + " password alone cannot complete SMS-protected login");
        var loginCode = Messages(user.Phone).Code;
        var bypass = await Post(login, "LoginTwoFactor", new() { { "Code", loginCode } }, "LoginSms");
        Check(bypass.Path.Contains("LoginSms"), role + " SMS mode cannot bypass verification through the authenticator endpoint");
        var wrong = await Post(login, "LoginSms", new() { { "Code", Wrong(loginCode) } }, "LoginSms");
        Check(WebUtility.HtmlDecode(wrong.Body).Contains("That code isn't correct"), role + " rejects incorrect SMS login codes");
        if (role == AccountRoles.Student)
        {
            var early = await Post(login, "ResendLoginSms", new() { { "PhoneNumber", plain.Phone } }, "LoginSms");
            Check(early.Body.Contains("Wait one minute") && Messages(user.Phone).Count == 2, "SMS login resend enforces the shared one-minute cooldown");
            clock.Advance(TimeSpan.FromSeconds(61));
            var resent = await Post(login, "ResendLoginSms", new() { { "PhoneNumber", plain.Phone } }, "LoginSms");
            Check(Messages(user.Phone).Count == 3 && Messages(plain.Phone).Count == 0, "SMS login resend ignores a forged recipient");
            loginCode = Messages(user.Phone).Code;
        }
        var complete = await Post(login, "LoginSms", new() { { "Code", loginCode }, { "ReturnUrl", "https://example.com" } }, "LoginSms");
        Check(!complete.Path.Contains("LoginSms") && (await login.GetAsync(account + "Profile")).StatusCode == HttpStatusCode.OK, role + " completes login with its password and SMS code without open redirects");
        var outsider = Client();
        Check((await Post(outsider, "LoginSms", new() { { "Code", loginCode } }, "Login")).Path.EndsWith("Login"), role + " SMS code alone cannot sign another browser in");
        clock.Advance(TimeSpan.FromSeconds(61));
        var fallback = Client(); await Login(fallback, user.Email);
        Check(!(await Post(fallback, "LoginRecoveryCode", new() { { "Code", recovery[0] } }, "LoginSms")).Path.Contains("LoginRecoveryCode"), role + " can use a saved recovery code instead of SMS");
        var managed = await Post(browser, "ManageTwoFactor", new() { { "CurrentPassword", password }, { "Code", recovery[1] }, { "operation", "disable" } });
        Check(managed.Body.Contains(role == AccountRoles.Admin ? "must keep two-factor authentication enabled" : "Two-factor authentication has been turned off"), role + " enforces the role-specific SMS MFA disable policy");
    }

    var reset = await Fixture("reset");
    using (var scope = factory.Services.CreateScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var user = (await users.FindByIdAsync(reset.Id))!;
        user.PhoneNumber = null; user.PhoneNumberConfirmed = false; await users.UpdateAsync(user);
    }
    var oldSession = Client(); await Login(oldSession, reset.Email);
    var unknownClient = Client(); var unknownEmail = prefix + ".unknown@hackacad.local";
    var unknown = await Post(unknownClient, "ForgotPassword", new() { { "Email", unknownEmail } }, "ForgotPassword");
    Check(unknown.Body.Contains("If the email belongs to an eligible account") && ResetLinks(unknownEmail).Length == 0, "unknown email recovery reveals no account details and sends no message");
    var recovering = Client(); var requested = await Post(recovering, "ForgotPassword", new() { { "Email", "  " + reset.Email + "  " }, { "PhoneNumber", plain.Phone } }, "ForgotPassword");
    Check(requested.Body.Contains("If the email belongs to an eligible account") && !requested.Body.Contains(reset.Email) && ResetLinks(reset.Email).Length == 1, "an account without a phone receives a reset link at its trimmed email");
    Check(Messages(reset.Phone).Count == 0, "forgot password never sends an SMS");
    await Post(recovering, "ForgotPassword", new() { { "Email", reset.Email } }, "ForgotPassword");
    Check(ResetLinks(reset.Email).Length == 1, "email reset requests enforce the one-minute delivery cooldown");
    Check((await Follow(recovering, await recovering.GetAsync(account + "ResetPassword"))).Path.EndsWith("ForgotPassword"), "requesting a reset email does not grant password reset access");
    Check((await Client().GetAsync(account + "VerifyRecoveryPhone")).StatusCode == HttpStatusCode.NotFound && (await Client().PostAsync(account + "ResendRecoverySms", new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode == HttpStatusCode.NotFound, "the old SMS password-recovery endpoints are removed");
    var resetLink = ResetLinks(reset.Email)[0];
    var tampered = await Follow(Client(), await Client().GetAsync(Regex.Replace(resetLink, "code=[^&]+", "code=invalid")));
    Check(tampered.Body.Contains("This reset link isn"), "a malformed reset token is rejected");
    Check((await Follow(Client(), await Client().GetAsync(Regex.Replace(resetLink, "userId=[^&]+", "userId=" + plain.Id)))).Body.Contains("This reset link isn"), "reset links cannot be redirected to another account");
    Check((await Follow(Client(), await Client().GetAsync(Regex.Replace(resetLink, "email=[^&]+", "email=" + Uri.EscapeDataString(plain.Email))))).Body.Contains("This reset link isn"), "reset links are checked against the account's current email");
    using (var scope = factory.Services.CreateScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var user = (await users.FindByIdAsync(reset.Id))!;
        var emailCode = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GenerateEmailConfirmationTokenAsync(user)));
        Check((await Follow(Client(), await Client().GetAsync(Regex.Replace(resetLink, "code=[^&]+", "code=" + emailCode)))).Body.Contains("This reset link isn"), "email confirmation tokens cannot authorize password resets");
        var options = scope.ServiceProvider.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value; var lifetime = options.TokenLifespan;
        try
        {
            options.TokenLifespan = TimeSpan.FromSeconds(-1);
            Check((await Follow(Client(), await Client().GetAsync(resetLink))).Body.Contains("This reset link isn"), "expired reset links are rejected by Identity");
        }
        finally { options.TokenLifespan = lifetime; }
    }
    var verified = await recovering.GetAsync(resetLink);
    var oldGrant = verified.Headers.GetValues("Set-Cookie").First(value => value.StartsWith(".AspNetCore.Mvc.CookieTempDataProvider=")).Split(';')[0];
    var resetPage = await Follow(recovering, verified);
    Check(resetPage.Path == account + "ResetPassword" && !resetPage.Body.Contains("name=\"code\"") && !resetPage.Body.Contains("name=\"userId\""), "a valid email link opens the clean reset page without token or account form fields");
    Check((await recovering.GetAsync(account + "Profile")).StatusCode == HttpStatusCode.Redirect, "opening a reset email does not sign the user in");
    var weak = await Post(recovering, "ResetPassword", new() { { "NewPassword", "weak" }, { "ConfirmPassword", "weak" } }, "ResetPassword");
    Check(weak.Body.Contains("field-validation-error"), "email recovery enforces the normal password policy");
    var changed = await Post(recovering, "ResetPassword", new() { { "NewPassword", newPassword }, { "ConfirmPassword", newPassword }, { "userId", plain.Id } }, "ResetPassword");
    Check(changed.Path.EndsWith("Login") && changed.Body.Contains("Your password has been reset"), "email recovery resets only the protected account and returns to login");
    Check((await oldSession.GetAsync(account + "Profile")).StatusCode == HttpStatusCode.Redirect, "email password reset invalidates existing device sessions");
    Check((await Login(Client(), reset.Email, newPassword)).Path.EndsWith("Dashboard"), "the new password works for an account without any phone number");
    Check((await Login(Client(), reset.Email)).Body.Contains("Unable to sign in"), "the old password stops working after email reset");
    var replay = Client(); replay.DefaultRequestHeaders.Add("Cookie", oldGrant);
    Check((await Follow(replay, await replay.GetAsync(account + "ResetPassword"))).Path.EndsWith("ForgotPassword"), "a used protected reset grant cannot be replayed");
    Check((await Follow(Client(), await Client().GetAsync(resetLink))).Body.Contains("This reset link isn"), "a used reset email cannot be replayed");
    using (var scope = factory.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Check((await db.Users.FindAsync(reset.Id))!.LastPasswordChangedUtc is not null, "email password reset updates Last password changed");
    }

    foreach (var condition in new[] { "disabled-email", "unconfirmed-email", "google-only" })
    {
        var ineligible = await Fixture(condition);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var user = (await users.FindByIdAsync(ineligible.Id))!;
            user.IsDisabled = condition == "disabled-email"; user.EmailConfirmed = condition != "unconfirmed-email";
            if (condition == "google-only") user.PasswordHash = null;
            await users.UpdateAsync(user);
        }
        var result = await Post(Client(), "ForgotPassword", new() { { "Email", ineligible.Email } }, "ForgotPassword");
        Check(result.Body.Contains("If the email belongs to an eligible account") && ResetLinks(ineligible.Email).Length == 0, condition + " recovery uses the same generic response without sending mail");
    }

    var protectedReset = await Fixture("protected-reset", sms: true); var protectedBrowser = Client();
    await Post(protectedBrowser, "ForgotPassword", new() { { "Email", protectedReset.Email } }, "ForgotPassword");
    await Follow(protectedBrowser, await protectedBrowser.GetAsync(ResetLinks(protectedReset.Email)[0]));
    await Post(protectedBrowser, "ResetPassword", new() { { "NewPassword", newPassword }, { "ConfirmPassword", newPassword } }, "ResetPassword");
    var stillProtected = Client(); var needsSms = await Login(stillProtected, protectedReset.Phone, newPassword);
    Check(needsSms.Path.Contains("LoginSms") && (await stillProtected.GetAsync(account + "Profile")).StatusCode == HttpStatusCode.Redirect, "email recovery keeps SMS 2FA enabled and still permits phone login");
    Check(!(await Post(stillProtected, "LoginSms", new() { { "Code", Messages(protectedReset.Phone).Code } }, "LoginSms")).Path.Contains("LoginSms"), "SMS-protected accounts complete their normal second-factor login after email recovery");

    var locked = await Fixture("locked", sms: true); var guessing = Client(); await Login(guessing, locked.Phone);
    for (var attempt = 0; attempt < 5; attempt++) await Post(guessing, "LoginSms", new() { { "Code", Wrong(Messages(locked.Phone).Code) } }, "LoginSms");
    using (var scope = factory.Services.CreateScope())
    {
        var user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(locked.Id))!;
        Check(user.LockoutEnd > DateTimeOffset.UtcNow.AddMinutes(4) && user.LockoutEnd < DateTimeOffset.UtcNow.AddMinutes(6), "five incorrect SMS login guesses lock the account for five minutes");
    }
    Check((await Follow(guessing, await guessing.GetAsync(account + "LoginSms"))).Path.EndsWith("Login"), "a locked account loses its pending SMS login ticket");

    var expiry = await Fixture("expiry");
    using (var scope = factory.Services.CreateScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var user = (await users.FindByIdAsync(expiry.Id))!;
        var phones = scope.ServiceProvider.GetRequiredService<PhoneVerification>();
        Check(phones.IsTestMode, "all verification messages are confined to local test capture");
        await phones.SendAsync(user.Id, user.SecurityStamp!, expiry.Phone, "contact"); var code = Messages(expiry.Phone).Code;
        Check((await phones.CheckAsync(user.Id, user.SecurityStamp!, code, "reset:other")).Number is null, "a contact verification code cannot authorize an unrelated SMS purpose");
        clock.Advance(TimeSpan.FromMinutes(11));
        Check((await phones.CheckAsync(user.Id, user.SecurityStamp!, code, "contact")).Number is null, "SMS challenges expire after ten minutes");
    }
    Console.WriteLine($"All {checks} phone authentication assertions passed. No live SMS or email sent.");
}
finally
{
    using var scope = factory.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    foreach (var user in await users.Users.Where(u => u.Email != null && u.Email.StartsWith(prefix)).ToListAsync()) await users.DeleteAsync(user);
}

sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan amount) => now += amount;
}
