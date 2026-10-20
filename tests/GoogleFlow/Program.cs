using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HackAcad.Web.Controllers;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
[assembly: WebApplicationFactoryContentRoot("HackAcad.Web", "../../../../../", "HackAcad.Web.csproj", "0")]

// Dedicated database and a test-only Google HTTP backchannel. No external requests or real OAuth credentials.
if (!(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ?? "").Contains("database=hackacad_uiresearch_20261016", StringComparison.OrdinalIgnoreCase))
    throw new Exception("Use the isolated test database only.");
Environment.SetEnvironmentVariable("Authentication__Google__ClientId", "test-client");
Environment.SetEnvironmentVariable("Authentication__Google__ClientSecret", "test-secret");
Environment.SetEnvironmentVariable("Logging__LogLevel__Default", "Error");
await using var factory = new WebApplicationFactory<AccountController>().WithWebHostBuilder(builder =>
    builder.ConfigureTestServices(services => services.PostConfigure<GoogleOptions>("Google", options =>
        options.Backchannel = new HttpClient(new GoogleBackchannel()))));
HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
var checks=0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS "+message); checks++; }
async Task<(string Path,string Body)> Follow(HttpClient client, HttpResponseMessage response)
{
    string path=response.RequestMessage!.RequestUri!.PathAndQuery;
    for(var i=0; i<10 && response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther; i++)
    {
        var target=new Uri(client.BaseAddress!,response.Headers.Location!);
        if(target.Host!="localhost") throw new Exception("Unexpected external redirect");
        path=target.PathAndQuery; response=await client.GetAsync(path);
    }
    var body=await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) throw new Exception("Unexpected response "+(int)response.StatusCode+" at "+path);
    return(path,body);
}
async Task<HttpResponseMessage> Post(HttpClient client,string path,Dictionary<string,string> data,string source="/Identity/Account/Login")
{
    var body=await client.GetStringAsync(source);
    var token=Regex.Match(body,"name=\"__RequestVerificationToken\"[^>]+value=\"([^\"]+)\"").Groups[1].Value;
    data["__RequestVerificationToken"]=WebUtility.HtmlDecode(token);
    return await client.PostAsync(path,new FormUrlEncodedContent(data));
}
async Task<(string Path,string Body)> Google(HttpClient client,string email,string subject,bool verified=true,string? domain=null,bool connect=false,bool reauth=false,string? returnUrl=null)
{
    var challenge=await Post(client,"/Identity/Account/GoogleLogin",new() { ["connect"]=connect.ToString(),["reauthenticate"]=reauth.ToString(),["returnUrl"]=returnUrl??"" }, connect||reauth?"/Identity/Account/Settings":"/Identity/Account/Login");
    Check(challenge.StatusCode==HttpStatusCode.Redirect && challenge.Headers.Location!.Host=="accounts.google.com", "Google challenge uses Google's authorization endpoint");
    var query=QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
    Check(query["code_challenge_method"]=="S256" && !query.ContainsKey("client_secret"),"OAuth uses PKCE without exposing the client secret");
    var code=WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { sub=subject,email,name="Google Learner",email_verified=verified,hd=domain }));
    return await Follow(client,await client.GetAsync("/signin-google?code="+code+"&state="+Uri.EscapeDataString(query["state"]!)));
}
var suffix=Guid.NewGuid().ToString("N")[..10];
var prefix="googleflow."+suffix;
var email=prefix+"@gmail.com";
var adminEmail=prefix+".admin@gmail.com";
var subject="new-"+suffix;
string? newId=null;
try
{
    var anon=Client();
    Check((await anon.GetStringAsync("/Identity/Account/Login")).Contains("Continue with Google") && (await anon.GetStringAsync("/Identity/Account/Register")).Contains("Continue with Google"),"Google is offered on both login and registration");
    Check((await anon.PostAsync("/Identity/Account/GoogleLogin",new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode==HttpStatusCode.BadRequest,"OAuth initiation requires an anti-forgery token");
    Check((await Follow(anon,await anon.GetAsync("/Identity/Account/GoogleCallback"))).Path.StartsWith("/Identity/Account/Login"),"callback without a protected Google cookie cannot sign in");
    Check((await Follow(Client(),await Client().GetAsync("/signin-google?code=fake&state=tampered"))).Path.StartsWith("/Identity/Account/Login"),"tampered OAuth state is rejected safely");
    var unverified=await Google(Client(),prefix+".unverified@gmail.com","unverified-"+suffix,false);
    Check(unverified.Path.StartsWith("/Identity/Account/Login"),"Google email must be verified before registration");
    var fresh=Client();
    var onboarding=await Google(fresh,email,subject);
    Check(onboarding.Body.Contains("Make yourself at home") && !onboarding.Body.Contains("name=\"Password\""),"new Google identity completes registration with a username and no forced password");
    var malformed=await Follow(fresh,await Post(fresh,"/Identity/Account/GoogleRegister",new(){{"DisplayName","!"}},onboarding.Path));
    Check(malformed.Body.Contains("field-validation-error"),"Google registration enforces the shared username rules");
    var created=await Follow(fresh,await Post(fresh,"/Identity/Account/GoogleRegister",new(){{"DisplayName","Google student"},{"Email",adminEmail},{"Role","Admin"},{"ReturnUrl","https://evil.example/"}},onboarding.Path));
    Check(created.Path=="/Identity/Account/Dashboard","registration rejects external return URLs");
    using(var scope=factory.Services.CreateScope())
    {
        var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user=(await users.FindByEmailAsync(email))!;newId=user.Id;
        Check(user.EmailConfirmed && !await users.HasPasswordAsync(user) && await db.Students.AnyAsync(s=>s.UserId==user.Id) && !await db.Admins.AnyAsync(s=>s.UserId==user.Id),"Google email comes from its ticket and public registration is Student-only");
        Check(await db.LoginRecords.AnyAsync(r=>r.UserId==user.Id&&r.Outcome=="Signed in with Google") && await db.AccountSessions.AnyAsync(r=>r.UserId==user.Id),"Google login creates the normal session and login record");
        var existing=new ApplicationUser{UserName=adminEmail,Email=adminEmail,DisplayName="Google test admin",EmailConfirmed=true};
        Check((await scope.ServiceProvider.GetRequiredService<UserAccounts>().CreateAsync(existing,"HackAcad@123!","Admin")).Succeeded,"existing-account fixture is created");
    }
    using(var scope=factory.Services.CreateScope())
    {
        var user=await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(newId!);
        var principal=await scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>().CreateAsync(user!);
        var session=await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AccountSessions.Where(x=>x.UserId==newId).OrderByDescending(x=>x.CreatedUtc).FirstAsync();
        ((ClaimsIdentity)principal.Identity!).AddClaims([new Claim(AccountSessions.ClaimName,session.Id),new Claim(AccountSessions.GoogleTimeClaim,DateTimeOffset.UtcNow.AddMinutes(-6).ToUnixTimeSeconds().ToString())]);
        var options=factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var cookie=options.TicketDataFormat.Protect(new AuthenticationTicket(principal,new AuthenticationProperties {ExpiresUtc=DateTimeOffset.UtcNow.AddMinutes(10)},IdentityConstants.ApplicationScheme));
        var expired=Client();expired.DefaultRequestHeaders.Add("Cookie",options.Cookie.Name+"="+cookie);
        var rejected=await Follow(expired,await Post(expired,"/Identity/Account/ChangePassword",new(){{"NewPassword","Rejected@123!"},{"ConfirmPassword","Rejected@123!"}},"/Identity/Account/ChangePassword"));
        Check(rejected.Body.Contains("Sign in with Google again before setting"),"expired Google proof cannot set a password or renew its five-minute window");
        Check(!await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().HasPasswordAsync(user!),"rejected first-password request leaves the account passwordless");
    }
    await Follow(fresh,await Post(fresh,"/Identity/Account/Profile",new(){{"DisplayName","Google student"}},"/Identity/Account/Profile"));
    Check((await fresh.GetStringAsync("/Identity/Account/ChangePassword")).Contains("Confirm with Google"),"passwordless account can request Google confirmation when proof is no longer present");
    var reconfirmed=await Google(fresh,email,subject,reauth:true,returnUrl:"/Identity/Account/ChangePassword");
    Check(reconfirmed.Path=="/Identity/Account/ChangePassword" && !reconfirmed.Body.Contains("Confirm with Google"),"Google reauthentication restores proof only for the same linked account");
    var passwordPage=await fresh.GetStringAsync("/Identity/Account/ChangePassword");
    Check(passwordPage.Contains("Set a password") && !passwordPage.Contains("name=\"CurrentPassword\""),"Google-only account can set a first password without inventing a current password");
    var set=await Follow(fresh,await Post(fresh,"/Identity/Account/ChangePassword",new(){{"NewPassword","GoogleLocal@123!"},{"ConfirmPassword","GoogleLocal@123!"}},"/Identity/Account/ChangePassword"));
    Check(set.Path=="/Identity/Account/Settings","fresh Google proof can set a local password");
    using(var scope=factory.Services.CreateScope())
    {
        var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var user=(await users.FindByIdAsync(newId))!;
        Check(await users.CheckPasswordAsync(user,"GoogleLocal@123!") && user.LastPasswordChangedUtc.HasValue,"first password and change timestamp persist together");
    }
    var duplicate=await Google(Client(),adminEmail,"admin-"+suffix);
    Check(duplicate.Path.StartsWith("/Identity/Account/Login") && duplicate.Body.Contains("connect Google in Manage account"),"same email never silently links an existing Admin account");
    var admin=Client();await Follow(admin,await Post(admin,"/Identity/Account/Login",new(){{"Email",adminEmail},{"Password","HackAcad@123!"}}));
    var connected=await Google(admin,adminEmail,"admin-"+suffix,connect:true);
    Check(connected.Path=="/Identity/Account/Settings" && connected.Body.Contains("Connected"),"signed-in account can explicitly connect matching Google email");
    var adminGoogle=await Google(Client(),adminEmail,"admin-"+suffix);
    Check(adminGoogle.Path=="/Admin","linked Google login preserves the existing Admin role");
    using(var scope=factory.Services.CreateScope())
    {
        var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await users.SetLockoutEndDateAsync((await users.FindByEmailAsync(adminEmail))!,DateTimeOffset.UtcNow.AddMinutes(5));
    }
    Check((await Google(Client(),adminEmail,"admin-"+suffix)).Path.StartsWith("/Identity/Account/Login"),"Google cannot bypass an account lockout");
    using(var scope=factory.Services.CreateScope())
    {
        var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await users.SetLockoutEndDateAsync((await users.FindByEmailAsync(adminEmail))!,null);
    }
    var other=await Google(admin,prefix+".other@gmail.com","other-"+suffix,connect:true);
    Check(other.Body.Contains("Google account not connected. You selected "+prefix+".other@gmail.com") && other.Body.Contains("but your verified HackAcad email is "+adminEmail) && other.Body.Contains("change and verify your HackAcad email before connecting"), "Google mismatch names both emails and explains recovery");
    using(var scope=factory.Services.CreateScope())
    {
        var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Check(await users.FindByLoginAsync("Google","other-"+suffix) is null,"a different Google email cannot be connected by a forged callback");
        var existing=(await users.FindByEmailAsync(adminEmail))!;
        await users.ResetAuthenticatorKeyAsync(existing);await users.SetTwoFactorEnabledAsync(existing,true);
    }
    var secondFactorClient=Client();var twoFactor=await Google(secondFactorClient,adminEmail,"admin-"+suffix);
    Check(twoFactor.Path.StartsWith("/Identity/Account/LoginTwoFactor") && (await secondFactorClient.GetAsync("/Admin")).StatusCode==HttpStatusCode.Redirect,"Google cannot bypass Admin two-factor authentication");

    string googleSmsPhone = "+1555" + Random.Shared.Next(1000000,9999999);
    using(var scope=factory.Services.CreateScope())
    {
        Check(scope.ServiceProvider.GetRequiredService<PhoneVerification>().IsTestMode,"Google SMS 2FA tests require local capture and never use a live sender");
        var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var user=(await users.FindByEmailAsync(adminEmail))!;
        user.PhoneNumber=googleSmsPhone;user.PhoneNumberConfirmed=true;user.SmsTwoFactorEnabled=true;await users.UpdateAsync(user);
    }
    var googleSmsClient=Client();var googleSms=await Google(googleSmsClient,adminEmail,"admin-"+suffix);
    Check(googleSms.Path.StartsWith("/Identity/Account/LoginSms") && (await googleSmsClient.GetAsync("/Admin")).StatusCode==HttpStatusCode.Redirect,"linked Google sign-in requires the configured SMS second factor");
    var googleSmsCode=Directory.GetFiles(Environment.GetEnvironmentVariable("Sms__DevelopmentPickupDirectory")!,"*.json")
        .Select(path=>JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone()).Single(value=>value.GetProperty("number").GetString()==googleSmsPhone).GetProperty("code").GetString()!;
    await Follow(googleSmsClient,await Post(googleSmsClient,"/Identity/Account/LoginSms",new(){{"Code",googleSmsCode}},"/Identity/Account/LoginSms"));
    Check((await googleSmsClient.GetAsync("/Identity/Account/Profile")).StatusCode==HttpStatusCode.OK,"linked Google sign-in completes only after the correct SMS code");
    using(var scope=factory.Services.CreateScope())
    {
        var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var user=(await users.FindByIdAsync(newId))!;
        user.IsDisabled=true;await users.UpdateAsync(user);
    }
    Check((await Google(Client(),email,subject)).Path.StartsWith("/Identity/Account/Login"),"disabled account cannot sign in with Google");
    var mailbox=Client();var thirdEmail=prefix+".external@example.org";
    var third=await Google(mailbox,thirdEmail,"third-"+suffix);
    var thirdResult=await Follow(mailbox,await Post(mailbox,"/Identity/Account/GoogleRegister",new(){{"DisplayName","External mailbox"}},third.Path));
    Check(thirdResult.Path=="/Identity/Account/VerificationPending","third-party Google email still requires HackAcad mailbox verification");
    Check(thirdResult.Body.Contains(thirdEmail) && !thirdResult.Body.Contains("name=\"Email\""), "Google third-party mailbox retains its recipient without an email form");
    var ordinary=Client();var ordinaryEmail=prefix+".registration@example.org";
    var registration=await Follow(ordinary,await Post(ordinary,"/Identity/Account/Register",new(){{"Email",ordinaryEmail},{"DisplayName","Pending learner"},{"Password","HackAcad@123!"},{"ConfirmPassword","HackAcad@123!"}}));
    Check(registration.Path.EndsWith("VerificationPending") && registration.Body.Contains(ordinaryEmail) && !registration.Body.Contains("name=\"Email\""), "ordinary registration has a direct resend button and the saved email");
    int Mails(string recipient) => Directory.GetFiles(Environment.GetEnvironmentVariable("Email__DevelopmentPickupDirectory")!,"*.eml").Count(p=>File.ReadAllText(p).Contains(recipient));
    var initialCount=Mails(ordinaryEmail);
    var immediate=await Follow(ordinary,await Post(ordinary,"/Identity/Account/ResendVerification",new(),"/Identity/Account/VerificationPending"));
    Check(immediate.Path.EndsWith("VerificationPending") && Mails(ordinaryEmail)==initialCount,"immediate resend preserves the one-minute email cooldown");
    using(var scope=factory.Services.CreateScope())
    {
        var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var pendingUser=(await users.FindByEmailAsync(ordinaryEmail))!;
        scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>().Remove("verification-email:"+pendingUser.Id+":"+ordinaryEmail.ToUpperInvariant());
    }
    var resent=await Follow(ordinary,await Post(ordinary,"/Identity/Account/ResendVerification",new(){{"Email",thirdEmail}},"/Identity/Account/VerificationPending"));
    Check(resent.Path.EndsWith("VerificationPending") && Mails(ordinaryEmail)==initialCount+1 && Mails(thirdEmail)==1,"resend sends only to the protected registration email and ignores an injected recipient");
    Check((await Client().PostAsync("/Identity/Account/ResendVerification",new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode==HttpStatusCode.BadRequest,"direct resend requires an anti-forgery token");
    Check((await Follow(Client(),await Client().GetAsync("/Identity/Account/ResendVerification"))).Path.EndsWith("Login"),"old resend URL cannot display an email lookup form");
    var stranger=Client();
    Check((await Follow(stranger,await Post(stranger,"/Identity/Account/ResendVerification",new(){{"Email",ordinaryEmail}}))).Path.EndsWith("Login") && Mails(ordinaryEmail)==initialCount+1,"a fresh browser cannot select another account as a resend recipient");
    var returning=Client();
    Check((await Follow(returning,await Post(returning,"/Identity/Account/Login",new(){{"Email",ordinaryEmail},{"Password","wrong"}}))).Path.EndsWith("Login"),"wrong password cannot establish pending-verification context");
    var recovery=await Follow(returning,await Post(returning,"/Identity/Account/Login",new(){{"Email",ordinaryEmail},{"Password","HackAcad@123!"}}));
    Check(recovery.Path.EndsWith("VerificationPending") && recovery.Body.Contains(ordinaryEmail) && (await returning.GetAsync("/Identity/Account/Dashboard")).StatusCode==HttpStatusCode.Redirect,"correct password recovers the verification page without granting an application session");
    Check(!(await Client().GetStringAsync("/Identity/Account/Login")).Contains("Resend verification email"),"login no longer offers a resend verification link");
    var lockedPending=Client();
    for(var i=0;i<5;i++) await Follow(lockedPending,await Post(lockedPending,"/Identity/Account/Login",new(){{"Email",ordinaryEmail},{"Password","wrong"}}));
    Check((await Follow(lockedPending,await Post(lockedPending,"/Identity/Account/Login",new(){{"Email",ordinaryEmail},{"Password","HackAcad@123!"}}))).Path.EndsWith("Login"),"verification recovery cannot bypass the five-attempt password lockout");
    Console.WriteLine($"All {checks} Google and verification assertions passed.");
}
finally
{
    using var scope=factory.Services.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    foreach(var user in await users.Users.Where(u=>u.Email!=null&&u.Email.StartsWith(prefix)).ToListAsync()) await users.DeleteAsync(user);
}

sealed class GoogleBackchannel:HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        object payload;
        if(request.Method==HttpMethod.Post)
        {
            var form=QueryHelpers.ParseQuery("?"+await request.Content!.ReadAsStringAsync(token));
            payload=new {access_token=form["code"].ToString(),token_type="Bearer",expires_in=3600};
        }
        else payload=JsonSerializer.Deserialize<JsonElement>(WebEncoders.Base64UrlDecode(request.Headers.Authorization!.Parameter!));
        return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json")};
    }
}
