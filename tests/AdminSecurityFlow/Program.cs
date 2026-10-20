using System.Net;
using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text.RegularExpressions;
using HackAcad.Web.Controllers;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using MimeKit;

[assembly: WebApplicationFactoryContentRoot("HackAcad.Web", "../../../../../", "HackAcad.Web.csproj", "0")]
if (!(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ?? "").Contains("database=hackacad_uiresearch_20261016", StringComparison.OrdinalIgnoreCase)
    || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Email__DevelopmentPickupDirectory"))) throw new Exception("Use an isolated database and mail pickup only.");
var clock = new TestClock();
await using var factory = new WebApplicationFactory<AccountController>().WithWebHostBuilder(b => b.ConfigureTestServices(s => {
    s.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)); s.AddSingleton<IStartupFilter, TestClientAddress>();
}));
const string account="/Identity/Account/", password="HackAcad@123!";
var prefix="adminsecurity."+Guid.NewGuid().ToString("N")[..8]; int checks=0, address=0;
var codes = new Dictionary<string,Queue<string>>();
void Check(bool ok,string name) { if(!ok) throw new Exception(name);checks++;Console.WriteLine("PASS "+name); }
HttpClient Client() { var c=factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect=false, BaseAddress=new Uri("https://localhost") });c.DefaultRequestHeaders.Add("X-Test-Address","198.51.100."+(++address));return c; }
string Hidden(string html,string name) => WebUtility.HtmlDecode(Regex.Match(html,"name=\""+name+"\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
async Task<string> Html(HttpClient c,string path) {var r=await c.GetAsync(path);if(!r.IsSuccessStatusCode)throw new Exception($"GET {path}: {r.StatusCode} {r.Headers.Location}");return await r.Content.ReadAsStringAsync();}
async Task<HttpResponseMessage> Post(HttpClient c,string path,Dictionary<string,string> data,string? source=null) { data["__RequestVerificationToken"]=Hidden(await Html(c,source??path),"__RequestVerificationToken");return await c.PostAsync(path,new FormUrlEncodedContent(data)); }
async Task<(string Path,string Body)> Follow(HttpClient c,HttpResponseMessage r) {
 var path=r.RequestMessage!.RequestUri!.PathAndQuery;
 for(int i=0;i<12 && r.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther;i++) {var u=new Uri(c.BaseAddress!,r.Headers.Location!);if(u.Host!="localhost")throw new Exception("External redirect");path=u.PathAndQuery;r=await c.GetAsync(path);}
 if(!r.IsSuccessStatusCode)throw new Exception($"{path}: {r.StatusCode} "+(await r.Content.ReadAsStringAsync())[..Math.Min(300,(await r.Content.ReadAsStringAsync()).Length)]);
 return(path,await r.Content.ReadAsStringAsync());
}
string Totp(string key) {var bytes=new List<byte>();int buffer=0,bits=0;foreach(var letter in key){buffer=(buffer<<5)|"ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(letter);bits+=5;if(bits>=8){bits-=8;bytes.Add((byte)(buffer>>bits));}}var counter=new byte[8];BinaryPrimitives.WriteInt64BigEndian(counter,DateTimeOffset.UtcNow.ToUnixTimeSeconds()/30);var hash=HMACSHA1.HashData(bytes.ToArray(),counter);return((BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(hash[^1]&15,4))&0x7fffffff)%1000000).ToString("D6");}
async Task<Fixture> UserFixture(string name,string role=AccountRoles.Student,bool mfa=false,bool disabled=false) {
 using var s=factory.Services.CreateScope();var u=s.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
 var user=new ApplicationUser{Email=prefix+"."+name+"@example.edu",UserName=prefix+"."+name+"@example.edu",DisplayName="Security "+name,EmailConfirmed=true};
 if(!(await s.ServiceProvider.GetRequiredService<UserAccounts>().CreateAsync(user,password,role)).Succeeded)throw new Exception("Fixture failed");
 var key="";if(mfa){await u.ResetAuthenticatorKeyAsync(user);key=(await u.GetAuthenticatorKeyAsync(user))!;await u.SetTwoFactorEnabledAsync(user,true);codes[user.Id]=new Queue<string>((await u.GenerateNewTwoFactorRecoveryCodesAsync(user,40))!);}
 if(disabled){user.IsDisabled=true;await u.UpdateAsync(user);}return new(user.Id,user.Email!,key);
}
async Task<(string Path,string Body)> Login(HttpClient c,Fixture user) {
 var r=await Post(c,account+"Login",new(){{"Email",user.Email},{"Password",password}});
 var x=await Follow(c,r);if(x.Path.Contains("LoginTwoFactor")) x=await Follow(c,await Post(c,account+"LoginTwoFactor",new(){{"Code",Totp(user.Key)}}));return x;
}
async Task Verify(HttpClient c,Fixture user,string targetPage,int index=0) {
 var page=await Html(c,targetPage);
 var links=Regex.Matches(page,"href=\"([^\"]*Admin/Verify\\?ticket=[^\"]+)\"").Select(m=>WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();
 if(links.Count<=index)throw new Exception("No verification link at "+targetPage);
 var form=await Html(c,links[index]);var ticket=Hidden(form,"Ticket");
 var r=await Follow(c,await Post(c,"/Admin/Verify",new(){{"Ticket",ticket},{"CurrentPassword",password},{"Code",codes[user.Id].Dequeue()}},links[index]));
 Check(r.Body.Contains("Identity verified"),"scoped identity verification succeeds for "+targetPage);
}
IEnumerable<MimeMessage> Mails()=>Directory.GetFiles(Environment.GetEnvironmentVariable("Email__DevelopmentPickupDirectory")!,"*.eml").Select(path=>MimeMessage.Load(path));
string SupportCode(Fixture user)=>Regex.Matches(Mails().Last(m=>m.Subject=="HackAcad support: Verify your support access" && m.To.Mailboxes.Any(a=>a.Address==user.Email)).TextBody??"",@"Support/Verify\?code=([A-F0-9]{64})").Single().Groups[1].Value;
async Task OpenSupport(HttpClient c,Fixture user) { await Follow(c,await Post(c,"/Support",new(){{"Email",user.Email}}));var code=SupportCode(user);await Follow(c,await Post(c,"/Support/Verify",new(){{"code",code},{"confirm","true"}},"/Support/Verify?code="+code)); }
async Task<SupportTicket> Ticket(Fixture user) {using var s=factory.Services.CreateScope();return await s.ServiceProvider.GetRequiredService<ApplicationDbContext>().SupportTickets.AsNoTracking().SingleAsync(t=>t.UserId==user.Id);}
async Task<int> Audit(string action,string target) {using var s=factory.Services.CreateScope();return await s.ServiceProvider.GetRequiredService<ApplicationDbContext>().AdminActivities.CountAsync(a=>a.Action==action && a.TargetId==target);}
try {
 var administrator=await UserFixture("manager",AccountRoles.Admin,true);var disabled=await UserFixture("disabled",disabled:true);var other=await UserFixture("other");
 var setup=await UserFixture("setup",AccountRoles.Admin);using var setupClient=Client();var setupResult=await Login(setupClient,setup);
 Check(setupResult.Path.Contains("TwoFactor"),"administrator without MFA is directed to setup");
 foreach(var path in new[]{"/Admin/Users","/Admin/Security",account+"Dashboard",account+"Settings"}) Check((await setupClient.GetAsync(path)).Headers.Location?.ToString().Contains("TwoFactor")==true,"MFA setup cannot be bypassed at "+path);
 using var admin=Client();Check((await Login(admin,administrator)).Path=="/Admin","MFA login marks the new admin session");
 using var regular=Client();await Login(regular,other);Check((await regular.GetAsync("/Admin/Security")).StatusCode==HttpStatusCode.Redirect,"student cannot enter administration");
 Check((await admin.GetAsync("/Admin/Security")).IsSuccessStatusCode,"verified administrator can inspect security overview");
 var target="/Admin/UserDetails/"+other.Id;
 Check((await admin.PostAsync("/Admin/AccountStatus/"+other.Id,new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode==HttpStatusCode.BadRequest,"admin mutations require CSRF before step-up");
 var denied=await Post(admin,"/Admin/AccountStatus/"+other.Id,new(){{"Disable","true"},{"Reason","A controlled security test."}},target);
 Check(denied.Headers.Location?.ToString().Contains("/Admin/Verify")==true && await Audit("Disabled account",other.Id)==0,"direct admin mutation has no effect without scoped verification");
 var proofPage=await Html(admin,denied.Headers.Location!.ToString());var proof=Hidden(proofPage,"Ticket");
 var wrong=await Follow(admin,await Post(admin,"/Admin/Verify",new(){{"Ticket",proof},{"CurrentPassword","wrong"},{"Code",codes[administrator.Id].Peek()}},denied.Headers.Location.ToString()));
 Check(wrong.Body.Contains("current HackAcad password"),"wrong password cannot grant a sensitive action");
 using var secondAdmin=Client();await Login(secondAdmin,administrator);
 var foreign=await Post(secondAdmin,"/Admin/Verify",new(){{"Ticket",proof},{"CurrentPassword",password},{"Code",codes[administrator.Id].Peek()}},"/Admin/Verify");
 Check(foreign.StatusCode==HttpStatusCode.BadRequest,"verification tickets cannot cross administrator sessions");
 await Verify(admin,administrator,target);
 var wrongTarget=await Post(admin,"/Admin/AccountStatus/"+disabled.Id,new(){{"Disable","false"},{"Reason","Review a different account."}},"/Admin/UserDetails/"+disabled.Id);
 Check(wrongTarget.Headers.Location?.ToString().Contains("/Admin/Verify")==true,"verification for one user cannot change another user");
 await Follow(admin,await Post(admin,"/Admin/AccountStatus/"+other.Id,new(){{"Disable","true"},{"Reason","Investigating the reported access issue."}},target));
 Check(await Audit("Disabled account",other.Id)==1,"disabled account mutation creates one atomic audit entry");
 Check((await regular.GetAsync(account+"Settings")).StatusCode==HttpStatusCode.Redirect,"disabling an account revokes the existing browser immediately");
 using var blocked=Client();var blockedPage=await Login(blocked,other);
 Check(blockedPage.Body.Contains("Contact support") && blockedPage.Body.Contains("disabled"),"disabled login explains the support path after a correct password");
 using(var s=factory.Services.CreateScope()) {var audit=await s.ServiceProvider.GetRequiredService<ApplicationDbContext>().AdminActivities.SingleAsync(a=>a.Action=="Disabled account" && a.TargetId==other.Id);Check(audit.Before=="Active" && audit.After=="Disabled" && audit.Result=="Succeeded" && audit.IpAddress.StartsWith("198.51.100.") && audit.CorrelationId.Length>0,"audit retains before/after, outcome, source and correlation");}
 using var support=Client();var sent=await Follow(support,await Post(support,"/Support",new(){{"Email",disabled.Email}}));var validCode=SupportCode(disabled);
 using var unknown=Client();var unknownSent=await Follow(unknown,await Post(unknown,"/Support",new(){{"Email",prefix+".missing@example.edu"}}));
 Check(sent.Body==unknownSent.Body || Regex.Replace(sent.Body,"value=\"[^\"]+\"","")==Regex.Replace(unknownSent.Body,"value=\"[^\"]+\"",""),"unknown and existing emails receive the same generic response");
 Check((await support.GetAsync("/Support/Verify?code="+validCode)).IsSuccessStatusCode,"mail link GET does not consume access");
 Check((await support.PostAsync("/Support/Verify",new FormUrlEncodedContent(new Dictionary<string,string>{{"code",validCode},{"confirm","true"}}))).StatusCode==HttpStatusCode.BadRequest,"support access exchange requires CSRF");
 var opened=await Follow(support,await Post(support,"/Support/Verify",new(){{"code",validCode},{"confirm","true"}},"/Support/Verify?code="+validCode));
 Check(opened.Path=="/Support/Requests" && !opened.Body.Contains("Manage account"),"mailbox-verified disabled user receives only the support interface");
 Check((await support.GetAsync(account+"Settings")).StatusCode==HttpStatusCode.Redirect && (await support.GetAsync("/Admin/Security")).StatusCode==HttpStatusCode.Redirect,"support cookie grants neither application nor admin access");
 using var replay=Client();var reused=await Follow(replay,await Post(replay,"/Support/Verify",new(){{"code",validCode},{"confirm","true"}},"/Support/Verify?code="+validCode));Check(reused.Body.Contains("already been used"),"support links are single-use");
 var blank=await Post(support,"/Support/Create",new(){{"Subject","     x     "},{"Body","                  "}});
 using(var s=factory.Services.CreateScope())Check(blank.StatusCode==HttpStatusCode.OK && !await s.ServiceProvider.GetRequiredService<ApplicationDbContext>().SupportTickets.AnyAsync(t=>t.UserId==disabled.Id),"space-padded subject and empty message cannot create a request");
 var created=await Follow(support,await Post(support,"/Support/Create",new(){{"Subject","Why was my account disabled?"},{"Body","Please review my account access. I think this may be a mistake."},{"Internal","true"},{"Status","Resolved"}}));
 var ticket=await Ticket(disabled);var thread="/Support/Thread/"+ticket.Id;var adminThread="/Admin/SupportThread/"+ticket.Id;
 Check(created.Path==thread && ticket.Status=="Awaiting admin","forged user status and internal-note flags cannot close or hide a request");
 var duplicate=await Follow(support,await Post(support,"/Support/Create",new(){{"Subject","Duplicate support request"},{"Body","Please see my existing conversation for details."}}));Check(duplicate.Path==thread && duplicate.Body.Contains("already have an open request"),"duplicate open requests return to the existing conversation");
 using var otherSupport=Client();await OpenSupport(otherSupport,other);
 Check((await otherSupport.GetAsync(thread)).StatusCode==HttpStatusCode.NotFound,"support users cannot read another user's ticket");
 Check((await Post(otherSupport,"/Support/Reply/"+ticket.Id,new(){{"Version",ticket.Version},{"Body","Attempt to reply to someone else's request."}},"/Support/Create")).StatusCode==HttpStatusCode.NotFound,"support users cannot write another user's ticket");
 Check((await Html(admin,"/Admin/SupportRequests")).Contains("Why was my account disabled?"),"new requests appear in the admin support queue");
 Check((await Html(admin,adminThread)).Contains("Internal note only"),"admin conversation distinguishes internal notes from public replies");
 await Verify(admin,administrator,adminThread);
 await Follow(admin,await Post(admin,"/Admin/SupportReply/"+ticket.Id,new(){{"Version",ticket.Version},{"Body","PRIVATE investigation note, do not expose to the user."},{"Internal","true"},{"Status","Awaiting admin"}},adminThread));
 Check(!(await Html(support,thread)).Contains("PRIVATE investigation"),"administrator internal notes are excluded from the user response");
 Check(!Mails().Any(m=>(m.TextBody??"").Contains("PRIVATE investigation")),"internal notes are never emailed to the user");
 var stale=await Post(admin,"/Admin/SupportReply/"+ticket.Id,new(){{"Version",ticket.Version},{"Body","An outdated administrator reply must be rejected."},{"Status","Resolved"}},adminThread);
 Check(stale.StatusCode==HttpStatusCode.Conflict,"stale conversation versions cannot overwrite a newer reply");
 ticket=await Ticket(disabled);
 await Follow(admin,await Post(admin,"/Admin/SupportReply/"+ticket.Id,new(){{"Version",ticket.Version},{"Body","We reviewed the case. Please provide more details about your last successful sign-in."},{"Status","Awaiting user"}},adminThread));
 Check((await Html(support,thread)).Contains("Please provide more details"),"public admin reply appears in the disabled user's conversation");
 Check(Mails().Any(m=>m.Subject==$"HackAcad support: Reply to support request #{ticket.Id}" && m.To.Mailboxes.Any(a=>a.Address==disabled.Email)),"public reply produces an email notification to the account mailbox");
 using(var s=factory.Services.CreateScope()) {var db=s.ServiceProvider.GetRequiredService<ApplicationDbContext>();Check(await db.Users.AnyAsync(u=>u.Id==disabled.Id && u.IsDisabled),"replying does not automatically reactivate an account");Check(await db.UserNotifications.AnyAsync(n=>n.UserId==disabled.Id && n.Title.Contains("support request")),"public reply also records an in-app notification");}
 ticket=await Ticket(disabled);await Follow(support,await Post(support,"/Support/Reply/"+ticket.Id,new(){{"Version",ticket.Version},{"Body","Here are the details you requested for my review."},{"Status","Resolved"},{"Internal","true"}},thread));
 Check((await Ticket(disabled)).Status=="Awaiting admin" && (await Html(admin,adminThread)).Contains("Here are the details"),"user follow-up reopens the admin queue and cannot forge private notes");
 var config=factory.Services.GetRequiredService<IConfiguration>();var capture=config["Email:DevelopmentPickupDirectory"];
 try {config["Email:DevelopmentPickupDirectory"]=Path.Combine(factory.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath,"wwwroot");ticket=await Ticket(disabled);var failed=await Follow(admin,await Post(admin,"/Admin/SupportReply/"+ticket.Id,new(){{"Version",ticket.Version},{"Body","This public reply is retained even when email delivery fails."},{"Status","Resolved"}},adminThread));Check(failed.Body.Contains("email notification could not be sent") && (await Html(support,thread)).Contains("email delivery fails"),"failed notification reports truthfully without losing the saved reply");}finally{config["Email:DevelopmentPickupDirectory"]=capture;}
 var oldTicket=await Ticket(disabled);
 var newRequest=await Follow(support,await Post(support,"/Support/Create",new(){{"Subject","A new account support question"},{"Body","Please help me with a separate account access question."}}));
 var duplicateReopen=await Follow(support,await Post(support,"/Support/Reply/"+oldTicket.Id,new(){{"Version",oldTicket.Version},{"Body","Please reopen this older conversation as well."}},thread));
 Check(duplicateReopen.Path==newRequest.Path && duplicateReopen.Body.Contains("already have an open request"),"reopening an older ticket cannot bypass the one-open-request rule");
 var auditor=await UserFixture("auditor",AccountRoles.Admin,true);using(var s=factory.Services.CreateScope()){var db=s.ServiceProvider.GetRequiredService<ApplicationDbContext>();await db.Admins.Where(a=>a.UserId==auditor.Id).ExecuteUpdateAsync(a=>a.SetProperty(x=>x.IsReadOnly,true));}
 using var reader=Client();await Login(reader,auditor);
 foreach(var path in new[]{"/Admin/Security","/Admin/LoginLogs","/Admin/AuditLog"})Check((await reader.GetAsync(path)).IsSuccessStatusCode,"read-only auditor can inspect "+path);
 foreach(var path in new[]{"/Admin/Users","/Admin/SupportRequests",adminThread,target})Check((await reader.GetAsync(path)).Headers.Location?.ToString().Contains("AccessDenied")==true,"read-only auditor cannot manage "+path);
 Check((await Post(reader,"/Admin/AccountStatus/"+disabled.Id,new(){{"Disable","false"},{"Reason","A forged auditor management request."}},"/Admin/Verify")).Headers.Location?.ToString().Contains("AccessDenied")==true,"read-only mutation denied server-side");
 await Verify(admin,administrator,"/Admin/UserDetails/"+auditor.Id,1);
 await Follow(admin,await Post(admin,"/Admin/AdminAccess/"+auditor.Id,new(){{"ReadOnly","false"},{"Reason","Restore management permissions after this controlled review."}},"/Admin/UserDetails/"+auditor.Id));
 using(var s=factory.Services.CreateScope())Check(await s.ServiceProvider.GetRequiredService<ApplicationDbContext>().Admins.AnyAsync(a=>a.UserId==auditor.Id && !a.IsReadOnly),"verified administrator can update another administrator's access mode");
 Check((await reader.GetAsync("/Admin/Security")).Headers.Location?.ToString().Contains("Login")==true,"administrative permission changes invalidate the target's old session");
 // Actual per-IP login limiter remains active; each test browser otherwise receives its own IP.
 using var throttle=Client();HttpResponseMessage? limited=null;for(var i=0;i<31;i++)limited=await Post(throttle,account+"Login",new(){{"Email",prefix+".nobody@example.edu"},{"Password","Wrong@123!"}});
 Check(limited!.StatusCode==HttpStatusCode.TooManyRequests,"password endpoint enforces the production IP limiter");
 using(var s=factory.Services.CreateScope()) {var db=s.ServiceProvider.GetRequiredService<ApplicationDbContext>();var monitor=s.ServiceProvider.GetRequiredService<SecurityMonitor>();var context=new DefaultHttpContext();context.Connection.RemoteIpAddress=IPAddress.Parse("203.0.113.246");
 for(var i=0;i<6;i++)await monitor.LoginAsync(disabled.Id,disabled.Email,"Failed sign-in",context);
 Check(await db.SecurityAlerts.CountAsync(a=>a.UserId==disabled.Id && a.Title=="Repeated failed sign-ins")==1,"repeated failures raise a deduplicated account alert");
 for(var i=0;i<11;i++)await monitor.LoginAsync(null,prefix+".spray"+i,"Failed sign-in",context);
 Check(await db.SecurityAlerts.CountAsync(a=>a.IpAddress=="203.0.113.246" && a.Title=="One IP tried multiple accounts")==1,"password spraying across identifiers raises one IP alert");
 var alert=await db.SecurityAlerts.AsNoTracking().SingleAsync(a=>a.UserId==disabled.Id && a.Title=="Repeated failed sign-ins");
 await Verify(admin,administrator,"/Admin/Alert/"+alert.Id);
 await Follow(admin,await Post(admin,"/Admin/Alert/"+alert.Id,new(){{"Version",alert.Version},{"Status","Resolved"},{"Notes","Controlled test traffic; reviewed and marked as a false positive."}}));
 await monitor.LoginAsync(disabled.Id,disabled.Email,"Failed sign-in",context);
 Check(await db.SecurityAlerts.CountAsync(a=>a.Id==alert.Id && a.Status=="Resolved")==1,"deduplication preserves a resolved false-positive investigation");
 Check((await Html(admin,"/Admin/Alert/"+alert.Id)).Contains("false positive"),"investigation notes remain visible after resolving an alert");
 Check((await Post(admin,"/Admin/Alert/"+alert.Id,new(){{"Version",alert.Version},{"Status","New"},{"Notes","Attempting to overwrite a newer investigation."}})).StatusCode==HttpStatusCode.Conflict,"alert concurrency tokens reject stale investigation updates");
 }
 var device=await UserFixture("devices",AccountRoles.Admin,true);using var deviceOne=Client();await Login(deviceOne,device);using var deviceTwo=Client();await Login(deviceTwo,device);
 using(var s=factory.Services.CreateScope()){var db=s.ServiceProvider.GetRequiredService<ApplicationDbContext>();Check(await db.SecurityAlerts.AnyAsync(a=>a.UserId==device.Id && a.Title.Contains("new device")),"new admin device/network generates a review signal");}
 using(var s=factory.Services.CreateScope()){var db=s.ServiceProvider.GetRequiredService<ApplicationDbContext>();await db.AccountSessions.Where(x=>x.UserId==device.Id).ExecuteUpdateAsync(x=>x.SetProperty(a=>a.CreatedUtc,clock.GetUtcNow().UtcDateTime.AddHours(-8).AddMinutes(-1)).SetProperty(a=>a.LastSeenUtc,clock.GetUtcNow().UtcDateTime));}
 Check((await deviceTwo.GetAsync("/Admin/Security")).Headers.Location?.ToString().Contains("Login")==true,"absolute eight-hour Admin expiry applies even with recent activity");
 var stampUser=await UserFixture("stamp");using var stampSupport=Client();await OpenSupport(stampSupport,stampUser);
 using(var s=factory.Services.CreateScope()){var u=s.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();await u.UpdateSecurityStampAsync((await u.FindByIdAsync(stampUser.Id))!);}
 Check((await stampSupport.GetAsync("/Support/Requests")).Headers.Location?.ToString().Contains("/Support")==true,"security-stamp changes invalidate support-only access");
 using var victim=Client();await Login(victim,setup); // no MFA required to reach setup, but the cookie is revocable.
 await Verify(admin,administrator,"/Admin/Sessions/"+setup.Id);
 await Follow(admin,await Post(admin,"/Admin/RevokeSessions/"+setup.Id,new(){{"reason","Revoke the controlled test sessions."}},"/Admin/Sessions/"+setup.Id));
 Check((await victim.GetAsync(account+"TwoFactor")).Headers.Location?.ToString().Contains("Login")==true,"admin-revoked session is rejected on its next request");
 var expiry=await UserFixture("expired");using var exp=Client();await Post(exp,"/Support",new(){{"Email",expiry.Email}});var expiryCode=SupportCode(expiry);
 clock.Advance(TimeSpan.FromMinutes(11));var expired=await Follow(exp,await Post(exp,"/Support/Verify",new(){{"code",expiryCode},{"confirm","true"}},"/Support/Verify?code="+expiryCode));Check(expired.Body.Contains("expired"),"support email links expire after ten minutes");
 var expiredGrant=await Post(admin,"/Admin/AccountStatus/"+other.Id,new(){{"Disable","false"},{"Reason","Verify that an expired grant cannot act."}},target);
 Check(expiredGrant.Headers.Location?.ToString().Contains("/Admin/Verify")==true,"sensitive-action grants expire independently of the admin login");
 clock.Advance(TimeSpan.FromMinutes(16));Check((await admin.GetAsync("/Admin/Security")).Headers.Location?.ToString().Contains("Login")==true,"idle administrator session expires after fifteen minutes");
 clock.Advance(TimeSpan.FromMinutes(4));Check((await support.GetAsync(thread)).Headers.Location?.ToString().Contains("/Support")==true,"support-only session expires after thirty minutes");
 Console.WriteLine($"All {checks} Admin security and support checks passed. Isolated database; local email capture only.");
}finally {
 using var s=factory.Services.CreateScope();var db=s.ServiceProvider.GetRequiredService<ApplicationDbContext>();var users=s.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var fixtures=await users.Users.Where(u=>u.Email!=null && u.Email.StartsWith(prefix)).ToListAsync();var ids=fixtures.Select(u=>u.Id).ToList();var tickets=await db.SupportTickets.Where(t=>ids.Contains(t.UserId)).Select(t=>t.Id).ToListAsync();
 await db.SupportMessages.Where(m=>tickets.Contains(m.TicketId)).ExecuteDeleteAsync();await db.SupportTickets.Where(t=>ids.Contains(t.UserId)).ExecuteDeleteAsync();await db.SupportAccesses.Where(a=>ids.Contains(a.UserId)).ExecuteDeleteAsync();
 await db.SecurityAlerts.Where(a=>ids.Contains(a.UserId!) || a.IpAddress=="203.0.113.246" || a.EventKey.StartsWith("spray:") && a.CreatedUtc>=clock.GetUtcNow().UtcDateTime.AddDays(-1)).ExecuteDeleteAsync();
 await db.SecurityEvents.Where(e=>ids.Contains(e.UserId!) || e.IpAddress=="203.0.113.246" || e.UserId==null && e.IpAddress.StartsWith("198.51.100.")).ExecuteDeleteAsync();
 await db.AdminActivities.Where(a=>ids.Contains(a.ActorUserId!) || ids.Contains(a.TargetId)).ExecuteDeleteAsync();foreach(var fixture in fixtures)await users.DeleteAsync(fixture);
}
record Fixture(string Id,string Email,string Key);
sealed class TestClock : TimeProvider {private TimeSpan offset;public override DateTimeOffset GetUtcNow()=>DateTimeOffset.UtcNow+offset;public void Advance(TimeSpan value)=>offset+=value;}
sealed class TestClientAddress : IStartupFilter {public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)=>app=>{app.Use(async(context,proceed)=>{context.Connection.RemoteIpAddress=IPAddress.Parse(context.Request.Headers["X-Test-Address"].FirstOrDefault()??"198.51.100.254");await proceed();});next(app);};}
