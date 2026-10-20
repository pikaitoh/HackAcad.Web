using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HackAcad.Web.Controllers;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
[assembly: WebApplicationFactoryContentRoot("HackAcad.Web", "../../../../../", "HackAcad.Web.csproj", "0")]
if (!(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ?? "").Contains("database=hackacad_uiresearch_20261016",StringComparison.OrdinalIgnoreCase)
    || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Email__DevelopmentPickupDirectory"))) throw new Exception("Use isolated database and local email captures only.");
foreach(var provider in new[]{"GitHub"})
{
    Environment.SetEnvironmentVariable($"Authentication__{provider}__ClientId","test-client");
    Environment.SetEnvironmentVariable($"Authentication__{provider}__ClientSecret","test-secret");
}
// Even stale credentials must not reactivate the removed provider.
Environment.SetEnvironmentVariable("Authentication__Microsoft__ClientId","removed-client");
Environment.SetEnvironmentVariable("Authentication__Microsoft__ClientSecret","removed-secret");
await using var factory = new WebApplicationFactory<AccountController>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
{
    services.PostConfigure<OAuthOptions>("GitHub",o=>o.Backchannel=new HttpClient(new ProviderBackchannel()));
}));
var prefix="externalflow."+Guid.NewGuid().ToString("N")[..9];var checks=0;long subject=DateTime.UtcNow.Ticks;
void Check(bool yes,string message){if(!yes)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
HttpClient Client()=>factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false,BaseAddress=new Uri("https://localhost")});
async Task<(string Path,string Body)> Follow(HttpClient c,HttpResponseMessage r)
{
    var path=r.RequestMessage!.RequestUri!.PathAndQuery;
    for(var i=0;i<10&&r.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther;i++)
    {var url=new Uri(c.BaseAddress!,r.Headers.Location!);if(url.Host!="localhost")throw new Exception("Unexpected external redirect");path=url.PathAndQuery;r=await c.GetAsync(path);}
    if(!r.IsSuccessStatusCode)throw new Exception($"HTTP {(int)r.StatusCode} at {path}");return(path,await r.Content.ReadAsStringAsync());
}
async Task<HttpResponseMessage> Post(HttpClient c,string path,Dictionary<string,string> data,string source="/Identity/Account/Login")
{
    var body=await c.GetStringAsync(source);data["__RequestVerificationToken"]=WebUtility.HtmlDecode(Regex.Match(body,"name=\"__RequestVerificationToken\"[^>]+value=\"([^\"]+)\"").Groups[1].Value);
    return await c.PostAsync(path,new FormUrlEncodedContent(data));
}
async Task<(string Path,string Body)> External(HttpClient c,string provider,string email,string id,bool verified=true,bool connect=false,bool reauth=false,string? returnUrl=null)
{
    var r=await Post(c,"/Identity/Account/ExternalLogin?provider="+provider,new(){{"connect",connect.ToString()},{"reauthenticate",reauth.ToString()},{"returnUrl",returnUrl??""}},connect||reauth?"/Identity/Account/Settings":"/Identity/Account/Login");
    Check(r.StatusCode==HttpStatusCode.Redirect&&r.Headers.Location!.Host=="github.com",provider+" uses the correct authorization server");
    var q=QueryHelpers.ParseQuery(r.Headers.Location!.Query);
    Check(q["code_challenge_method"]=="S256"&&!q.ContainsKey("client_secret"),provider+" challenge uses PKCE and never exposes client secret");
    Check(q["scope"].ToString().Contains("user:email",StringComparison.OrdinalIgnoreCase)&&!q["scope"].ToString().Contains("repo"),provider+" requests only sign-in profile/email access");
    var code=WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new{id,email,verified}));
    return await Follow(c,await c.GetAsync("/signin-"+provider.ToLowerInvariant()+"?state="+Uri.EscapeDataString(q["state"]!)+"&code="+code));
}
try
{
    using var anonymous=Client();
    foreach(var page in new[]{"Login","Register"})
    {
        var body=await anonymous.GetStringAsync("/Identity/Account/"+page);
        Check(body.Contains("Continue with GitHub")&&!body.Contains("Continue with Microsoft")&&body.Contains("Continue with Google"),"Google and GitHub appear without Microsoft on "+page);
    }
    Check((await Follow(anonymous,await Post(anonymous,"/Identity/Account/ExternalLogin?provider=Microsoft",new()))).Path.StartsWith("/Identity/Account/Login"),"Microsoft cannot be challenged even with stale configuration");
    Check((await anonymous.PostAsync("/Identity/Account/ExternalLogin?provider=GitHub",new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode==HttpStatusCode.BadRequest,"external sign-in initiation requires CSRF");
    Check((await Follow(anonymous,await Post(anonymous,"/Identity/Account/ExternalLogin?provider=Unknown",new()))).Path.StartsWith("/Identity/Account/Login"),"unknown authentication scheme cannot be challenged");
    Check((await Follow(anonymous,await anonymous.GetAsync("/Identity/Account/ExternalCallback?provider=GitHub"))).Path.Contains("Login"),"callback without protected cookie cannot sign in");
    foreach(var provider in new[]{"GitHub"})
    {
        var failed=await Follow(Client(),await Client().GetAsync("/signin-"+provider.ToLowerInvariant()+"?error=access_denied&state=invalid"));
        Check(failed.Path.StartsWith("/Identity/Account/Login")&&failed.Body.Contains("External sign-in could not be completed"),provider+" cancellation or invalid state returns a safe login error");
    }
    foreach(var provider in new[]{"GitHub"})
    {
        var id=(subject++).ToString();var email=prefix+"."+provider.ToLowerInvariant()+"@example.edu";
        using var browser=Client();
        var invalid=await External(Client(),provider,email,(subject++).ToString(),verified:false);
        Check(invalid.Path.Contains("Login"),provider+" fails safely without a usable verified provider email");
        var start=await External(browser,provider,email,id);
        Check(start.Body.Contains("Make yourself at home")&&start.Body.Contains(email)&&!start.Body.Contains("name=\"Password\""),provider+" starts passwordless Student onboarding with protected email");
        Check(Regex.IsMatch(start.Body, @"<div class=""form-actions"">\s*<button[^>]*>Create Student account</button>\s*<a[^>]*>Cancel</a>"), "onboarding groups Create and Cancel in the same action row");
        var wrongProvider=await Follow(browser,await Post(browser,"/Identity/Account/ExternalRegister?provider=Google",new(){{"DisplayName","Test learner"}},start.Path));
        Check(wrongProvider.Path.Contains("Login"),"cross-provider cookie substitution is rejected");
        start=await External(browser,provider,email,id);
        var created=await Follow(browser,await Post(browser,"/Identity/Account/ExternalRegister?provider="+provider,new(){{"DisplayName","External learner"},{"Email","forged@example.net"},{"Role","Admin"},{"ReturnUrl","https://evil.example"}},start.Path));
        Check(created.Path.EndsWith("VerificationPending")&&created.Body.Contains(email),provider+" requires HackAcad mailbox verification");
        Check((await browser.GetAsync("/Identity/Account/Dashboard")).StatusCode==HttpStatusCode.Redirect,provider+" cannot access the workspace before mailbox verification");
        string userId;
        using(var scope=factory.Services.CreateScope())
        {
            var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var user=(await users.FindByEmailAsync(email))!;userId=user.Id;
            Check(!user.EmailConfirmed&&!await users.HasPasswordAsync(user)&&await db.Students.AnyAsync(s=>s.UserId==user.Id)&&!await db.Admins.AnyAsync(a=>a.UserId==user.Id),provider+" registration is Student-only and ignores forged email/role");
            Check((await users.FindByLoginAsync(provider,id))?.Id==userId,provider+" binds the immutable provider identity");
            Check(Directory.GetFiles(Environment.GetEnvironmentVariable("Email__DevelopmentPickupDirectory")!,"*.eml").Any(p=>File.ReadAllText(p).Contains(email)),provider+" verification is captured locally");
            var token=await users.GenerateEmailConfirmationTokenAsync(user);Check((await users.ConfirmEmailAsync(user,token)).Succeeded,"fixture confirms the intended mailbox");
        }
        var signed=await External(browser,provider,email,id,returnUrl:"https://evil.example");
        Check(signed.Path.EndsWith("Dashboard"),provider+" signs in only after verification and rejects external return URLs");
        var settings=await browser.GetStringAsync("/Identity/Account/Settings");
        Check(settings.Contains("Connected accounts")&&settings.Contains("Connected")&&!settings.Contains("Connect Microsoft")&&!settings.Contains(">Microsoft</h3>"),provider+" connection is visible in Manage account");
        using(var scope=factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Check(await db.LoginRecords.AnyAsync(x=>x.UserId==userId&&x.Outcome=="Signed in with "+provider)&&await db.AccountSessions.AnyAsync(x=>x.UserId==userId),provider+" sign-in creates normal session and login history records");
        }
        var password=await browser.GetStringAsync("/Identity/Account/ChangePassword");
        Check(password.Contains("Set a password")&&!password.Contains("Confirm with "+provider),provider+" login provides short-lived proof for first password setup");
        await Follow(browser,await Post(browser,"/Identity/Account/Profile",new(){{"DisplayName","External learner"}},"/Identity/Account/Profile"));
        Check((await browser.GetStringAsync("/Identity/Account/ChangePassword")).Contains("Confirm with "+provider),provider+" confirmation is offered when current proof is absent");
        var noProof=await Follow(browser,await Post(browser,"/Identity/Account/ChangePassword",new(){{"NewPassword","NotAllowed@123!"},{"ConfirmPassword","NotAllowed@123!"}},"/Identity/Account/ChangePassword"));
        Check(noProof.Body.Contains("Sign in with "+provider+" again before setting"),provider+" cannot set a first password without fresh external proof");
        var reauth=await External(browser,provider,email,id,reauth:true,returnUrl:"/Identity/Account/ChangePassword");
        Check(reauth.Path.EndsWith("ChangePassword")&&!reauth.Body.Contains("Confirm with "+provider),provider+" can reauthenticate its own linked identity");
        var set=await Follow(browser,await Post(browser,"/Identity/Account/ChangePassword",new(){{"NewPassword","ExternalLocal@123!"},{"ConfirmPassword","ExternalLocal@123!"}},"/Identity/Account/ChangePassword"));
        Check(set.Path.EndsWith("Settings"),provider+" account can set its first local password");
        using(var scope=factory.Services.CreateScope())
        {
            var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var user=(await users.FindByIdAsync(userId))!;
            Check(await users.CheckPasswordAsync(user,"ExternalLocal@123!"),provider+" local password is persisted securely");
            await users.SetLockoutEndDateAsync(user,DateTimeOffset.UtcNow.AddMinutes(5));
        }
        Check((await External(Client(),provider,email,id)).Path.Contains("Login"),provider+" respects account lockout");
        using(var scope=factory.Services.CreateScope())
        {
            var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var user=(await users.FindByIdAsync(userId))!;
            await users.SetLockoutEndDateAsync(user,null);await users.ResetAuthenticatorKeyAsync(user);await users.SetTwoFactorEnabledAsync(user,true);
        }
        using var secondFactor=Client();var pending=await External(secondFactor,provider,email,id);
        Check(pending.Path.Contains("LoginTwoFactor")&&(await secondFactor.GetAsync("/Identity/Account/Profile")).StatusCode==HttpStatusCode.Redirect,provider+" cannot bypass configured 2FA");
        using(var scope=factory.Services.CreateScope())
        {
            var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var user=(await users.FindByIdAsync(userId))!;
            await users.SetTwoFactorEnabledAsync(user,false);user.IsDisabled=true;await users.UpdateAsync(user);
        }
        using var blocked=Client();var blockedResult=await External(blocked,provider,email,id);
        Check(blockedResult.Path.Contains("Login") && blockedResult.Body.Contains("Your HackAcad account has been disabled.") && blockedResult.Body.Contains("Contact support"),provider+" disabled owner sees clear support guidance");
        Check((await blocked.GetAsync("/Identity/Account/Profile")).StatusCode==HttpStatusCode.Redirect,provider+" disabled owner receives no application session");
        Check(!(await External(Client(),provider,email,id,verified:false)).Body.Contains("Your HackAcad account has been disabled."),provider+" invalid verification cannot reveal disabled status");

        var adminEmail=prefix+".admin."+provider.ToLowerInvariant()+"@example.edu";var adminSub=(subject++).ToString();var adminRecovery=new Queue<string>();
        using(var scope=factory.Services.CreateScope())
        {
            var user=new ApplicationUser{Email=adminEmail,UserName=adminEmail,DisplayName="External Admin",EmailConfirmed=true};
            Check((await scope.ServiceProvider.GetRequiredService<UserAccounts>().CreateAsync(user,"HackAcad@123!",AccountRoles.Admin)).Succeeded,"existing Admin fixture created");
            var manager=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();await manager.ResetAuthenticatorKeyAsync(user);await manager.SetTwoFactorEnabledAsync(user,true);adminRecovery=new Queue<string>((await manager.GenerateNewTwoFactorRecoveryCodesAsync(user,10))!);
        }
        var duplicate=await External(Client(),provider,adminEmail,adminSub);
        Check(duplicate.Path.Contains("Login")&&duplicate.Body.Contains("connect "+provider+" in Manage account"),provider+" matching email never silently links an existing Admin");
        using var admin=Client();await Follow(admin,await Post(admin,"/Identity/Account/Login",new(){{"Email",adminEmail},{"Password","HackAcad@123!"}}));
        await Follow(admin,await Post(admin,"/Identity/Account/LoginRecoveryCode",new(){{"Code",adminRecovery.Dequeue()}},"/Identity/Account/LoginRecoveryCode"));
        var linked=await External(admin,provider,adminEmail,adminSub,connect:true);
        Check(linked.Path.EndsWith("Settings")&&linked.Body.Contains("Connected"),provider+" is explicitly linked by the authenticated owner");
        using(var scope=factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var owner=(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(adminEmail))!;
            Check(await db.AdminActivities.CountAsync(a=>a.TargetId==owner.Id&&a.Action==provider+" connected")==1,provider+" connection creates one security activity");
            Check(linked.Body.Contains(adminEmail)&&linked.Body.Contains("GitHubLearner"),provider+" displays the provider email and username snapshot");
        }
        Check(Directory.GetFiles(Environment.GetEnvironmentVariable("Email__DevelopmentPickupDirectory")!,"*.eml").Select(p=>MimeKit.MimeMessage.Load(p)).Any(m=>m.Subject=="HackAcad security: "+provider+" connected"&&m.To.Mailboxes.Any(a=>a.Address==adminEmail)),provider+" connection sends a captured security notice");
        using var returningAdmin=Client();var pendingAdmin=await External(returningAdmin,provider,adminEmail,adminSub);
        Check(pendingAdmin.Path.Contains("LoginTwoFactor"),provider+" linked Admin login retains the mandatory MFA gate");
        Check((await Follow(returningAdmin,await Post(returningAdmin,"/Identity/Account/LoginRecoveryCode",new(){{"Code",adminRecovery.Dequeue()}},"/Identity/Account/LoginRecoveryCode"))).Path=="/Admin",provider+" linked login preserves Admin role after MFA");
        var different=await External(admin,provider,prefix+".other@example.edu",(subject++).ToString(),connect:true);
        Check(different.Path.EndsWith("Settings")&&different.Body.Contains("account not connected. You selected "+prefix+".other@example.edu")&&different.Body.Contains("but your verified HackAcad email is "+adminEmail)&&different.Body.Contains("change and verify your HackAcad email before connecting"),provider+" explains the selected email mismatch and both recovery options");
    }
    foreach(var provider in new[]{"GitHub"})
    {
        Environment.SetEnvironmentVariable($"Authentication__{provider}__ClientId","");
        Environment.SetEnvironmentVariable($"Authentication__{provider}__ClientSecret","");
    }
    await using(var disabledFactory=new WebApplicationFactory<AccountController>())
    {
        using var disabled=disabledFactory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false,BaseAddress=new Uri("https://localhost")});
        var html=await disabled.GetStringAsync("/Identity/Account/Login");
        Check(!html.Contains("Continue with GitHub")&&!html.Contains("Continue with Microsoft"),"unconfigured providers expose no unusable login buttons");
    }
    Console.WriteLine($"All {checks} GitHub checks passed. Mock backchannels; no live OAuth or outbound messages.");
}
finally
{
    using var scope=factory.Services.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    foreach(var user in await users.Users.Where(u=>u.Email!=null&&u.Email.StartsWith(prefix)).ToListAsync()) await users.DeleteAsync(user);
}

sealed class ProviderBackchannel:HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        object payload;
        if(request.Method==HttpMethod.Post)
        {
            var form=QueryHelpers.ParseQuery("?"+await request.Content!.ReadAsStringAsync(token));
            if(form["code_verifier"].ToString().Length<43)throw new Exception("Missing PKCE verifier");
            payload=new{access_token=form["code"].ToString(),token_type="Bearer",expires_in=3600};
        }
        else
        {
            var data=JsonSerializer.Deserialize<JsonElement>(WebEncoders.Base64UrlDecode(request.Headers.Authorization!.Parameter!));
            var email=data.GetProperty("email").GetString();var id=data.GetProperty("id").GetString();
            if(request.RequestUri!.AbsolutePath=="/user/emails") payload=new[]{new{email,primary=true,verified=data.GetProperty("verified").GetBoolean(),visibility="private"}};
            else if(request.RequestUri.AbsolutePath=="/user")payload=new{id=long.Parse(id!),login="GitHubLearner",email="untrusted-public@example.net"};
            else throw new Exception("Unexpected GitHub request");
        }
        return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json")};
    }
}
