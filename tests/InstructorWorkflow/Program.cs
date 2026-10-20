using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HackAcad.Web.Controllers;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
[assembly: WebApplicationFactoryContentRoot("HackAcad.Web", "../../../../../", "HackAcad.Web.csproj", "0")]
if (!(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ?? "").Contains("database=hackacad_mergereview_20261019",StringComparison.OrdinalIgnoreCase)
    || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Email__DevelopmentPickupDirectory")) || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Sms__DevelopmentPickupDirectory"))) throw new Exception("Use isolated review database and local message captures only.");
var checks=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
var today=DateOnly.FromDateTime(DateTime.UtcNow);
Check(InstructorApplications.TeachingDays([new(){StartDate=today.AddYears(-3)},new(){StartDate=today.AddYears(-3)}],today)<InstructorApplications.MinimumDays(today),"overlapping teaching roles cannot turn three years into six");
Check(InstructorApplications.TeachingDays([new(){StartDate=today.AddYears(-5)}],today)>=InstructorApplications.MinimumDays(today),"exact five-year ongoing history meets the date threshold");
await using var factory=new WebApplicationFactory<AccountController>();
var prefix="instructorflow."+Guid.NewGuid().ToString("N")[..8];const string password="HackAcad@123!";
var adminCodes = new Dictionary<string,Queue<string>>();
var signedInAdmins = new Dictionary<HttpClient,string>();
HttpClient Client()=>factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false,BaseAddress=new Uri("https://localhost")});
Dictionary<string,string> Fields(params string[] pairs)=>Enumerable.Range(0,pairs.Length/2).ToDictionary(i=>pairs[i*2],i=>pairs[i*2+1]);
async Task<string> Token(HttpClient c,string path="/Identity/Account/Profile")
{
 var html=await c.GetStringAsync(path);var token=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]+value=\"([^\"]+)\"").Groups[1].Value;
 if(token.Length==0)throw new Exception("No CSRF token: "+path);return WebUtility.HtmlDecode(token);
}
async Task<HttpResponseMessage> Post(HttpClient c,string path,Dictionary<string,string> fields,string tokenPage="/Identity/Account/Profile")
{fields["__RequestVerificationToken"]=await Token(c,tokenPage);var response=await c.PostAsync(path,new FormUrlEncodedContent(fields));
 if(response.Headers.Location?.ToString().StartsWith("/Admin/Verify?ticket=")==true && signedInAdmins.TryGetValue(c,out var email)) {
  var location=response.Headers.Location.ToString();var html=await c.GetStringAsync(location);
  var ticket=WebUtility.HtmlDecode(Regex.Match(html,"name=\"Ticket\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
  var verify=await Post(c,"/Admin/Verify",Fields("Ticket",ticket,"CurrentPassword",password,"Code",adminCodes[email].Dequeue()),location);
  if(verify.StatusCode!=HttpStatusCode.OK)throw new Exception("Admin step-up failed");
  var resume=Regex.Match(await verify.Content.ReadAsStringAsync(),"<form id=\"admin-resume-form\".*?</form>",RegexOptions.Singleline).Value;
  var inputs=Regex.Matches(resume,"name=\"([^\"]+)\"[^>]*value=\"([^\"]*)\"").Select(m=>new KeyValuePair<string,string>(WebUtility.HtmlDecode(m.Groups[1].Value),WebUtility.HtmlDecode(m.Groups[2].Value)));
  response=await c.PostAsync(path,new FormUrlEncodedContent(inputs));
 }return response;}
async Task<(string Id,string Email)> Fixture(string label,string role)
{
 using var scope=factory.Services.CreateScope();var user=new ApplicationUser{Email=prefix+"."+label+"@hackacad.local",UserName=prefix+"."+label+"@hackacad.local",DisplayName="Workflow "+label,EmailConfirmed=true};
 if(!(await scope.ServiceProvider.GetRequiredService<UserAccounts>().CreateAsync(user,password,role)).Succeeded)throw new Exception("Fixture failed");
 if(role==AccountRoles.Admin) { var manager=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();await manager.ResetAuthenticatorKeyAsync(user);await manager.SetTwoFactorEnabledAsync(user,true);adminCodes[user.Email!]=new Queue<string>((await manager.GenerateNewTwoFactorRecoveryCodesAsync(user,50))!); }
return(user.Id,user.Email);
}
async Task Login(HttpClient c,string email){var response=await Post(c,"/Identity/Account/Login",Fields("Email",email,"Password",password),"/Identity/Account/Login");Check(response.StatusCode==HttpStatusCode.Redirect,"account signs in normally");
 if(adminCodes.TryGetValue(email,out var recovery)) { signedInAdmins[c]=email;var complete=await Post(c,"/Identity/Account/LoginRecoveryCode",Fields("Code",recovery.Dequeue()),"/Identity/Account/LoginRecoveryCode");Check(complete.StatusCode==HttpStatusCode.Redirect,"Admin fixture completes mandatory MFA"); }
}
async Task<HttpResponseMessage> Submit(HttpClient c,int years=6,bool invalidPdf=false,string note="")
{
 using var form=new MultipartFormDataContent();
 foreach(var pair in Fields("FullName","Instructor Applicant","WorkEmail","applicant@example.edu","TeachingTopics","Cybersecurity and Linux","Declaration","true","SubmissionNote",note,"Experience[0].Institution","Review university","Experience[0].Position","Lecturer","Experience[0].Subjects","Computer science and security","Experience[0].StartDate",today.AddYears(-years).ToString("yyyy-MM-dd"),"Experience[0].EndDate","","__RequestVerificationToken",await Token(c)))form.Add(new StringContent(pair.Value),pair.Key);
 form.Add(new ByteArrayContent(Encoding.ASCII.GetBytes(invalidPdf?"not pdf":"%PDF-1.4\n% Synthetic teaching evidence for isolated tests\n%%EOF")),"Evidence","evidence.pdf");
 return await c.PostAsync("/Education/Apply",form);
}
async Task<long> Latest(string id){using var scope=factory.Services.CreateScope();return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().InstructorApplications.Where(a=>a.UserId==id).OrderByDescending(a=>a.Revision).Select(a=>a.Id).FirstAsync();}
Dictionary<string,string> Review(string decision,bool checks=true)=>Fields("Decision",decision,"ReasonCategory",decision==ApplicationStatus.Approved?"Eligibility verified":"Information incomplete or inconsistent","Reason","Evidence has been checked against the university teaching records.","VerificationNotes","Verified staff profile and appointment records with institution references.","VerifiedYears","6","RelevantTeaching",checks.ToString(),"ExperienceVerified",checks.ToString(),"EvidenceVerified",checks.ToString(),"DetailsConsistent",checks.ToString());
try
{
 var applicant=await Fixture("applicant",AccountRoles.Student);var other=await Fixture("other",AccountRoles.Student);var admin=await Fixture("admin",AccountRoles.Admin);var secondAdmin=await Fixture("secondadmin",AccountRoles.Admin);var instructor=await Fixture("instructor",AccountRoles.Instructor);
 using var s=Client();using var s2=Client();using var a=Client();using var a2=Client();using var t=Client();using var guest=Client();
 await Login(s,applicant.Email);await Login(s2,other.Email);await Login(a,admin.Email);await Login(a2,secondAdmin.Email);await Login(t,instructor.Email);
 Check((await guest.GetAsync("/Education")).StatusCode==HttpStatusCode.OK,"For Education is publicly accessible");
 Check((await guest.GetStringAsync("/")).Contains("For Education"),"Guest navigation exposes For Education");
 Check((await guest.GetAsync("/Education/Apply")).StatusCode==HttpStatusCode.Redirect,"anonymous applications require login");
 foreach(var c in new[]{a,t})Check((await c.GetAsync("/Education/Apply")).Headers.Location!.OriginalString.Contains("AccessDenied"),"only Students can submit an Instructor application");
 foreach(var path in new[]{"CreateUser","EditUser/"+other.Id,"DeleteUser/"+other.Id})
 {
  Check((await a.GetAsync("/Admin/"+path)).StatusCode==HttpStatusCode.NotFound,"removed Admin user mutation GET is unavailable: "+path);
  Check((await Post(a,"/Admin/"+path,Fields("DisplayName","Changed","Role","Instructor"))).StatusCode==HttpStatusCode.NotFound,"removed Admin user mutation POST is unavailable: "+path);
 }
 var userPage=await a.GetStringAsync("/Admin/UserDetails/"+other.Id);
 Check(userPage.Contains("read-only")&&!userPage.Contains("name=\"Email\"")&&!userPage.Contains("name=\"Role\""),"Admin user details expose no editable personal or role fields");
 Check((await Post(a,"/Admin/AccountStatus/"+other.Id,Fields("Disable","true","Reason",""))).StatusCode==HttpStatusCode.OK,"account status changes reject missing reasons");
 Check((await Post(a,"/Admin/AccountStatus/"+admin.Id,Fields("Disable","true","Reason","Self disable test reason"))).StatusCode==HttpStatusCode.OK,"Admin cannot disable their own account");
 await Post(a,"/Admin/AccountStatus/"+other.Id,Fields("Disable","true","Reason","Isolated test account access suspension"));
 Check((await s2.GetAsync("/Identity/Account/Profile")).StatusCode==HttpStatusCode.Redirect,"disabling revokes an already signed-in session");
 using(var scope=factory.Services.CreateScope())Check(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AnyAsync(u=>u.Id==other.Id&&u.IsDisabled),"disable preserves the account record");
 await Post(a,"/Admin/AccountStatus/"+other.Id,Fields("Disable","false","Reason","Verified test account access can be restored"));await Login(s2,other.Email);
 var registerEmail=prefix+".registered@hackacad.local";
 var registration=await Post(guest,"/Identity/Account/Register",Fields("DisplayName","Public Student","Email",registerEmail,"Password",password,"ConfirmPassword",password,"Role","Instructor"),"/Identity/Account/Register");
 using(var scope=factory.Services.CreateScope())
 {
  var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var registered=await db.Users.SingleAsync(u=>u.Email==registerEmail);
  Check(await db.Students.AnyAsync(r=>r.UserId==registered.Id)&&!await db.Instructors.AnyAsync(r=>r.UserId==registered.Id),"forged role fields cannot make public registration an Instructor");
 }
 Check((await s.PostAsync("/Education/Apply",new MultipartFormDataContent())).StatusCode==HttpStatusCode.BadRequest,"Instructor application submission requires CSRF protection");
 Check((await Submit(s,3)).StatusCode==HttpStatusCode.OK,"under-five-year teaching history is rejected");
 Check((await Submit(s,6,true)).StatusCode==HttpStatusCode.OK,"fake PDF extension without PDF content is rejected");
 Check((await Submit(s)).StatusCode==HttpStatusCode.Redirect,"eligible Student can submit evidence without optional certificates or samples");
 var id=await Latest(applicant.Id);
 Check((await Submit(s)).StatusCode==HttpStatusCode.Redirect&&await Latest(applicant.Id)==id,"duplicate pending submissions do not create a second request");
 Check((await s.GetStringAsync("/Education/Status")).Contains("Pending"),"applicant can see pending status");
 Check((await guest.GetAsync("/Education/Evidence/"+id)).StatusCode==HttpStatusCode.Redirect,"anonymous evidence downloads require authentication");
 Check((await s2.GetAsync("/Education/Evidence/"+id)).StatusCode==HttpStatusCode.NotFound,"another Student cannot download private evidence");
 Check((await s2.GetAsync("/Education/Status/"+id)).StatusCode==HttpStatusCode.NotFound,"another Student cannot inspect an application");
 var file=await a.GetAsync("/Education/Evidence/"+id);Check(file.StatusCode==HttpStatusCode.OK&&file.Content.Headers.ContentDisposition?.DispositionType=="attachment","Admin can download evidence only as an attachment");
 Check((await s.GetAsync("/Admin/Applications")).StatusCode==HttpStatusCode.Redirect,"Students cannot access the administrator queue");
 Check((await a.PostAsync("/Admin/ReviewApplication/"+id,new FormUrlEncodedContent(Review(ApplicationStatus.Approved)))).StatusCode==HttpStatusCode.BadRequest,"review decision requires CSRF protection");
 Check((await Post(a,"/Admin/ReviewApplication/"+id,Review(ApplicationStatus.Approved,false))).StatusCode==HttpStatusCode.OK,"unchecked eligibility cannot be approved");
 var missing=Review(ApplicationStatus.Approved);missing["VerificationNotes"]="";
 Check((await Post(a,"/Admin/ReviewApplication/"+id,missing)).StatusCode==HttpStatusCode.OK,"approval without verification sources is rejected");
 var below=Review(ApplicationStatus.Approved);below["VerifiedYears"]="4";
 Check((await Post(a,"/Admin/ReviewApplication/"+id,below)).StatusCode==HttpStatusCode.OK,"approval below five verified years is rejected");
 var noReason=Review(ApplicationStatus.Rejected);noReason["Reason"]="";
 Check((await Post(a,"/Admin/ReviewApplication/"+id,noReason)).StatusCode==HttpStatusCode.OK,"rejection without an explanation is rejected");
 Check((await Post(a,"/Admin/ReviewApplication/"+id,Review(ApplicationStatus.MoreInformation,false))).StatusCode==HttpStatusCode.Redirect,"Admin can request more information with a reason");
 var status=await s.GetStringAsync("/Education/Status");Check(status.Contains("Revise and resubmit")&&!status.Contains("Verified staff profile"),"applicant sees revision action and public reason, not private verification notes");
 Check((await Post(a2,"/Admin/ReviewApplication/"+id,Review(ApplicationStatus.Approved))).StatusCode==HttpStatusCode.Conflict,"another Admin cannot overwrite a closed decision");
 await Submit(s,6,false,"Additional appointment documentation supplied.");var revised=await Latest(applicant.Id);
 Check(revised!=id,"reconsideration creates a new evidence revision");
 using(var scope=factory.Services.CreateScope())Check(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().InstructorApplications.CountAsync(x=>x.UserId==applicant.Id)==2,"earlier evidence submission is preserved");
 Check((await Post(a,"/Admin/ReviewApplication/"+revised,Review(ApplicationStatus.Approved))).StatusCode==HttpStatusCode.Redirect,"complete verified application can be approved");
 Check((await s.GetAsync("/Identity/Account/Profile")).StatusCode==HttpStatusCode.Redirect,"approval invalidates the old Student session");
 await Login(s,applicant.Email);
 Check((await s.GetAsync("/InstructorCourses")).StatusCode==HttpStatusCode.OK,"approved applicant receives Instructor Studio access");
 Check((await s.PostAsync("/Courses/Enroll",new FormUrlEncodedContent(Fields("id","1")))).StatusCode==HttpStatusCode.BadRequest,"Instructor learner actions still require CSRF after approval");
 using(var scope=factory.Services.CreateScope())
 {
  var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
  Check(await db.Instructors.AnyAsync(x=>x.UserId==applicant.Id)&&!await db.Students.AnyAsync(x=>x.UserId==applicant.Id),"approval atomically replaces only the role marker");
  Check(await db.InstructorReviews.CountAsync(r=>r.ApplicationId==id||r.ApplicationId==revised)==2,"decisions remain append-only across revisions");
 }
 Check((await a2.GetStringAsync("/Admin/AuditLog?search=Instructor")).Contains("Reviewed Instructor application"),"audit log includes another Admin's decisions");
 Check(!(await a2.GetStringAsync("/Admin/AuditLog?mine=true&search=Instructor")).Contains("Reviewed Instructor application"),"audit can filter to the current Admin only");
 Check((await a.GetStringAsync("/Admin/Applications")).Contains("Instructor Applicant"),"Admin queue lists latest applicant revisions");
 Check((await s.GetStringAsync("/InstructorCourses")).Contains("workspace-sidebar"),"Instructor uses the shared sidebar workspace");
 Check((await s2.GetStringAsync("/Identity/Account/Dashboard")).Contains("workspace-sidebar"),"Student uses the shared sidebar workspace");
 Check((await a.GetStringAsync("/Admin")).Contains("workspace-sidebar"),"Admin uses the shared sidebar workspace");
 Console.WriteLine($"All {checks} Instructor workflow checks passed. Isolated database only; no real messages sent.");
}
finally
{
 using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
 var ids=await db.Users.Where(u=>u.Email!=null&&u.Email.StartsWith(prefix)).Select(u=>u.Id).ToListAsync();
 var requests=await db.InstructorApplications.Where(a=>ids.Contains(a.UserId)).Select(a=>a.Id).ToListAsync();
 await db.InstructorReviews.Where(r=>requests.Contains(r.ApplicationId)).ExecuteDeleteAsync();await db.InstructorApplications.Where(a=>requests.Contains(a.Id)).ExecuteDeleteAsync();
 await db.AdminActivities.Where(a=>a.ActorUserId!=null&&ids.Contains(a.ActorUserId)).ExecuteDeleteAsync();
 var manager=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();foreach(var id in ids){var user=await manager.FindByIdAsync(id);if(user!=null)await manager.DeleteAsync(user);}
}
