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
var prefix="personalize."+Guid.NewGuid().ToString("N")[..8];const string password="HackAcad@123!";
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
async Task<HttpResponseMessage> Submit(HttpClient c,int years=6,bool invalidPdf=false,string note="",string fullName="Instructor Applicant",string topics="Cybersecurity and Linux",string subjects="Computer science and security",string email="applicant@example.edu")
{
 using var form=new MultipartFormDataContent();
 foreach(var pair in Fields("FullName",fullName,"WorkEmail",email,"TeachingTopics",topics,"Declaration","true","SubmissionNote",note,"Experience[0].Institution","Review university","Experience[0].Position","Lecturer","Experience[0].Subjects",subjects,"Experience[0].StartDate",today.AddYears(-years).ToString("yyyy-MM-dd"),"Experience[0].EndDate","","__RequestVerificationToken",await Token(c)))form.Add(new StringContent(pair.Value),pair.Key);
 form.Add(new ByteArrayContent(Encoding.ASCII.GetBytes(invalidPdf?"not pdf":"%PDF-1.4\n% Synthetic teaching evidence for isolated tests\n%%EOF")),"Evidence","evidence.pdf");
 return await c.PostAsync("/Education/Apply",form);
}
async Task<long> Latest(string id){using var scope=factory.Services.CreateScope();return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().InstructorApplications.Where(a=>a.UserId==id).OrderByDescending(a=>a.Revision).Select(a=>a.Id).FirstAsync();}
Dictionary<string,string> Review(string decision,bool checks=true)=>Fields("Decision",decision,"ReasonCategory",decision==ApplicationStatus.Approved?"Eligibility verified":"Information incomplete or inconsistent","Reason","Evidence has been checked against the university teaching records.","VerificationNotes","Verified staff profile and appointment records with institution references.","VerifiedYears","6","RelevantTeaching",checks.ToString(),"ExperienceVerified",checks.ToString(),"EvidenceVerified",checks.ToString(),"DetailsConsistent",checks.ToString());

try
{
 var student=await Fixture("student",AccountRoles.Student);var other=await Fixture("other",AccountRoles.Student);var admin=await Fixture("admin",AccountRoles.Admin);var secondAdmin=await Fixture("secondadmin",AccountRoles.Admin);var instructor=await Fixture("instructor",AccountRoles.Instructor);
 using var s=Client();using var o=Client();using var a=Client();using var a2=Client();using var t=Client();using var guest=Client();
 await Login(s,student.Email);await Login(o,other.Email);await Login(a,admin.Email);await Login(a2,secondAdmin.Email);await Login(t,instructor.Email);
 var education=await s.GetStringAsync("/Education");
 Check(!education.Contains("class=\"workspace-sidebar\"")&&education.Contains("workspace-return"),"signed-in Education omits the workspace sidebar and retains a header return link");
 Check(!education.Contains("View my application"),"no application means no misleading View my application link");
 Check((await s.GetAsync("/Education/Status")).Headers.Location!.OriginalString.EndsWith("/Education/Apply"),"direct status URL without a submission leads to the application form");
 foreach(var pair in new[]{(s,"/Identity/Account/Dashboard"),(a,"/Admin"),(t,"/InstructorCourses")})
 {
  var html=await pair.Item1.GetStringAsync(pair.Item2);
  var sidebar=Regex.Match(html,"<nav class=\"admin-nav\"[\\s\\S]*?</nav>").Value;
  Check(!sidebar.Contains("For Education"),"workspace sidebar omits For Education for each role");
  var menu=Regex.Match(html,"<nav class=\"account-menu-panel\"[\\s\\S]*?</nav>").Value;
  Check(menu.Contains("For Education")==ReferenceEquals(pair.Item1,s),"only Student account menu offers For Education");
 }
 var apply=await s.GetStringAsync("/Education/Apply");
 Check(!apply.Contains("class=\"workspace-sidebar\""),"application form uses the standalone Education layout");
 Check((await Submit(s,fullName:"1")).StatusCode==HttpStatusCode.OK,"numeric applicant name is rejected before creating an application");
 Check((await Submit(s,topics:"as")).StatusCode==HttpStatusCode.OK,"insufficient teaching topic description is rejected");
 Check((await Submit(s,subjects:"as")).StatusCode==HttpStatusCode.OK,"insufficient subject description is rejected");
 Check((await Submit(s,topics:"  as     ")).StatusCode==HttpStatusCode.OK,"padding a short description with spaces cannot bypass validation");
 using(var scope=factory.Services.CreateScope())Check(!await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().InstructorApplications.AnyAsync(x=>x.UserId==student.Id),"validation failures create no submission or revision");
 foreach(var name in new[]{"陈美玲","José O’Connor","Devi A/P Kumar"})
 {
  var model=new InstructorApplicationInput{FullName=name};var results=new List<System.ComponentModel.DataAnnotations.ValidationResult>();
  Check(System.ComponentModel.DataAnnotations.Validator.TryValidateProperty(model.FullName,new System.ComponentModel.DataAnnotations.ValidationContext(model){MemberName="FullName"},results),"name validation accepts Unicode and common name punctuation: "+name);
 }
 foreach(var name in new[]{"FullName","WorkEmail","TeachingTopics","Experience[0].Institution","Experience[0].Position","Experience[0].Subjects","Experience[0].StartDate","Experience[0].EndDate","Evidence","SupportingInformation","SubmissionNote","Declaration"})
  Check(apply.Contains("data-valmsg-for=\""+name+"\""),"inline field error exists: "+name);
 Check(apply.Contains("novalidate")&&apply.Contains("Fields marked with"),"application uses inline validation and explains required stars");
 foreach(var domain in new[]{"gmail.c","GMAIL.CON","gmial.com"})
 {
  var typo=await Submit(s,email:"applicant@"+domain);var body=await typo.Content.ReadAsStringAsync();
  Check(typo.StatusCode==HttpStatusCode.OK&&body.Contains("Did you mean gmail.com?"),"email typo produces an inline suggestion: "+domain);
 }
 foreach(var (typoDomain,expected) in new[]{("yahoo.c","yahoo.com"),("OUTLOOK.CON","outlook.com"),("hotmail.cmo","hotmail.com"),("live.cm","live.com"),("icloud.c","icloud.com"),("me.con","me.com"),("mac.cmo","mac.com"),("proton.m","proton.me"),("protonmail.c","protonmail.com"),("aol.con","aol.com"),("qq.c","qq.com"),("163.c","163.com"),("126.c","126.com")})
 {
  var model=new InstructorApplicationInput{WorkEmail="applicant@"+typoDomain};var results=new List<System.ComponentModel.DataAnnotations.ValidationResult>();
  Check(!System.ComponentModel.DataAnnotations.Validator.TryValidateProperty(model.WorkEmail,new System.ComponentModel.DataAnnotations.ValidationContext(model){MemberName="WorkEmail"},results)&&results.Any(r=>r.ErrorMessage=="Did you mean "+expected+"? Check your email domain."),"known provider typo is rejected with its correction: "+typoDomain);
 }
 var yahooTypo=await Submit(s,email:"applicant@yahoo.c");var yahooBody=await yahooTypo.Content.ReadAsStringAsync();
 Check(yahooTypo.StatusCode==HttpStatusCode.OK&&yahooBody.Contains("Did you mean yahoo.com?"),"Yahoo typo is rejected by the submission endpoint");
 using(var scope=factory.Services.CreateScope())Check(!await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().InstructorApplications.AnyAsync(x=>x.UserId==student.Id),"email typo failures do not create any submissions");
 foreach(var email in new[]{"applicant@university.edu.my","applicant@gmail.com","applicant@department.ac.uk","applicant@yahoo.com","applicant@company.co","applicant@subdomain.company.com","applicant@proton.me","applicant@protonmail.com","applicant@icloud.com","applicant@qq.com","applicant@163.com","applicant@126.com","applicant@yahoo.co.uk"})
 {
  var model=new InstructorApplicationInput{WorkEmail=email};var results=new List<System.ComponentModel.DataAnnotations.ValidationResult>();
  Check(System.ComponentModel.DataAnnotations.Validator.TryValidateProperty(model.WorkEmail,new System.ComponentModel.DataAnnotations.ValidationContext(model){MemberName="WorkEmail"},results),"email suggestions preserve valid provider and institutional domains: "+email);
 }
 Check(apply.Contains("data-val-emaildomain-map"),"server email-domain rules are also rendered for client validation");
 var emailRules=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(WebUtility.HtmlDecode(Regex.Match(apply,"data-val-emaildomain-map=\"([^\"]+)\"").Groups[1].Value))!;
 Check(emailRules["yahoo.c"]=="yahoo.com"&&emailRules["outlook.c"]=="outlook.com"&&emailRules["icloud.c"]=="icloud.com"&&emailRules["proton.m"]=="proton.me","rendered client rules share the expanded server typo corrections");
 Check(Regex.Match(apply,"<a[^>]*>Back to For Education</a>").Value.Contains("class=\"btn btn-outline-secondary\""),"application return action uses an outline button");
 var ownDetails=await a.GetStringAsync("/Admin/UserDetails/"+admin.Id);
 Check(ownDetails.Contains("user-details-self")&&!ownDetails.Contains("<h2 class=\"h5\">Account access</h2>")&&ownDetails.Contains("You cannot change your own account access."),"own Admin details use one read-only card");
 Check((await a.GetStringAsync("/Notifications")).Contains("notifications-content"),"notifications heading and list share one centered wrapper");
 var invalid=await Post(s,"/Education/Apply",Fields("FullName","Applicant","WorkEmail","aaa","Experience[0].StartDate",today.AddDays(1).ToString("yyyy-MM-dd")));
 var errors=await invalid.Content.ReadAsStringAsync();
 Check(errors.Contains("Please enter a valid email address.")&&errors.Contains("between 1950 and today"),"server rejects invalid email and future dates with field messages");
 Check(errors.Contains("Upload a PDF")&&errors.Contains("Confirm that"),"server also requires evidence and declaration");
 Check((await s.PostAsync("/Preferences/Appearance",new FormUrlEncodedContent(Fields("value","dark")))).StatusCode==HttpStatusCode.BadRequest,"appearance POST requires CSRF");
 Check((await Post(s,"/Preferences/Appearance",Fields("value","invalid"))).StatusCode==HttpStatusCode.BadRequest,"invalid appearance values rejected");
 await Post(s,"/Preferences/Appearance",Fields("value","dark","returnUrl","https://example.org"));
 Check((await s.GetStringAsync("/Identity/Account/Profile")).Contains("data-appearance=\"dark\""),"saved appearance follows the account to another page");
 Check(!(await o.GetStringAsync("/Identity/Account/Profile")).Contains("data-appearance=\"dark\""),"appearance does not change another user's account");
 Check((await Post(s,"/Preferences/Banner",Fields("key","course-999999"))).StatusCode==HttpStatusCode.BadRequest,"forged locked banner selection rejected");
 var bannerSaved=await Post(s,"/Preferences/Banner",Fields("key","midnight"));
 Check(bannerSaved.Headers.Location!.OriginalString=="/Identity/Account/Profile","banner selection returns to the Profile top without the gallery fragment");
 Check((await s.GetStringAsync("/Identity/Account/Profile")).Contains("profile-cover banner-art banner-midnight"),"base banner selection persists");
 int courseId,emptyId,lessonId,challengeId,labId,enrollmentId;
 using(var scope=factory.Services.CreateScope())
 {
  var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
  var course=new Course{Title=prefix+" Cryptography",InstructorId=instructor.Id,Category="Cryptography",IsPublished=true};
  var empty=new Course{Title=prefix+" Empty",InstructorId=instructor.Id};db.Courses.AddRange(course,empty);await db.SaveChangesAsync();courseId=course.CourseId;emptyId=empty.CourseId;
  var lesson=new Lesson{CourseId=courseId,Title="Required lesson"};var challenge=new Challenge{CourseId=courseId,Title="Required CTF",FlagHash="test",IsPublished=true};var lab=new SimulationTask{CourseId=courseId,Title="Required lab",IsPublished=true};
  db.AddRange(lesson,challenge,lab);var enrollment=new Enrollment{CourseId=courseId,UserId=student.Id,Status="Completed"};db.Enrollments.Add(enrollment);await db.SaveChangesAsync();lessonId=lesson.LessonId;challengeId=challenge.ChallengeId;labId=lab.TaskId;enrollmentId=enrollment.EnrollmentId;
  course.ReviewStatus=CourseReviewStatus.Approved;course.IsPublished=true;await db.SaveChangesAsync();
 }
 long auditBefore,noticesBefore;
 using(var scope=factory.Services.CreateScope())
 {
  var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
  auditBefore=await db.AdminActivities.CountAsync(x=>x.ActorUserId==admin.Id && x.Action=="Updated course");
  noticesBefore=await db.UserNotifications.CountAsync(x=>x.UserId==instructor.Id);
 }
 var noChange=await Post(a,"/Admin/EditCourse/"+courseId,Fields("Title",prefix+" Cryptography","Category","Cryptography","Description","","IsPublished","true"));
 Check(noChange.StatusCode==HttpStatusCode.OK&&(await noChange.Content.ReadAsStringAsync()).Contains("No changes to save."),"unchanged course POST reports no changes rather than success");
 await Post(a,"/Admin/EditCourse/"+courseId,Fields("Title","  "+prefix+" Cryptography  ","Category"," Cryptography ","Description","  ","IsPublished","true"));
 using(var scope=factory.Services.CreateScope())
 {
  var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
  Check(await db.AdminActivities.CountAsync(x=>x.ActorUserId==admin.Id && x.Action=="Updated course")==auditBefore,"identical or whitespace-only course saves create no update audit records");
  Check(await db.UserNotifications.CountAsync(x=>x.UserId==instructor.Id)==noticesBefore,"unchanged course saves create no notifications");
  Check((await db.Courses.SingleAsync(x=>x.CourseId==courseId)).Description==null,"unchanged saves do not mutate optional stored fields");
 }
 var editPage=await a.GetStringAsync("/Admin/EditCourse/"+courseId);
 Check(editPage.Contains("data-original-values"),"edit page provides persisted values for reversible dirty-state tracking");
 var changedSave=await Post(a,"/Admin/EditCourse/"+courseId,Fields("Title",prefix+" Cryptography updated","Category","Cryptography","IsPublished","true"));
 Check(changedSave.StatusCode==HttpStatusCode.Redirect,"actual course modification saves successfully");
 using(var scope=factory.Services.CreateScope())Check(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AdminActivities.CountAsync(x=>x.ActorUserId==admin.Id && x.Action=="Updated course")==auditBefore+1,"actual course modification creates exactly one update audit event");
 // Continue learner/reward checks with the revised fixture approved again.
 using(var scope=factory.Services.CreateScope()) { var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var course=await db.Courses.SingleAsync(c=>c.CourseId==courseId);course.ReviewStatus=CourseReviewStatus.Approved;course.IsPublished=true;await db.SaveChangesAsync(); }
 var blocked=await a.GetStringAsync("/Admin/DeleteCourse/"+courseId);
 Check(blocked.Contains("Course cannot be deleted")&&!blocked.Contains("Delete course permanently"),"course with enrolment has a clear explanation and no delete button");
 Check((await a.GetStringAsync("/Admin/DeleteCourse/"+emptyId)).Contains("Delete course permanently"),"unused course retains permanent deletion option");
 await s.GetAsync("/Identity/Account/Profile");
 using(var scope=factory.Services.CreateScope())
 {
  var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
  Check(!await db.BannerRewards.AnyAsync(r=>r.UserId==student.Id&&r.CourseId==courseId),"enrolment or forged Completed status alone never unlocks a reward");
  db.ProgressRecords.AddRange(new Progress{EnrollmentId=enrollmentId,ContentType=ProgressContentType.Challenge,ChallengeId=challengeId,IsCompleted=true,CompletedUtc=DateTime.UtcNow},new Progress{EnrollmentId=enrollmentId,ContentType=ProgressContentType.Simulation,SimulationTaskId=labId,IsCompleted=true,CompletedUtc=DateTime.UtcNow});await db.SaveChangesAsync();
 }
 await s.GetAsync("/Identity/Account/Profile");
 using(var scope=factory.Services.CreateScope())Check(!await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().BannerRewards.AnyAsync(r=>r.UserId==student.Id&&r.CourseId==courseId),"unfinished required lesson still prevents a reward");
 await Post(s,"/Courses/CompleteLesson",Fields("lessonId",lessonId.ToString(),"courseId",courseId.ToString()));
 await s.GetAsync("/Identity/Account/Profile");await s.GetAsync("/Identity/Account/Profile");
 long rewardNotice;
 using(var scope=factory.Services.CreateScope())
 {
  var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
  Check(await db.BannerRewards.CountAsync(r=>r.UserId==student.Id&&r.CourseId==courseId)==1,"final required item grants exactly one course banner");
  Check(await db.UserNotifications.CountAsync(n=>n.UserId==student.Id&&n.EventKey=="banner:"+courseId)==1,"repeated visits never duplicate reward notifications");
  rewardNotice=await db.UserNotifications.Where(n=>n.UserId==student.Id&&n.EventKey=="banner:"+courseId).Select(n=>n.Id).SingleAsync();
 }
 using(var scope=factory.Services.CreateScope())
 {
  var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var old=new Enrollment{UserId=other.Id,CourseId=courseId};db.Enrollments.Add(old);await db.SaveChangesAsync();
  db.ProgressRecords.AddRange(new Progress{EnrollmentId=old.EnrollmentId,ContentType=ProgressContentType.Lesson,LessonId=lessonId,IsCompleted=true,CompletedUtc=DateTime.UtcNow},new Progress{EnrollmentId=old.EnrollmentId,ContentType=ProgressContentType.Challenge,ChallengeId=challengeId,IsCompleted=true,CompletedUtc=DateTime.UtcNow},new Progress{EnrollmentId=old.EnrollmentId,ContentType=ProgressContentType.Simulation,SimulationTaskId=labId,IsCompleted=true,CompletedUtc=DateTime.UtcNow});await db.SaveChangesAsync();
 }
 await o.GetAsync("/Identity/Account/Profile");
 using(var scope=factory.Services.CreateScope())Check(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().BannerRewards.AnyAsync(r=>r.UserId==other.Id&&r.CourseId==courseId),"historical completed records are backfilled on the next signed-in visit");
 Check((await Post(o,"/Notifications/Read/"+rewardNotice,Fields())).StatusCode==HttpStatusCode.NotFound,"individual mark-as-read rejects another user's notification");
 Check((await Post(s,"/Notifications/Read/"+rewardNotice,Fields())).StatusCode==HttpStatusCode.Redirect,"owned notification can be marked read without opening its target");
 Check((await Post(s,"/Preferences/Banner",Fields("key","course-"+courseId))).StatusCode==HttpStatusCode.Redirect,"earned course banner can be selected");
 Check((await Post(t,"/Preferences/Banner",Fields("key","course-"+courseId))).StatusCode==HttpStatusCode.BadRequest,"Instructor cannot select Student reward banners");
 Check((await Post(o,"/Notifications/Open/"+rewardNotice,Fields())).StatusCode==HttpStatusCode.NotFound,"another user cannot open or mark someone else's notification");
 Check((await s.PostAsync("/Notifications/Open/"+rewardNotice,new FormUrlEncodedContent(Fields()))).StatusCode==HttpStatusCode.BadRequest,"notification mutations require CSRF");
 Check((await Post(s,"/Notifications/Open/"+rewardNotice,Fields())).Headers.Location!.OriginalString.EndsWith("Profile#profile-banners"),"opening a notification leads to its related page");
 using(var scope=factory.Services.CreateScope())Check((await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().UserNotifications.SingleAsync(n=>n.Id==rewardNotice)).ReadUtc!=null,"opening marks only the owned notification read");
 await Post(a,"/Admin/EditCourse/"+courseId,Fields("Title",prefix+" Cryptography","Category","Cryptography","IsPublished","false"));
 Check((await t.GetStringAsync("/Notifications")).Contains("Course unpublished by Admin"),"Admin unpublishing sends the owner a course notification");
 await Submit(s);var applicationId=await Latest(student.Id);
 var pendingReview=await a.GetStringAsync("/Admin/ReviewApplication/"+applicationId);
 Check(pendingReview.Contains("application-review-grid")&&pendingReview.Contains("name=\"Decision\""),"pending applications retain the side-by-side review form");
 Check(!(await s.GetStringAsync("/Education/Status")).Contains("class=\"workspace-sidebar\""),"application status also omits the workspace sidebar");
 Check((await s.GetStringAsync("/Education")).Contains("View my application"),"submitted application changes the Education CTA");
 long adminNotice;
 using(var scope=factory.Services.CreateScope())
 {
  var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
  Check(await db.UserNotifications.AnyAsync(n=>n.UserId==admin.Id&&n.EventKey=="application:"+applicationId)&&await db.UserNotifications.AnyAsync(n=>n.UserId==secondAdmin.Id&&n.EventKey=="application:"+applicationId),"each Admin gets a separate new-application notification");
  adminNotice=await db.UserNotifications.Where(n=>n.UserId==admin.Id&&n.EventKey=="application:"+applicationId).Select(n=>n.Id).SingleAsync();
 }
 await Post(a,"/Notifications/Open/"+adminNotice,Fields());
 using(var scope=factory.Services.CreateScope())Check(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().UserNotifications.AnyAsync(n=>n.UserId==secondAdmin.Id&&n.EventKey=="application:"+applicationId&&n.ReadUtc==null),"one Admin reading a notice does not mark another Admin's copy read");
 await Post(a,"/Admin/ReviewApplication/"+applicationId,Review(ApplicationStatus.MoreInformation,false));
 var closedReview=await a.GetStringAsync("/Admin/ReviewApplication/"+applicationId);
 Check(closedReview.Contains("review-closed")&&!closedReview.Contains("name=\"Decision\"")&&closedReview.Contains("Waiting for the applicant"),"reviewed application uses a single-column record and a precise waiting message");
 var adminReview=await a.GetStringAsync("/Admin/ReviewApplication/"+applicationId);
 Check(adminReview.Contains("<details class=\"verification-details\">")&&!adminReview.Contains("<details class=\"verification-details\" open"),"Admin verification is a collapsed native disclosure");
 Check(adminReview.Contains("<dt>Reason</dt>")&&adminReview.Contains("<dt>Explanation</dt>")&&adminReview.Contains("Reviewed by"),"review reason, explanation and reviewer are clearly labeled");
 Check(adminReview.Contains("Not confirmed")&&!adminReview.Contains("Relevant field: False")&&!adminReview.Contains("Experience verified: False"),"verification results use readable labeled rows instead of raw booleans");
 var studentReview=await s.GetStringAsync("/Education/Status");
 Check(!studentReview.Contains("verification-details")&&!studentReview.Contains("Verification notes"),"Student review never includes Admin-only verification content");
 using(var scope=factory.Services.CreateScope())
 {
  var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var r=await db.InstructorReviews.SingleAsync(x=>x.ApplicationId==applicationId);r.VerificationNotes="";await db.SaveChangesAsync();
 }
 Check(!(await a.GetStringAsync("/Admin/ReviewApplication/"+applicationId)).Contains("<h4 class=\"h6\">Verification notes</h4>"),"empty internal notes omit the notes heading");
 await Submit(s,note:"Additional teaching evidence supplied.");var revised=await Latest(student.Id);
 Check(revised!=applicationId&&(await a.GetStringAsync("/Admin/ReviewApplication/"+applicationId)).Contains("View latest submission"),"older reviews link to the new submission after resubmission");
 var history=await s.GetStringAsync("/Education/Status");
 Check(history.Contains("submission-history-table")&&history.Contains("Submitted on")&&history.Contains("submission-latest"),"submission history has separate columns and a Latest label");
 Check(history.Contains("Submission 1 review")&&history.Contains("data-status=\"Needs more information\""),"past review identifies its submission and renders a semantic status badge");
 Check(pendingReview.Contains("data-status=\"Pending\""),"current pending record renders the shared status badge");
 Check((await a.GetStringAsync("/Admin/Applications")).Contains("application-status"),"Admin application list uses the shared status badge");
 await Post(a,"/Admin/AccountStatus/"+other.Id,Fields("Disable","true","Reason","Isolated display validation test"));
 var accessPage=await a.GetStringAsync("/Admin/UserDetails/"+other.Id);
 Check(accessPage.IndexOf("workspace-main",StringComparison.Ordinal)<accessPage.IndexOf("Account disabled.",StringComparison.Ordinal),"success feedback is inside the workspace content and cannot push the sidebar down");
 await Post(a,"/Admin/AccountStatus/"+other.Id,Fields("Disable","false","Reason","Isolated display validation restore"));
 await Login(o,other.Email);
 Check((await s.GetStringAsync("/Notifications")).Contains("Needs more information"),"review outcome notifies the applicant");
 await Post(s,"/Notifications/ReadAll",Fields());
 using(var scope=factory.Services.CreateScope())Check(!await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().UserNotifications.AnyAsync(n=>n.UserId==student.Id&&n.ReadUtc==null),"mark all read is scoped to the current account");
 var sessionPage=await o.GetStringAsync("/Identity/Account/Sessions");
 Check(!sessionPage.Contains("name=\"CurrentPassword\""),"single-device sessions hide the unnecessary password field");
 Check(sessionPage.Contains("session-facts"),"session metadata uses aligned definition rows");
 Check(!(await s.GetStringAsync("/Identity/Account/ChangePassword")).Contains("Older password changes"),"password page omits obsolete implementation-history wording");
 var twoFactor=await s.GetStringAsync("/Identity/Account/TwoFactor");
 Check(twoFactor.Contains("class=\"status-label status-disabled\">Off</span>"),"2FA off shares the phone verification status style");
 Console.WriteLine($"All {checks} personalization checks passed.");
}
finally
{
 using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
 var ids=await db.Users.Where(u=>u.Email!=null&&u.Email.StartsWith(prefix)).Select(u=>u.Id).ToListAsync();
 var apps=await db.InstructorApplications.Where(a=>ids.Contains(a.UserId)).Select(a=>a.Id).ToListAsync();
 await db.UserNotifications.Where(n=>n.EventKey.StartsWith("application:")&&apps.Select(id=>"application:"+id).Contains(n.EventKey)).ExecuteDeleteAsync();
 await db.InstructorReviews.Where(r=>apps.Contains(r.ApplicationId)).ExecuteDeleteAsync();await db.InstructorApplications.Where(a=>apps.Contains(a.Id)).ExecuteDeleteAsync();
 var enrollmentIds=await db.Enrollments.Where(e=>ids.Contains(e.UserId)).Select(e=>e.EnrollmentId).ToListAsync();
 await db.ProgressRecords.Where(p=>enrollmentIds.Contains(p.EnrollmentId)).ExecuteDeleteAsync();await db.ChallengeSubmissions.Where(c=>ids.Contains(c.UserId)).ExecuteDeleteAsync();await db.Enrollments.Where(e=>ids.Contains(e.UserId)).ExecuteDeleteAsync();
 await db.Courses.Where(c=>c.Title.StartsWith(prefix)).ExecuteDeleteAsync();await db.AdminActivities.Where(a=>a.ActorUserId!=null&&ids.Contains(a.ActorUserId)).ExecuteDeleteAsync();
 var manager=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();foreach(var id in ids){var user=await manager.FindByIdAsync(id);if(user!=null)await manager.DeleteAsync(user);}
}
