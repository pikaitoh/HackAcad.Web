using System.Net;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HackAcad.Web.Controllers;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
[assembly: WebApplicationFactoryContentRoot("HackAcad.Web", "../../../../../", "HackAcad.Web.csproj", "0")]

if(!(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")??"").Contains("database=hackacad_uiresearch_20261016",StringComparison.OrdinalIgnoreCase)) throw new Exception("Use the isolated test database only.");
Environment.SetEnvironmentVariable("Logging__LogLevel__Default","Error");
await using var factory=new WebApplicationFactory<AccountController>();
const string account="/Identity/Account/",password="HackAcad@123!";
var prefix="roles2fa."+Guid.NewGuid().ToString("N")[..10];var checks=0;
void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
HttpClient Client()=>factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false,BaseAddress=new Uri("https://localhost")});
async Task<(string Path,string Body)> Follow(HttpClient client,HttpResponseMessage response)
{
    var path=response.RequestMessage!.RequestUri!.PathAndQuery;
    for(var i=0;i<10 && response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther;i++)
    {var url=new Uri(client.BaseAddress!,response.Headers.Location!);if(url.Host!="localhost")throw new Exception("External redirect");path=url.PathAndQuery;response=await client.GetAsync(path);}
    if(!response.IsSuccessStatusCode)throw new Exception("Unexpected HTTP "+(int)response.StatusCode+" at "+path);
    return(path,await response.Content.ReadAsStringAsync());
}
async Task<(string Path,string Body)> Post(HttpClient client,string action,Dictionary<string,string> fields,string source="TwoFactor")
{
    var page=await client.GetStringAsync(account+source);
    fields["__RequestVerificationToken"]=WebUtility.HtmlDecode(Regex.Match(page,"name=\"__RequestVerificationToken\"[^>]+value=\"([^\"]+)\"").Groups[1].Value);
    return await Follow(client,await client.PostAsync(account+action,new FormUrlEncodedContent(fields)));
}
Task<(string Path,string Body)> Login(HttpClient client,string email)=>Post(client,"Login",new(){{"Email",email},{"Password",password}},"Login");
string[] Codes(string body)=>Regex.Matches(body,"<li><code>([^<]+)</code></li>").Select(m=>m.Groups[1].Value).ToArray();
string Totp(string key)
{
    var bytes=new List<byte>();int buffer=0,bits=0;
    foreach(var letter in key){buffer=(buffer<<5)|"ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(letter);bits+=5;if(bits>=8){bits-=8;bytes.Add((byte)(buffer>>bits));}}
    var counter=new byte[8];BinaryPrimitives.WriteInt64BigEndian(counter,DateTimeOffset.UtcNow.ToUnixTimeSeconds()/30);
    var hash=HMACSHA1.HashData(bytes.ToArray(),counter);var offset=hash[^1]&15;
    return ((BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset,4))&0x7fffffff)%1000000).ToString("D6");
}
try
{
    Check((await Client().GetAsync(account+"TwoFactor")).StatusCode==HttpStatusCode.Redirect,"anonymous users cannot access 2FA settings");
    foreach(var role in new[]{AccountRoles.Student,AccountRoles.Instructor,AccountRoles.Admin})
    {
        var email=prefix+"."+role.ToLowerInvariant()+"@hackacad.local";
        using(var scope=factory.Services.CreateScope())
        {
            var result=await scope.ServiceProvider.GetRequiredService<UserAccounts>().CreateAsync(new ApplicationUser{Email=email,UserName=email,DisplayName="Role security "+role,EmailConfirmed=true},password,role);
            if(!result.Succeeded)throw new Exception("Fixture creation failed");
        }
        var owner=Client();await Login(owner,email);
        Check((await owner.GetStringAsync(account+"Settings")).Contains(">Two-factor authentication</span>"),role+" sees the shared 2FA navigation");
        Check((await owner.GetAsync(account+"TwoFactor")).StatusCode==HttpStatusCode.OK,role+" can open its own 2FA settings");
        Check((await Follow(owner,await owner.GetAsync(account+"EnableAuthenticator"))).Path.EndsWith("TwoFactor"),role+" cannot access a setup secret before password confirmation");
        var denied=await Post(owner,"BeginAuthenticator",new(){{"CurrentPassword","wrong"}});
        Check(denied.Body.Contains("Your current password is incorrect") && !denied.Body.Contains("data:image/png"),role+" needs its own correct password to start setup");
        var setup=await Post(owner,"BeginAuthenticator",new(){{"CurrentPassword",password}});
        var key=Regex.Match(setup.Body,"<code class=\"authenticator-key\">([^<]+)</code>").Groups[1].Value;
        Check(key.Length>0 && setup.Body.Contains("data:image/png;base64,"),role+" receives a locally rendered QR and its own setup key");
        var invalid=await Post(owner,"EnableAuthenticator",new(){{"Code","invalid"}},"EnableAuthenticator");
        Check(invalid.Body.Contains("field-validation-error"),role+" cannot enable 2FA with an invalid code");
        var enabled=await Post(owner,"EnableAuthenticator",new(){{"Code",Totp(key)}},"EnableAuthenticator");var codes=Codes(enabled.Body);
        Check(codes.Length==10,role+" can enable 2FA and receives ten recovery codes");
        var login=Client();var challenge=await Login(login,email);
        Check(challenge.Path.Contains("LoginTwoFactor") && (await login.GetAsync(account+"Dashboard")).StatusCode==HttpStatusCode.Redirect,role+" password alone cannot create an authenticated session");
        var complete=await Post(login,"LoginTwoFactor",new(){{"Code",Totp(key)}},"LoginTwoFactor");
        Check(!complete.Path.Contains("LoginTwoFactor") && (await login.GetAsync(account+"Profile")).StatusCode==HttpStatusCode.OK,role+" completes sign-in with its Authenticator code");
        var recovery=Client();await Login(recovery,email);
        Check(!(await Post(recovery,"LoginRecoveryCode",new(){{"Code",codes[0]}},"LoginRecoveryCode")).Path.Contains("LoginRecoveryCode"),role+" can sign in with an unused recovery code");
        var replay=Client();await Login(replay,email);
        Check((await Post(replay,"LoginRecoveryCode",new(){{"Code",codes[0]}},"LoginRecoveryCode")).Body.Contains("That code is incorrect"),role+" recovery codes cannot be reused");
        var reset=await Post(owner,"ManageTwoFactor",new(){{"CurrentPassword",password},{"Code",Totp(key)},{"operation","regenerate"}});var newCodes=Codes(reset.Body);
        Check(newCodes.Length==10 && !newCodes.Intersect(codes).Any(),role+" can replace recovery codes");
        var disabled=await Post(owner,"ManageTwoFactor",new(){{"CurrentPassword",password},{"Code",newCodes[0]},{"operation","disable"}});
        Check(disabled.Body.Contains("Two-factor authentication has been turned off"),role+" can disable its own 2FA with password and second-factor proof");
        Check(!(await Login(Client(),email)).Path.Contains("LoginTwoFactor"),role+" returns to normal login after turning off 2FA");
    }
    Console.WriteLine($"All {checks} role-specific 2FA assertions passed. No real email or SMS sent.");
}
finally
{
    using var scope=factory.Services.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    foreach(var user in await users.Users.Where(u=>u.Email!=null&&u.Email.StartsWith(prefix)).ToListAsync())await users.DeleteAsync(user);
}
