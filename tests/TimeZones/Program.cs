using System.Net;
using System.Text.RegularExpressions;
using HackAcad.Web.Controllers;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
[assembly: WebApplicationFactoryContentRoot("HackAcad.Web", "../../../../../", "HackAcad.Web.csproj", "0")]

if (!(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ?? "").Contains("database=hackacad_uiresearch_20261016", StringComparison.OrdinalIgnoreCase)
    || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Email__DevelopmentPickupDirectory"))
    || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Sms__DevelopmentPickupDirectory")))
    throw new Exception("Use the isolated database and local mail/SMS captures only.");
var checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0, int second = 0) => new(year, month, day, hour, minute, second, DateTimeKind.Utc);
var display = new UserTimeZone();
Check(display.Id == "Asia/Kuala_Lumpur" && display.Format(Utc(2026, 1, 1, 1, 30)) == "01 Jan 2026, 09:30:00", "default keeps Malaysia time");
display.Use("invalid/zone"); Check(display.Id == UserTimeZone.DefaultId, "invalid stored zone safely falls back");
display.Use("America/New_York");
Check(display.Format(Utc(2026, 1, 1, 1, 30)) == "31 Dec 2025, 20:30:00", "negative offsets correctly cross year and date boundaries");
Check(display.Format(Utc(2026, 7, 1, 1, 30)) == "30 Jun 2026, 21:30:00", "summer daylight saving uses the event's offset");
Check(display.Format(DateTime.SpecifyKind(Utc(2026, 1, 1, 1, 30), DateTimeKind.Unspecified)) == "31 Dec 2025, 20:30:00", "MySQL unspecified timestamps are interpreted as UTC");
Check(display.StartOfDayUtc(new(2026, 3, 9)) - display.StartOfDayUtc(new(2026, 3, 8)) == TimeSpan.FromHours(23), "spring date filter spans exactly 23 hours");
Check(display.StartOfDayUtc(new(2026, 11, 2)) - display.StartOfDayUtc(new(2026, 11, 1)) == TimeSpan.FromHours(25), "autumn date filter spans exactly 25 hours");
var options = UserTimeZone.Options(DateTimeOffset.UtcNow).ToArray();
Check(options.Length > 100 && options.Any(o => o.Value == "UTC") && options.Any(o => o.Value == UserTimeZone.DefaultId)
    && options.All(o => UserTimeZone.IsSupported(o.Value)), "native time zone choices cover worldwide regions and UTC");

await using var factory = new WebApplicationFactory<AccountController>();
const string account = "/Identity/Account/", password = "HackAcad@123!";
var prefix = "timezone." + Guid.NewGuid().ToString("N")[..10];
HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
async Task<string> Body(HttpClient client, string path)
{
    var response = await client.GetAsync(path);
    Check(response.StatusCode == HttpStatusCode.OK, "renders " + path);
    return await response.Content.ReadAsStringAsync();
}
async Task<HttpResponseMessage> Post(HttpClient client, string path, Dictionary<string,string> fields, string? source = null)
{
    var page = await client.GetStringAsync(source ?? path);
    fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]+value=\"([^\"]+)\"").Groups[1].Value);
    return await client.PostAsync(path, new FormUrlEncodedContent(fields));
}
async Task<(string Id,string Email)> Fixture(string label, string role)
{
    using var scope = factory.Services.CreateScope();
    var email = prefix + "." + label + "@hackacad.local";
    var user = new ApplicationUser { Email = email, UserName = email, DisplayName = prefix + " " + label,
        EmailConfirmed = true, JoinedDate = Utc(2026,1,1,1,30), LastPasswordChangedUtc = Utc(2026,1,1,1,30) };
    var result = await scope.ServiceProvider.GetRequiredService<UserAccounts>().CreateAsync(user,password,role);
    if (!result.Succeeded) throw new Exception("Fixture creation failed");
    return (user.Id,email);
}
async Task Login(HttpClient client, string email)
{
    var response = await Post(client,account+"Login",new() {{"Email",email},{"Password",password}});
    Check(response.StatusCode == HttpStatusCode.Redirect,"fixture signs in");
}
try
{
    var anonymous = Client();
    Check((await anonymous.GetAsync(account+"TimeZone")).StatusCode == HttpStatusCode.Redirect,"anonymous visitor cannot access preferences");
    foreach (var role in new[]{AccountRoles.Student,AccountRoles.Instructor,AccountRoles.Admin})
    {
        var fixture = await Fixture(role.ToLowerInvariant(), role); var client = Client(); var secondDevice = Client();
        await Login(client,fixture.Email); await Login(secondDevice,fixture.Email);
        var preference = await Body(client,account+"TimeZone");
        Check(preference.Contains("aria-current=\"page\"") && preference.Contains("Save time zone") && preference.Contains("Asia/Kuala_Lumpur"),role+" can find its time zone settings");
        Check((await client.PostAsync(account+"TimeZone",new FormUrlEncodedContent(new Dictionary<string,string>{{"TimeZoneId","UTC"}}))).StatusCode==HttpStatusCode.BadRequest,"save requires CSRF protection");
        var saved = await Post(client,account+"TimeZone",new(){{"TimeZoneId","America/New_York"},{"Id","another-user"},{"IsDisabled","true"}});
        Check(saved.StatusCode == HttpStatusCode.Redirect,role+" saves a supported zone");
        using(var scope=factory.Services.CreateScope())
        {
            var user=await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(fixture.Id);
            Check(user?.TimeZoneId=="America/New_York" && !user.IsDisabled,"saved preference persists without binding unrelated account fields");
        }
        var profile=await Body(secondDevice,account+"Profile");
        Check(profile.Contains("31 Dec 2025")&&profile.Contains("America/New_York"),"already signed-in second device picks up the saved zone");
        var passwords=await Body(client,account+"ChangePassword");
        Check(passwords.Contains("31 Dec 2025, 20:30:00")&&passwords.Contains("datetime=\"2026-01-01T01:30:00"),"password display changes while semantic datetime remains UTC");
        Check((await Body(client,account+"LoginHistory")).Contains("Times shown in America/New_York"),"personal login history follows viewer zone");
        Check((await Body(client,account+"Sessions")).Contains("Times shown in America/New_York"),"sessions follow viewer zone");
        foreach(var bad in new[]{"invalid/zone","",new string('x',101)})
        {
            var response=await Post(client,account+"TimeZone",new(){{"TimeZoneId",bad}});
            Check(response.StatusCode==HttpStatusCode.OK && (await response.Content.ReadAsStringAsync()).Contains("Choose a time zone"),"invalid zone rejected with inline error");
        }
        using(var scope=factory.Services.CreateScope())
            Check((await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(fixture.Id))?.TimeZoneId=="America/New_York","invalid saves leave preference unchanged");
        if(role!=AccountRoles.Admin) { Check((await client.GetAsync("/Admin/LoginLogs")).StatusCode==HttpStatusCode.Redirect,"time zone access does not grant administration"); continue; }
        var other=await Fixture("other",AccountRoles.Student);
        using(var scope=factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach(var entry in new[]{(Utc(2026,3,8,4,59,59),"boundary-before"),(Utc(2026,3,8,5),"boundary-start"),(Utc(2026,3,9,3,59,59),"boundary-end"),(Utc(2026,3,9,4),"boundary-after")})
                db.LoginRecords.Add(new LoginRecord{UserId=other.Id,OccurredUtc=entry.Item1,Outcome="Signed in",Device=entry.Item2,IpAddress="127.0.0.1"});
            db.AdminActivities.Add(new AdminActivity{ActorUserId=fixture.Id,OccurredUtc=Utc(2026,1,1,1,30),Action="Timezone test",TargetType="User",TargetId=other.Id,TargetName="Test account",Details="Isolated timezone fixture"});
            await db.SaveChangesAsync();
        }
        var logs=await Body(client,$"/Admin/LoginLogs?search={other.Email}&from=2026-03-08&to=2026-03-08");
        Check(logs.Contains("boundary-start")&&logs.Contains("boundary-end")&&!logs.Contains("boundary-before")&&!logs.Contains("boundary-after"),"Admin date filters include exactly the selected local DST day");
        Check(logs.Contains("08 Mar 2026")&&logs.Contains("23:59:59")&&logs.Contains("America/New_York"),"Admin logs format the same local day used for filtering");
        Check((await Body(client,"/Admin/AuditLog?mine=true&search=Timezone%20test")).Contains("31 Dec 2025, 20:30:00"),"Admin activity uses viewer time");
        Check((await Body(client,"/Admin/Users?search="+other.Email)).Contains("31 Dec 2025"),"Admin user list uses Admin preference for another user's Joined date");
        var edit=await Body(client,"/Admin/UserDetails/"+other.Id);
        Check(edit.Contains("31 Dec 2025, 20:30:00")&&edit.Contains("America/New_York"),"Admin read-only details page uses Admin preference, not target user's zone");
        Check((await Body(client,"/Admin/LoginLogs?from=2026-03-10&to=2026-03-08")).Contains("Choose an end date"),"reversed date range remains an inline validation error");
        Check((await Body(client,"/Admin/LoginLogs?to=9999-12-31")).Contains("Choose a valid end date"),"extreme end date cannot overflow into a server error");
        using(var scope=factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Check(await db.LoginRecords.AnyAsync(r=>r.UserId==other.Id&&r.OccurredUtc==new DateTime(2026,3,8,5,0,0,DateTimeKind.Utc)),"display and filters never rewrite stored UTC events");
            Check((await db.Users.FindAsync(other.Id))?.TimeZoneId==UserTimeZone.DefaultId,"one user's timezone never changes another account's preference");
        }
        var switchUtc=await Post(client,account+"TimeZone",new(){{"TimeZoneId","UTC"}});
        Check(switchUtc.StatusCode==HttpStatusCode.Redirect&&(await Body(client,"/Admin/LoginLogs?search="+other.Email+"&from=2026-03-08&to=2026-03-08")).Contains("boundary-before"),"switching to UTC immediately changes date-filter boundaries");
    }
    Console.WriteLine($"All {checks} timezone assertions passed. No live messages sent.");
}
finally
{
    using var scope=factory.Services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var ids=await db.Users.Where(u=>u.Email!=null&&u.Email.StartsWith(prefix)).Select(u=>u.Id).ToListAsync();
    await db.AdminActivities.Where(a=>a.ActorUserId!=null&&ids.Contains(a.ActorUserId)).ExecuteDeleteAsync();
    foreach(var id in ids) { var user=await users.FindByIdAsync(id); if(user is not null) await users.DeleteAsync(user); }
}
