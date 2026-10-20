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
var prefix="adminsecurity."+Guid.NewGuid().ToString("N")[..8]; int checks=0, address=0; int? testCourse=null;
var codes = new Dictionary<string,Queue<string>>();
Fixture? manager = null; bool automatic = false;
void Check(bool ok,string name) { if(!ok) throw new Exception(name);checks++;Console.WriteLine("PASS "+name); }
HttpClient Client() { var c=factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect=false, BaseAddress=new Uri("https://localhost") });c.DefaultRequestHeaders.Add("X-Test-Address","198.51.100."+(++address));return c; }
string Hidden(string html,string name) => WebUtility.HtmlDecode(Regex.Match(html,"name=\""+name+"\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
async Task<string> Html(HttpClient c,string path) {var r=await c.GetAsync(path);if(!r.IsSuccessStatusCode)throw new Exception($"GET {path}: {r.StatusCode} {r.Headers.Location}");return await r.Content.ReadAsStringAsync();}
async Task<HttpResponseMessage> Complete(HttpClient c,HttpResponseMessage response) {
 if(response.Headers.Location?.ToString().StartsWith("/Admin/Verify?ticket=")!=true)return response;
 var location=response.Headers.Location.ToString();var page=await Html(c,location);
 var verify=await c.PostAsync("/Admin/Verify",new FormUrlEncodedContent(new Dictionary<string,string>{{"Ticket",Hidden(page,"Ticket")},{"CurrentPassword",password},{"Code",codes[manager!.Id].Dequeue()},{"__RequestVerificationToken",Hidden(page,"__RequestVerificationToken")}}));
 if(verify.StatusCode!=HttpStatusCode.OK)throw new Exception("Native verification failed: "+verify.StatusCode);
 var resumePage=await verify.Content.ReadAsStringAsync();
 var resume=Regex.Match(resumePage,"<form id=\"admin-resume-form\".*?</form>",RegexOptions.Singleline).Value;
 Check(resumePage.Contains("admin-resume.js"),"native verification returns the original form for automatic continuation");
 var path=WebUtility.HtmlDecode(Regex.Match(resume,"id=\"admin-resume\"[^>]*action=\"([^\"]*)\"").Groups[1].Value);
 if(path.Length==0)path=WebUtility.HtmlDecode(Regex.Match(resume,"action=\"([^\"]*)\"").Groups[1].Value);
 var fields=Regex.Matches(resume,"name=\"([^\"]+)\"[^>]*value=\"([^\"]*)\"").Select(m=>new KeyValuePair<string,string>(WebUtility.HtmlDecode(m.Groups[1].Value),WebUtility.HtmlDecode(m.Groups[2].Value))).ToList();
 return await c.PostAsync(path,new FormUrlEncodedContent(fields));
}
async Task<HttpResponseMessage> Post(HttpClient c,string path,Dictionary<string,string> data,string? source=null,bool complete=true) {
 data["__RequestVerificationToken"]=Hidden(await Html(c,source??path),"__RequestVerificationToken");
 var response=await c.PostAsync(path,new FormUrlEncodedContent(data));
 return automatic && complete && path.StartsWith("/Admin/") && path!="/Admin/Verify" ? await Complete(c,response):response;
}
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
async Task<HttpResponseMessage> AsyncPost(HttpClient c,string path,Dictionary<string,string> data,string source) {
 if(!data.ContainsKey("__RequestVerificationToken"))data["__RequestVerificationToken"]=Hidden(await Html(c,source),"__RequestVerificationToken");
 using var request=new HttpRequestMessage(HttpMethod.Post,path){Content=new FormUrlEncodedContent(data)};request.Headers.Add("X-Admin-Request","fetch");return await c.SendAsync(request);
}
async Task<string> JsonTicket(HttpResponseMessage response) {
 using var json=System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());return json.RootElement.GetProperty("ticket").GetString()!;
}
async Task Proof(HttpClient c,Fixture who,string ticket) {
 var result=await AsyncPost(c,"/Admin/Verify",new(){{"Ticket",ticket},{"CurrentPassword",password},{"Code",codes[who.Id].Dequeue()}},"/Admin/Verify");
 Check(result.StatusCode==HttpStatusCode.OK && (await result.Content.ReadAsStringAsync()).Contains("verified"),"modal verification grants the requested operation");
}
IEnumerable<MimeMessage> Mails()=>Directory.GetFiles(Environment.GetEnvironmentVariable("Email__DevelopmentPickupDirectory")!,"*.eml").Select(path=>MimeMessage.Load(path));
string SupportCode(Fixture user)=>Regex.Matches(Mails().Last(m=>m.Subject=="HackAcad support: Verify your support access" && m.To.Mailboxes.Any(a=>a.Address==user.Email)).TextBody??"",@"Support/Verify\?code=([A-F0-9]{64})").Single().Groups[1].Value;
async Task OpenSupport(HttpClient c,Fixture user) { await Follow(c,await Post(c,"/Support",new(){{"Email",user.Email}}));var code=SupportCode(user);await Follow(c,await Post(c,"/Support/Verify",new(){{"code",code},{"confirm","true"}},"/Support/Verify?code="+code)); }
async Task<SupportTicket> Ticket(Fixture user) {using var s=factory.Services.CreateScope();return await s.ServiceProvider.GetRequiredService<ApplicationDbContext>().SupportTickets.AsNoTracking().SingleAsync(t=>t.UserId==user.Id);}
async Task<int> Audit(string action,string target) {using var s=factory.Services.CreateScope();return await s.ServiceProvider.GetRequiredService<ApplicationDbContext>().AdminActivities.CountAsync(a=>a.Action==action && a.TargetId==target);}
try {
 var administrator=await UserFixture("manager",AccountRoles.Admin,true);manager=administrator;var disabled=await UserFixture("disabled",disabled:true);var other=await UserFixture("other");
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
 await Complete(admin,denied); automatic=true;
 var wrongTarget=await Post(admin,"/Admin/AccountStatus/"+disabled.Id,new(){{"Disable","false"},{"Reason","Review a different account."}},"/Admin/UserDetails/"+disabled.Id,complete:false);
 Check(wrongTarget.Headers.Location?.ToString().Contains("/Admin/Verify")==true,"verification for one user cannot change another user");
 await Follow(admin,await Post(admin,"/Admin/AccountStatus/"+other.Id,new(){{"Disable","true"},{"Reason","Investigating the reported access issue."}},target));
 Check(await Audit("Disabled account",other.Id)==1,"disabled account mutation creates one atomic audit entry");
 Check((await regular.GetAsync(account+"Settings")).StatusCode==HttpStatusCode.Redirect,"disabling an account revokes the existing browser immediately");
 using var blocked=Client();var blockedPage=await Login(blocked,other);
 Check(blockedPage.Body.Contains("Contact support") && blockedPage.Body.Contains("disabled"),"disabled login explains the support path after a correct password");
 using(var s=factory.Services.CreateScope()) {var audit=await s.ServiceProvider.GetRequiredService<ApplicationDbContext>().AdminActivities.SingleAsync(a=>a.Action=="Disabled account" && a.TargetId==other.Id);Check(audit.Before=="Active" && audit.After=="Disabled" && audit.Result=="Succeeded" && audit.IpAddress.StartsWith("198.51.100.") && audit.CorrelationId.Length>0,"audit retains before/after, outcome, source and correlation");}
 // Exercise the fetch/dialog protocol without granting access before the original submission.
 var modalAdmin=await UserFixture("modalmanager",AccountRoles.Admin,true);using var modal=Client();await Login(modal,modalAdmin);
 var modalUser=await UserFixture("modaluser");var modalPath="/Admin/AccountStatus/"+modalUser.Id;var modalSource="/Admin/UserDetails/"+modalUser.Id;
 var data=new Dictionary<string,string>{{"Disable","true"},{"Reason","Original draft survives verification <script>alert(1)</script>."},{"AdminOperationId",Guid.NewGuid().ToString("N")}};
 var challenge=await AsyncPost(modal,modalPath,data,modalSource);
 Check((int)challenge.StatusCode==428 && await Audit("Disabled account",modalUser.Id)==0,"modal opens only after submission; cancellation leaves the account unchanged");
 var changed=new Dictionary<string,string>(data){["Reason"]="Attempt to replace a protected pending operation."};
 Check((await AsyncPost(modal,modalPath,changed,modalSource)).StatusCode==HttpStatusCode.Conflict,"pending operation is bound to the exact original payload");
 Check((int)(await AsyncPost(secondAdmin,modalPath,data.Where(p=>p.Key!="__RequestVerificationToken").ToDictionary(p=>p.Key,p=>p.Value),modalSource)).StatusCode==428,"same operation key cannot transfer verification to another session");
 var modalTicket=await JsonTicket(challenge);await Proof(modal,modalAdmin,modalTicket);
 var first=await AsyncPost(modal,modalPath,data,modalSource);var repeat=await AsyncPost(modal,modalPath,data,modalSource);
 Check((await first.Content.ReadAsStringAsync()).Contains("redirectUrl") && (await repeat.Content.ReadAsStringAsync()).Contains("redirectUrl") && await Audit("Disabled account",modalUser.Id)==1,"automatic continuation and repeated delivery execute exactly once");
 Check((await Html(modal,modalSource)).Contains("Account disabled."),"success feedback survives automatic continuation until the browser navigation");
 var noChangeCount=await Audit("Verified Admin identity",modalAdmin.Id);
 Check((await AsyncPost(modal,modalPath,new(){{"Disable","true"},{"Reason","Same state; no verification is necessary."}},modalSource)).StatusCode==HttpStatusCode.OK && await Audit("Verified Admin identity",modalAdmin.Id)==noChangeCount,"unchanged access needs no verification or additional mutation");
 var restore=new Dictionary<string,string>{{"Disable","false"},{"Reason","Restore the controlled test account."},{"AdminOperationId",Guid.NewGuid().ToString("N")}};
 Check((await (await AsyncPost(modal,modalPath,restore,modalSource)).Content.ReadAsStringAsync()).Contains("redirectUrl"),"five-minute grant applies to the same action and target");
 var staleUser=await UserFixture("stalepending");var stalePath="/Admin/AccountStatus/"+staleUser.Id;var staleSource="/Admin/UserDetails/"+staleUser.Id;
 var staleData=new Dictionary<string,string>{{"Disable","true"},{"Reason","Review an account changed by another administrator."},{"AdminOperationId",Guid.NewGuid().ToString("N")}};
 var staleChallenge=await AsyncPost(modal,stalePath,staleData,staleSource);await Proof(modal,modalAdmin,await JsonTicket(staleChallenge));
 using(var scope=factory.Services.CreateScope())await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.Where(u=>u.Id==staleUser.Id).ExecuteUpdateAsync(u=>u.SetProperty(x=>x.SecurityStamp,Guid.NewGuid().ToString()));
 Check((await AsyncPost(modal,stalePath,staleData,staleSource)).StatusCode==HttpStatusCode.Conflict && await Audit("Disabled account",staleUser.Id)==0,"record changes during verification stop the original operation");
 Check((await AsyncPost(modal,stalePath,new(){{"Disable","true"},{"Reason",""}},staleSource)).StatusCode==HttpStatusCode.UnprocessableEntity,"invalid input returns errors before opening verification");
 var teacher=await UserFixture("courseteacher",AccountRoles.Instructor);
 using(var scope=factory.Services.CreateScope()) {var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var course=new Course{Title="Unchanged course",Category="Security",Description="Original course description.",InstructorId=teacher.Id};db.Courses.Add(course);await db.SaveChangesAsync();testCourse=course.CourseId;}
 var coursePath="/Admin/EditCourse/"+testCourse;var courseForm=new Dictionary<string,string>{{"Title","Unchanged course"},{"Category","Security"},{"Description","Original course description."},{"IsPublished","false"},{"AdminOperationId",Guid.NewGuid().ToString("N")}};
 Check((await AsyncPost(modal,coursePath,courseForm,coursePath)).StatusCode==HttpStatusCode.OK && await Audit("Updated course",testCourse.ToString()!)==0,"unchanged course creates no challenge or audit event");
 courseForm["Title"]="Changed course";
 var courseChallenge=await AsyncPost(modal,coursePath,courseForm,coursePath);Check((int)courseChallenge.StatusCode==428,"changing a course opens a scoped verification challenge");
 await Proof(modal,modalAdmin,await JsonTicket(courseChallenge));
 var concurrent=await Task.WhenAll(AsyncPost(modal,coursePath,new(courseForm),coursePath),AsyncPost(modal,coursePath,new(courseForm),coursePath));
 Check(concurrent.All(x=>x.StatusCode==HttpStatusCode.OK) && await Audit("Updated course",testCourse.ToString()!)==1,"concurrent continuation requests save a course once");
 using(var scope=factory.Services.CreateScope()) {var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
 db.AdminActivities.AddRange(new AdminActivity{ActorUserId=administrator.Id,TargetId=modalUser.Id,TargetType="User",TargetName=prefix+" legacy",Action="Legacy management fixture",Details="Legacy event with missing context.",Before="{\"IsDisabled\":false,\"Note\":\"<script>unsafe</script>\"}",After="{\"IsDisabled\":true}"},
 new AdminActivity{ActorUserId=administrator.Id,TargetId=modalUser.Id,TargetType="Account security",TargetName=prefix+" account",Action="Account security fixture",Details="Account-specific fixture."});await db.SaveChangesAsync();}
 var auditPage=await Html(admin,"/Admin/AuditLog?search="+prefix);
 Check(auditPage.Contains("Legacy management fixture") && !auditPage.Contains("Account security fixture"),"audit defaults to management actions");
 Check(auditPage.Contains("Not recorded") && auditPage.Contains("Is Disabled") && auditPage.Contains("&lt;script&gt;unsafe&lt;/script&gt;") && !auditPage.Contains("<script>unsafe"),"audit renders readable values, escapes untrusted text and labels missing metadata");
 Check(auditPage.Contains("aria-expanded=\"false\"") && !Regex.Replace(auditPage,"<tr id=\"audit-event-.*?</dl>\\s*</div>\\s*</td></tr>","",RegexOptions.Singleline).Contains(modalUser.Id),"technical identifiers stay within collapsed audit details");
 Check((await Html(admin,"/Admin/AuditLog?kind=account&search="+prefix)).Contains("Account security fixture"),"account security events remain available through the explicit filter");
 using var activeSupport=Client();await Login(activeSupport,modalUser);var mailCount=Mails().Count();var activeOpened=await Follow(activeSupport,await activeSupport.GetAsync("/Support"));
 Check(activeOpened.Path=="/Support/Requests" && Mails().Count()==mailCount,"active signed-in users enter their own support area without a mailbox link");
 Check(!activeOpened.Body.Contains("pagination-bar") && !activeOpened.Body.Contains("<table"),"empty personal request list uses an empty state without a pager");
 var createHtml=await Html(activeSupport,"/Support/Create");Check(!createHtml.Contains("<option>"+SupportCategory.Appeal),"active users do not see the suspension appeal option");
 var appeal=await Post(activeSupport,"/Support/Create",new(){{"Category",SupportCategory.Appeal},{"Subject","Forged suspension appeal"},{"Body","I am attempting a suspension appeal while active."}});
 Check(appeal.StatusCode==HttpStatusCode.OK && (await appeal.Content.ReadAsStringAsync()).Contains("Only a currently disabled"),"server rejects a forged suspension appeal from an active user");
 Check((await Post(activeSupport,"/Support/Create",new(){{"Category","Injected category"},{"Subject","Invalid request category"},{"Body","An unsupported category must not be accepted."}})).StatusCode==HttpStatusCode.OK,"unknown support types are rejected");
 await Follow(activeSupport,await Post(activeSupport,"/Support/Create",new(){{"Category",SupportCategory.Security},{"Subject","Please review my connected accounts"},{"Body","I have a question about a connected account on my profile."}}));
 Check((await Ticket(modalUser)).Category==SupportCategory.Security,"general account security support remains available to active users");
 Check((await Html(admin,"/Admin/SupportRequests?category="+Uri.EscapeDataString(SupportCategory.Security))).Contains("Please review my connected accounts"),"admin queue supports request type filtering");
 Check(!(await Html(admin,"/Admin/SupportRequests?category="+Uri.EscapeDataString(SupportCategory.Appeal))).Contains("Please review my connected accounts"),"category filter excludes unrelated support requests");
 Check(!(await Html(admin,"/Admin/Security")).Contains("Recent administrative actions"),"Security overview omits the duplicate administrative feed");
 Check(!(await Html(modal,account+"Settings")).Contains("View security activity"),"Connected accounts uses the existing sidebar security activity link");
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
 var created=await Follow(support,await Post(support,"/Support/Create",new(){{"Category",SupportCategory.Appeal},{"Subject","Why was my account disabled?"},{"Body","Please review my account access. I think this may be a mistake."},{"Internal","true"},{"Status","Resolved"}}));
 var ticket=await Ticket(disabled);var thread="/Support/Thread/"+ticket.Id;var adminThread="/Admin/SupportThread/"+ticket.Id;
 Check(created.Path==thread && ticket.Status=="Awaiting admin" && ticket.Category==SupportCategory.Appeal,"forged user status and internal-note flags cannot close or hide a request");
 var duplicate=await Follow(support,await Post(support,"/Support/Create",new(){{"Subject","Duplicate support request"},{"Body","Please see my existing conversation for details."}}));Check(duplicate.Path==thread && duplicate.Body.Contains("already have an open request"),"duplicate open requests return to the existing conversation");
 using var otherSupport=Client();await OpenSupport(otherSupport,other);
 Check((await otherSupport.GetAsync(thread)).StatusCode==HttpStatusCode.NotFound,"support users cannot read another user's ticket");
 Check((await Post(otherSupport,"/Support/Reply/"+ticket.Id,new(){{"Version",ticket.Version},{"Body","Attempt to reply to someone else's request."}},"/Support/Create")).StatusCode==HttpStatusCode.NotFound,"support users cannot write another user's ticket");
 Check((await Html(admin,"/Admin/SupportRequests")).Contains("Why was my account disabled?"),"new requests appear in the admin support queue");
 Check((await Html(admin,adminThread)).Contains("Internal note only"),"admin conversation distinguishes internal notes from public replies");

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

 var saved=await Post(admin,"/Admin/Alert/"+alert.Id,new(){{"Version",alert.Version},{"Status","Resolved"},{"Notes","Controlled test traffic; reviewed and marked as a false positive."},{"ReturnUrl","/Admin/Security?status=Investigating&page=2"}});
 Check(saved.Headers.Location?.ToString()=="/Admin/Security?status=Investigating&page=2","investigation save returns to the original list filter and page");
 await Follow(admin,saved);
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

 await Follow(admin,await Post(admin,"/Admin/RevokeSessions/"+setup.Id,new(){{"reason","Revoke the controlled test sessions."}},"/Admin/Sessions/"+setup.Id));
 Check((await victim.GetAsync(account+"TwoFactor")).Headers.Location?.ToString().Contains("Login")==true,"admin-revoked session is rejected on its next request");
 var expiry=await UserFixture("expired");using var exp=Client();await Post(exp,"/Support",new(){{"Email",expiry.Email}});var expiryCode=SupportCode(expiry);
 clock.Advance(TimeSpan.FromMinutes(11));var expired=await Follow(exp,await Post(exp,"/Support/Verify",new(){{"code",expiryCode},{"confirm","true"}},"/Support/Verify?code="+expiryCode));Check(expired.Body.Contains("expired"),"support email links expire after ten minutes");
 automatic=false;var expiredGrant=await Post(admin,"/Admin/AccountStatus/"+other.Id,new(){{"Disable","false"},{"Reason","Verify that an expired grant cannot act."}},target);
 Check(expiredGrant.Headers.Location?.ToString().Contains("/Admin/Verify")==true,"sensitive-action grants expire independently of the admin login");
 clock.Advance(TimeSpan.FromMinutes(16));Check((await admin.GetAsync("/Admin/Security")).Headers.Location?.ToString().Contains("Login")==true,"idle administrator session expires after fifteen minutes");
 clock.Advance(TimeSpan.FromMinutes(4));Check((await support.GetAsync(thread)).Headers.Location?.ToString().Contains("/Support")==true,"support-only session expires after thirty minutes");
 Console.WriteLine($"All {checks} Admin security and support checks passed. Isolated database; local email capture only.");
}finally {
 using var s=factory.Services.CreateScope();var db=s.ServiceProvider.GetRequiredService<ApplicationDbContext>();var users=s.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var fixtures=await users.Users.Where(u=>u.Email!=null && u.Email.StartsWith(prefix)).ToListAsync();var ids=fixtures.Select(u=>u.Id).ToList();var tickets=await db.SupportTickets.Where(t=>ids.Contains(t.UserId)).Select(t=>t.Id).ToListAsync();
 if(testCourse is { } courseId)await db.Courses.Where(c=>c.CourseId==courseId).ExecuteDeleteAsync();
 await db.SupportMessages.Where(m=>tickets.Contains(m.TicketId)).ExecuteDeleteAsync();await db.SupportTickets.Where(t=>ids.Contains(t.UserId)).ExecuteDeleteAsync();await db.SupportAccesses.Where(a=>ids.Contains(a.UserId)).ExecuteDeleteAsync();
 await db.SecurityAlerts.Where(a=>ids.Contains(a.UserId!) || a.IpAddress=="203.0.113.246" || a.EventKey.StartsWith("spray:") && a.CreatedUtc>=clock.GetUtcNow().UtcDateTime.AddDays(-1)).ExecuteDeleteAsync();
 await db.SecurityEvents.Where(e=>ids.Contains(e.UserId!) || e.IpAddress=="203.0.113.246" || e.UserId==null && e.IpAddress.StartsWith("198.51.100.")).ExecuteDeleteAsync();
 await db.AdminActivities.Where(a=>ids.Contains(a.ActorUserId!) || ids.Contains(a.TargetId)).ExecuteDeleteAsync();foreach(var fixture in fixtures)await users.DeleteAsync(fixture);
}
record Fixture(string Id,string Email,string Key);
sealed class TestClock : TimeProvider {private TimeSpan offset;public override DateTimeOffset GetUtcNow()=>DateTimeOffset.UtcNow+offset;public void Advance(TimeSpan value)=>offset+=value;}
sealed class TestClientAddress : IStartupFilter {public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)=>app=>{app.Use(async(context,proceed)=>{context.Connection.RemoteIpAddress=IPAddress.Parse(context.Request.Headers["X-Test-Address"].FirstOrDefault()??"198.51.100.254");await proceed();});next(app);};}
