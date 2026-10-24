using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HackAcad.Web.Controllers;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
[assembly: WebApplicationFactoryContentRoot("HackAcad.Web", "../../../../../", "HackAcad.Web.csproj", "0")]

if (!(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ?? "").Contains("database=hackacad_mergereview_20261019", StringComparison.OrdinalIgnoreCase)
    || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Email__DevelopmentPickupDirectory"))
    || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Sms__DevelopmentPickupDirectory")))
    throw new Exception("Use the isolated merge-review database and local mail/SMS captures only.");

const string config="""
{"allowedCommands":["cd","cat","chmod","ls","nmap","curl","base64"],"initialDirectory":"/home/student","fileSystem":{"home":{"student":{}},"var":{"secret":{"audit.sh":"review token"}}},"objectives":[{"id":"nav","type":"cwd","target":"/var/secret"},{"id":"mode","type":"chmod","target":"audit.sh"},{"id":"read","type":"cat","target":"audit.sh"}]}
""";
var checks=0;
void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
Check(SimulationVerifier.TryConfiguration(config,out _,out _),"valid existing lab configuration is accepted");
Check(!SimulationVerifier.IsComplete(config,null),"bare completion request cannot complete a lab");
Check(!SimulationVerifier.IsComplete(config,[]),"empty command history is rejected");
Check(!SimulationVerifier.IsComplete(config,["cd /var/secret","chmod 644 audit.sh","cat audit.sh"]),"incorrect permission mode does not satisfy chmod objective");
Check(!SimulationVerifier.IsComplete(config,["cd /missing","chmod 755 audit.sh","cat audit.sh"]),"failed directory and file commands do not satisfy objectives");
Check(!SimulationVerifier.IsComplete(config,["cd /var/secret-extra","cat audit.sh","chmod 755 audit.sh"]),"partial directory names cannot satisfy cwd objective");
Check(SimulationVerifier.IsComplete(config,["bad-command","cd /missing","cd /var/secret","chmod 755 audit.sh","cat audit.sh"]),"learner mistakes are allowed before a valid complete command sequence");
Check(SimulationVerifier.IsComplete(config,["cd /var/secret","chmod 755 /var/secret/audit.sh","cat /var/secret/audit.sh"]),"absolute file paths are supported");
Check(!SimulationVerifier.IsComplete(config,Enumerable.Repeat("ls",257).ToArray()),"oversized command history is rejected");
Check(!SimulationVerifier.TryConfiguration("{};alert(1)",out _,out _),"non-JSON simulation scripts are rejected");
Check(!SimulationVerifier.TryConfiguration("{}",out _,out _),"missing objective definitions are rejected");
var network=config.Replace("[{\"id\":\"nav\",\"type\":\"cwd\",\"target\":\"/var/secret\"},{\"id\":\"mode\",\"type\":\"chmod\",\"target\":\"audit.sh\"},{\"id\":\"read\",\"type\":\"cat\",\"target\":\"audit.sh\"}]","[{\"id\":\"scan\",\"type\":\"command\",\"target\":\"nmap\"},{\"id\":\"request\",\"type\":\"command\",\"target\":\"curl\"}]");
Check(SimulationVerifier.IsComplete(network,["nmap target.hackacad.local","curl http://target.hackacad.local/debug"]),"network lab command objectives work without real network requests");
var crypto=network.Replace("\"nmap\"}","\"base64\"}");
Check(!SimulationVerifier.IsComplete(crypto,["base64 -d !!!","curl /debug"]),"invalid base64 does not satisfy a decode command objective");
Check(SimulationVerifier.IsComplete(crypto,["base64 -d dGVzdA==","curl /debug"]),"valid base64 command objectives are supported");

await using var factory=new WebApplicationFactory<AccountController>();
const string password="HackAcad@123!",flag="flag{course_security_test}";
var prefix="coursesecurity."+Guid.NewGuid().ToString("N")[..8];
var uploadedPaths=new List<string>();
var adminCodes = new Dictionary<string,Queue<string>>();
var signedInAdmins = new Dictionary<HttpClient,string>();
HttpClient Client()=>factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false,BaseAddress=new Uri("https://localhost")});
async Task<string> Token(HttpClient client,string source)
{
    var page=await client.GetStringAsync(source);
    var token=Regex.Match(page,"name=\"__RequestVerificationToken\"[^>]+value=\"([^\"]+)\"").Groups[1].Value;
    if(token.Length==0)throw new Exception("No CSRF token in "+source);
    return WebUtility.HtmlDecode(token);
}
async Task<HttpResponseMessage> Post(HttpClient client,string path,IEnumerable<KeyValuePair<string,string>> fields,string source="/Identity/Account/Profile")
{
    var pairs=fields.ToList();pairs.Add(new("__RequestVerificationToken",await Token(client,source)));
    return await client.PostAsync(path,new FormUrlEncodedContent(pairs));
}
Dictionary<string,string> Fields(params string[] pairs)=>Enumerable.Range(0,pairs.Length/2).ToDictionary(i=>pairs[i*2],i=>pairs[i*2+1]);
async Task<(string Id,string Email)> Fixture(string label,string role)
{
    using var scope=factory.Services.CreateScope();var email=prefix+"."+label+"@hackacad.local";
    var user=new ApplicationUser{Email=email,UserName=email,DisplayName="Course test "+label,EmailConfirmed=true,TimeZoneId="America/New_York"};
    if(!(await scope.ServiceProvider.GetRequiredService<UserAccounts>().CreateAsync(user,password,role)).Succeeded)throw new Exception("Fixture failed");
 if(role==AccountRoles.Admin) { var manager=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();await manager.ResetAuthenticatorKeyAsync(user);await manager.SetTwoFactorEnabledAsync(user,true);adminCodes[user.Email!]=new Queue<string>((await manager.GenerateNewTwoFactorRecoveryCodesAsync(user,50))!); }

    return(user.Id,email);
}
async Task Login(HttpClient client,string email)
{
    var response=await Post(client,"/Identity/Account/Login",Fields("Email",email,"Password",password),"/Identity/Account/Login");
    Check(response.StatusCode==HttpStatusCode.Redirect,"fixture signs in normally");
 if(adminCodes.TryGetValue(email,out var recovery)) { signedInAdmins[client]=email;var complete=await Post(client,"/Identity/Account/LoginRecoveryCode",Fields("Code",recovery.Dequeue()),"/Identity/Account/LoginRecoveryCode");Check(complete.StatusCode==HttpStatusCode.Redirect,"Admin fixture completes mandatory MFA"); }

}
async Task Complete(HttpClient student,int course,int task,string[]? commands,bool expected)
{
    var data=Fields("courseId",course.ToString(),"taskId",task.ToString()).ToList();
    if(commands!=null)data.AddRange(commands.Select(c=>new KeyValuePair<string,string>("commands",c)));
    var response=await Post(student,"/Courses/CompleteSimulation",data);
    Check(response.StatusCode==(expected?HttpStatusCode.OK:HttpStatusCode.BadRequest),expected?"server accepts a complete valid virtual command sequence":"server rejects incomplete simulation proof");
}
try
{
    var instructor=await Fixture("instructor",AccountRoles.Instructor);var otherInstructor=await Fixture("otherinstructor",AccountRoles.Instructor);
    var student=await Fixture("student",AccountRoles.Student);var otherStudent=await Fixture("otherstudent",AccountRoles.Student);var admin=await Fixture("admin",AccountRoles.Admin);
    using var t=Client();using var s=Client();using var t2=Client();using var s2=Client();using var a=Client();using var guest=Client();
    await Login(t,instructor.Email);await Login(s,student.Email);await Login(t2,otherInstructor.Email);await Login(s2,otherStudent.Email);await Login(a,admin.Email);
    int privateId,publicId,lessonId,challengeId,draftChallengeId,simId,draftSimId;
    using(var scope=factory.Services.CreateScope())
    {
        var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var draft=new Course{Title=prefix+" private",InstructorId=instructor.Id,IsPublished=false};
        var course=new Course{Title=prefix+" public",InstructorId=instructor.Id,IsPublished=true,CreatedDate=new DateTime(2026,1,1,1,30,0,DateTimeKind.Utc)};
        db.Courses.AddRange(draft,course);await db.SaveChangesAsync();privateId=draft.CourseId;publicId=course.CourseId;
        var lesson=new Lesson{CourseId=publicId,Title="Review lesson",Content="Review lesson content",OrderIndex=1};db.Lessons.Add(lesson);
        var challenge=new Challenge{CourseId=publicId,Title="Review challenge",IsPublished=true,Points=100,FlagHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(flag))).ToLowerInvariant()};
        var draftChallenge=new Challenge{CourseId=publicId,Title="Draft challenge",IsPublished=false,FlagHash=challenge.FlagHash};
        db.Challenges.AddRange(challenge,draftChallenge);
        var sim=new SimulationTask{CourseId=publicId,Title="Review lab",IsPublished=true,VirtualFileSystemJson=config,RewardFlagHash="flag{reward}"};
        var draftSim=new SimulationTask{CourseId=publicId,Title="Draft lab",IsPublished=false,VirtualFileSystemJson=config};
        db.SimulationTasks.AddRange(sim,draftSim);await db.SaveChangesAsync();
        lessonId=lesson.LessonId;challengeId=challenge.ChallengeId;draftChallengeId=draftChallenge.ChallengeId;simId=sim.TaskId;draftSimId=draftSim.TaskId;
    }
    foreach(var path in new[]{"/Courses","/Courses/Details/"+publicId})
    {
        var response=await guest.GetAsync(path);
        Check(response.StatusCode==HttpStatusCode.Redirect&&response.Headers.Location!.OriginalString.Contains("/Identity/Account/Login"),"Guest course URL requires login: "+path);
    }
    var home=await guest.GetStringAsync("/");
    var guestHeader=Regex.Match(home,"<header.*?</header>",RegexOptions.Singleline).Value;
    Check(!guestHeader.Contains("href=\"/Courses\""),"Guest navigation has no course catalogue entry");
    Check(home.Contains("id=\"home-title\"")&&home.Contains("data-learning-preview")&&home.Contains("/Identity/Account/Register"),"Guest landing page shows the learning preview and working signup entry");
    var adminCatalogue=await a.GetAsync("/Courses");
    Check(adminCatalogue.StatusCode==HttpStatusCode.Redirect&&adminCatalogue.Headers.Location?.OriginalString=="/Admin/Courses","Admin catalogue URL redirects to course management");
    var instructorCatalogue=await t.GetAsync("/Courses");
    Check(instructorCatalogue.StatusCode==HttpStatusCode.OK,"Instructor can browse the course catalogue");
    Check((await a.GetAsync("/Admin/Courses")).StatusCode==HttpStatusCode.OK,"Admin retains the course moderation page");
    Check((await t.GetAsync("/InstructorCourses")).StatusCode==HttpStatusCode.OK,"Instructor retains course authoring");
    var instructorDashboard=await t.GetAsync("/Identity/Account/Dashboard");
    var instructorDashboardHtml=await instructorDashboard.Content.ReadAsStringAsync();
    Check(instructorDashboard.StatusCode==HttpStatusCode.OK&&instructorDashboardHtml.Contains("Your Progress Totals")&&instructorDashboardHtml.Contains("Badges &amp; Achievements"),"Instructor dashboard includes learner progress and badges");
    bool HasCatalogueLink(string html)=>Regex.IsMatch(Regex.Match(html,"<header.*?</header>",RegexOptions.Singleline).Value,"href=\"/Courses\"");
    Check(!HasCatalogueLink(await a.GetStringAsync("/Admin/Courses")),"Admin header has no student course link");
    var instructorWorkspace=await t.GetStringAsync("/InstructorCourses");
    Check(!HasCatalogueLink(instructorWorkspace)&&instructorWorkspace.Contains("href=\"/Courses\""),"Instructor workspace keeps a course catalogue link");
    Check((await s.GetStringAsync("/Courses")).Contains("Workspace navigation"),"Student workspace retains a sidebar catalogue link");
    // Existing self-enrollments must not let a course owner learn from their own course.
    using(var scope=factory.Services.CreateScope())
    {
        var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Enrollments.AddRange(new Enrollment{UserId=admin.Id,CourseId=publicId,Status="InProgress"},new Enrollment{UserId=instructor.Id,CourseId=publicId,Status="InProgress"});
        await db.SaveChangesAsync();
    }
    foreach(var client in new[]{a,t})
    {
        var denied=await client.GetAsync("/Courses/Learn/"+publicId);
        Check(denied.StatusCode==HttpStatusCode.Redirect&&denied.Headers.Location!.OriginalString.Contains("/Identity/Account/AccessDenied"),"Admin and course owner cannot open Learn for this course");
        foreach(var action in new[]{"Enroll","CompleteLesson","SubmitFlag","CompleteSimulation"})
        {
            var response=await Post(client,"/Courses/"+action,Fields("id",publicId.ToString(),"courseId",publicId.ToString(),"lessonId",lessonId.ToString(),"challengeId",challengeId.ToString(),"taskId",simId.ToString(),"flag",flag));
            Check(response.StatusCode==HttpStatusCode.Redirect&&response.Headers.Location!.OriginalString.Contains("/Identity/Account/AccessDenied"),"Admin or course owner is denied direct learner action: "+action);
        }
        var preview=await client.GetAsync("/Courses/Details/"+publicId);
        if(client==t)
        {
            var html=await preview.Content.ReadAsStringAsync();
            Check(preview.StatusCode==HttpStatusCode.OK&&!html.Contains("Enrol Now")&&!html.Contains("Continue Learning")&&!html.Contains("Your Course Progress"),"Course owner view hides learner controls and stale progress");
        }
    }
    var otherTeacherCourse=await t2.GetStringAsync("/Courses/Details/"+publicId);
    Check(otherTeacherCourse.Contains("Enrol Now")&&otherTeacherCourse.Contains("Save Course"),"Instructor can enrol in and save another instructor's course");
    await Post(t2,"/Courses/Enroll",Fields("id",publicId.ToString()));
    Check((await t2.GetAsync("/Courses/Learn/"+publicId)).StatusCode==HttpStatusCode.OK,"Instructor can open another instructor's enrolled course");
    var savedByInstructor=await Post(t2,"/Courses/SaveCourse",Fields("id",publicId.ToString()));
    Check(savedByInstructor.StatusCode==HttpStatusCode.Redirect&&savedByInstructor.Headers.Location?.OriginalString.Contains("/Courses/Details/"+publicId)==true,"Instructor can save another instructor's course");
    Check((await t2.GetStringAsync("/Courses/Saved")).Contains(prefix+" public"),"Instructor can view saved courses");
    Check((await Post(t2,"/Courses/CompleteLesson",Fields("courseId",publicId.ToString(),"lessonId",lessonId.ToString()))).StatusCode==HttpStatusCode.Redirect,"Instructor can save lesson progress");
    DateTime? firstLessonCompletion;
    using(var scope=factory.Services.CreateScope())
    {
        var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        firstLessonCompletion=await db.ProgressRecords.Where(p=>p.EnrollmentId==db.Enrollments.Where(e=>e.UserId==otherInstructor.Id&&e.CourseId==publicId).Select(e=>e.EnrollmentId).Single()&&p.LessonId==lessonId).Select(p=>p.CompletedUtc).SingleAsync();
    }
    await Post(t2,"/Courses/CompleteLesson",Fields("courseId",publicId.ToString(),"lessonId",lessonId.ToString()));
    using(var scope=factory.Services.CreateScope())
    {
        var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repeatedLessonCompletion=await db.ProgressRecords.Where(p=>p.EnrollmentId==db.Enrollments.Where(e=>e.UserId==otherInstructor.Id&&e.CourseId==publicId).Select(e=>e.EnrollmentId).Single()&&p.LessonId==lessonId).Select(p=>p.CompletedUtc).SingleAsync();
        Check(firstLessonCompletion==repeatedLessonCompletion,"reviewing a completed lesson keeps its original activity date");
    }
    Check((await Post(t2,"/Courses/SubmitFlag",Fields("courseId",publicId.ToString(),"challengeId",challengeId.ToString(),"flag",flag))).StatusCode==HttpStatusCode.Redirect,"Instructor can submit a challenge flag");
    await Post(t2,"/Courses/SubmitFlag",Fields("courseId",publicId.ToString(),"challengeId",challengeId.ToString(),"flag",flag));
    await Complete(t2,publicId,simId,["cd /var/secret","chmod 755 audit.sh","cat audit.sh"],true);
    var completionPage=await t2.GetStringAsync("/Courses/Learn/"+publicId);
    Check(completionPage.Contains("Finish Course"),"Instructor sees the one-time finish action after completing the course");
    await Post(t2,"/Courses/FinishCourse",Fields("courseId",publicId.ToString()));
    var learnerDashboard=await t2.GetStringAsync("/Identity/Account/Dashboard");
    Check(learnerDashboard.Contains("Academy Graduate")&&learnerDashboard.Contains("Congratulations! You completed"),"Instructor dashboard shows earned badges and course completion message");
    Check(learnerDashboard.Contains("4 events in ")&&learnerDashboard.Contains("1 CTF solve")&&learnerDashboard.Contains("1 course finished"),"activity calendar counts one lesson, one solved CTF, one lab, and one course finish despite repeats");
    Check(!(await t2.GetStringAsync("/Courses/Learn/"+publicId)).Contains("Finish Course"),"Instructor does not see the finish action again after acknowledgement");
    using(var scope=factory.Services.CreateScope())
    {
        var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var oldEnrollments=await db.Enrollments.Where(e=>e.UserId==admin.Id||e.UserId==instructor.Id).Select(e=>e.EnrollmentId).ToListAsync();
        Check(oldEnrollments.Count==2&&!await db.ProgressRecords.AnyAsync(p=>oldEnrollments.Contains(p.EnrollmentId))&&!await db.ChallengeSubmissions.AnyAsync(c=>c.UserId==admin.Id||c.UserId==instructor.Id),"blocked Admin and own-course requests preserve enrollments without progress or attempts");
    }
    Check((await guest.GetAsync("/Courses/Learn/"+publicId)).StatusCode==HttpStatusCode.Redirect,"anonymous users cannot open learning routes");
    Check((await s.GetAsync("/Courses/Details/"+privateId)).StatusCode==HttpStatusCode.NotFound,"private details stay private");
    Check((await s.GetAsync("/Courses/Learn/"+privateId)).StatusCode==HttpStatusCode.NotFound,"private learning route is blocked");
    Check((await Post(s,"/Courses/Enroll",Fields("id",privateId.ToString()))).StatusCode==HttpStatusCode.NotFound,"private enrollment POST is blocked");
    Check((await t.GetAsync("/Courses/Details/"+privateId)).StatusCode==HttpStatusCode.OK,"course owner retains private preview");
    var adminDetails=await a.GetAsync("/Courses/Details/"+privateId);
    Check(adminDetails.StatusCode==HttpStatusCode.Redirect&&adminDetails.Headers.Location?.OriginalString=="/Admin/EditCourse/"+privateId,"Admin course details open the moderation page");
    Check((await t2.GetAsync("/Courses/Details/"+privateId)).StatusCode==HttpStatusCode.NotFound,"other instructors cannot preview a private course");
    Check((await s.GetAsync("/Courses/Learn/"+publicId)).StatusCode==HttpStatusCode.Redirect,"GET Learn does not silently enroll a student");
    using(var scope=factory.Services.CreateScope())Check(!await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Enrollments.AnyAsync(e=>e.UserId==student.Id),"read requests create no enrollments");
    Check((await s.PostAsync("/Courses/Enroll",new FormUrlEncodedContent(Fields("id",publicId.ToString())))).StatusCode==HttpStatusCode.BadRequest,"enrollment still requires CSRF");
    await Post(s,"/Courses/Enroll",Fields("id",publicId.ToString()));await Post(s,"/Courses/Enroll",Fields("id",publicId.ToString()));
    Check((await s.GetAsync("/Courses/Learn/"+publicId)).StatusCode==HttpStatusCode.OK,"explicit enrollment opens the player");
    using(var scope=factory.Services.CreateScope())Check(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Enrollments.CountAsync(e=>e.UserId==student.Id&&e.CourseId==publicId)==1,"repeated enrollment is idempotent");
    Check((await Post(s,"/Courses/SubmitFlag",Fields("courseId",publicId.ToString(),"challengeId",draftChallengeId.ToString(),"flag",flag))).StatusCode==HttpStatusCode.NotFound,"unpublished challenges cannot receive submissions");
    Check((await Post(s,"/Courses/CompleteSimulation",Fields("courseId",publicId.ToString(),"taskId",draftSimId.ToString()))).StatusCode==HttpStatusCode.NotFound,"unpublished labs cannot be completed");
    Check((await Post(s2,"/Courses/CompleteLesson",Fields("courseId",publicId.ToString(),"lessonId",lessonId.ToString()))).StatusCode==HttpStatusCode.NotFound,"another student without enrollment cannot complete activities");

    var forged=await Post(t,"/InstructorCourses/SaveChallenge",Fields("CourseId",publicId.ToString(),"Title",prefix+" forged","Category","Forensics","Difficulty","Easy","Points","100","PlaintextFlag",flag,"ExistingFilePath","../appsettings.json","IsPublished","true"));
    int forgedId;
    using(var scope=factory.Services.CreateScope())
    {
        var item=await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Challenges.SingleAsync(c=>c.Title==prefix+" forged");forgedId=item.ChallengeId;
        Check(forged.StatusCode==HttpStatusCode.Redirect&&item.FilePath==null,"posted attachment path is ignored on creation");
        item.FilePath="../appsettings.json";item.IsPublished=false;await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();
    }
    Check((await t.GetAsync("/Courses/DownloadChallengeFile/"+forgedId)).StatusCode==HttpStatusCode.NotFound,"legacy unsafe database path is rejected even for its owner");
    var env=factory.Services.GetRequiredService<IWebHostEnvironment>();
    foreach(var path in new[]{"../appsettings.json","/uploads/ctf/../../appsettings.json","/uploads/ctf/..\\..\\appsettings.json","/uploads/ctf/C:secret","/course-files/ctf/../secret"})
        Check(CourseFiles.Resolve(env,path)==null,"attachment resolver rejects traversal and alternate path syntax");
    using(var multipart=new MultipartFormDataContent())
    {
        foreach(var f in Fields("CourseId",publicId.ToString(),"Title",prefix+" upload","Category","Forensics","Difficulty","Easy","Points","100","PlaintextFlag",flag,"IsPublished","true"))multipart.Add(new StringContent(f.Value),f.Key);
        multipart.Add(new StringContent(await Token(t,"/Identity/Account/Profile")),"__RequestVerificationToken");
        multipart.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("<h1>Review attachment</h1>")),"UploadFile","review.html");
        Check((await t.PostAsync("/InstructorCourses/SaveChallenge",multipart)).StatusCode==HttpStatusCode.Redirect,"Instructor can still upload a real challenge attachment");
    }
    int uploadId; string? savedAttachmentPath;
    using(var scope=factory.Services.CreateScope())
    {
        var item=await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Challenges.SingleAsync(c=>c.Title==prefix+" upload");uploadId=item.ChallengeId;savedAttachmentPath=item.FilePath;
        var path=CourseFiles.Resolve(env,item.FilePath)!;uploadedPaths.Add(path);Check(File.Exists(path)&&path.Contains("App_Data"),"new attachments are stored outside the public web root");
        item.IsPublished=false;await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();
    }
    Check((await s.GetAsync("/Courses/DownloadChallengeFile/"+uploadId)).StatusCode==HttpStatusCode.NotFound,"draft challenge attachment is blocked for a student");
    var attachment=await t.GetAsync("/Courses/DownloadChallengeFile/"+uploadId);
    Check(attachment.StatusCode==HttpStatusCode.OK&&attachment.Content.Headers.ContentDisposition?.DispositionType=="attachment","owner can safely download an HTML attachment without inline execution");
    Check((await guest.GetAsync("/uploads/ctf/auth_portal.js")).StatusCode==HttpStatusCode.NotFound,"static legacy attachment URLs cannot bypass access checks");
    var editUpload=await Post(t,"/InstructorCourses/SaveChallenge",Fields("CourseId",publicId.ToString(),"ChallengeId",uploadId.ToString(),"Title",prefix+" upload","Category","Forensics","Difficulty","Easy","Points","100","ExistingFilePath","../appsettings.json","IsPublished","false"));
    using(var scope=factory.Services.CreateScope())Check(editUpload.StatusCode==HttpStatusCode.Redirect&&(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Challenges.FindAsync(uploadId))?.FilePath==savedAttachmentPath,"editing an attachment ignores forged paths and preserves its existing file");
    var sizeRejected=false;
    try{await CourseFiles.SaveAsync(env,new Microsoft.AspNetCore.Http.FormFile(Stream.Null,0,CourseFiles.MaximumLength+1,"UploadFile","large.txt"));}catch(ArgumentException){sizeRejected=true;}
    Check(sizeRejected,"oversized uploads are rejected before any disk write");
    var otherEdit=await Post(t2,"/InstructorCourses/SaveLesson",Fields("CourseId",publicId.ToString(),"LessonId",lessonId.ToString(),"Title","Changed"));
    Check(otherEdit.StatusCode==HttpStatusCode.NotFound,"other instructors cannot edit course content by changing IDs");
    int unusedLessonId,unusedChallengeId,unusedSimId;
    using(var scope=factory.Services.CreateScope())
    {
        var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var l=new Lesson{CourseId=publicId,Title="Unused lesson"};var c=new Challenge{CourseId=publicId,Title="Unused challenge",FlagHash="unused"};var task=new SimulationTask{CourseId=publicId,Title="Unused lab"};
        db.AddRange(l,c,task);await db.SaveChangesAsync();unusedLessonId=l.LessonId;unusedChallengeId=c.ChallengeId;unusedSimId=task.TaskId;
    }
    foreach(var pair in new[]{("DeleteLesson",unusedLessonId),("DeleteChallenge",unusedChallengeId),("DeleteSimulation",unusedSimId)})
        Check((await Post(t,"/InstructorCourses/"+pair.Item1,Fields("id",pair.Item2.ToString()))).StatusCode==HttpStatusCode.Redirect,"unused content can still be deleted normally");
    using(var scope=factory.Services.CreateScope())
    {
        var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Check(!await db.Lessons.AnyAsync(l=>l.LessonId==unusedLessonId)&&!await db.Challenges.AnyAsync(c=>c.ChallengeId==unusedChallengeId)&&!await db.SimulationTasks.AnyAsync(t=>t.TaskId==unusedSimId),"unused content deletion actually removes only the selected records");
    }


    var invalid=await Post(t,"/InstructorCourses/SaveSimulation",Fields("CourseId",publicId.ToString(),"TaskId",simId.ToString(),"Title","Review lab","VirtualFileSystemJson","invalid JSON","IsPublished","true"));
    Check(invalid.StatusCode==HttpStatusCode.OK&&(await invalid.Content.ReadAsStringAsync()).Contains("Enter a JSON object"),"malformed simulation configuration shows an inline validation error");
    var scriptConfig=config.Replace("review token","</script><script>window.__reviewMarker=1</script><script>");
    var safeJson=SimulationVerifier.SafeJson(scriptConfig);
    Check(!safeJson.Contains("</script>")&&safeJson.Contains("u003C"),"HTML-safe serialization escapes script-closing sequences");
    await Post(t,"/InstructorCourses/SaveSimulation",Fields("CourseId",publicId.ToString(),"TaskId",simId.ToString(),"Title","Review lab","VirtualFileSystemJson",scriptConfig,"RewardFlag","flag{reward}","IsPublished","true"));
    var lab=await s.GetStringAsync($"/Courses/Learn/{publicId}?simulationId={simId}");
    Check(!lab.Contains("<script>window.__reviewMarker")&&lab.Contains("u003C"),"stored JSON cannot break out of the rendered lab script");
    await Complete(s,publicId,simId,null,false);await Complete(s,publicId,simId,["cd /var/secret","chmod 644 audit.sh","cat audit.sh"],false);
    await Complete(s,publicId,simId,["cd /var/secret","chmod 755 audit.sh","cat audit.sh"],true);
    await Complete(s,publicId,simId,["cd /var/secret","chmod 755 audit.sh","cat audit.sh"],true);
    using(var scope=factory.Services.CreateScope())
    {
        var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var enrollment=await db.Enrollments.SingleAsync(e=>e.UserId==student.Id&&e.CourseId==publicId);
        Check(await db.ProgressRecords.CountAsync(p=>p.EnrollmentId==enrollment.EnrollmentId&&p.SimulationTaskId==simId)==1,"repeated valid completion does not duplicate progress");
        Check(enrollment.Status=="InProgress","partial course remains InProgress");
    }
    await Post(s,"/Courses/CompleteLesson",Fields("courseId",publicId.ToString(),"lessonId",lessonId.ToString()));
    await Post(s,"/Courses/SubmitFlag",Fields("courseId",publicId.ToString(),"challengeId",challengeId.ToString(),"flag","wrong"));
    await Post(s,"/Courses/SubmitFlag",Fields("courseId",publicId.ToString(),"challengeId",challengeId.ToString(),"flag",flag));
    using(var scope=factory.Services.CreateScope())
    {
        var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var enrollment=await db.Enrollments.SingleAsync(e=>e.UserId==student.Id&&e.CourseId==publicId);
        Check(enrollment.Status=="Completed","first completion of the final item immediately marks the course Completed");
        Check(await db.ChallengeSubmissions.CountAsync(p=>p.UserId==student.Id)==2,"both incorrect and correct flag attempts are preserved");
        foreach(var row in await db.ChallengeSubmissions.Where(p=>p.UserId==student.Id).ToListAsync())row.SubmittedDate=new DateTime(2026,1,1,1,30,0,DateTimeKind.Utc);
        await db.SaveChangesAsync();
    }
    foreach(var pair in new[]{("DeleteLesson",lessonId),("DeleteChallenge",challengeId),("DeleteSimulation",simId)})
    {
        var deleted=await Post(t,"/InstructorCourses/"+pair.Item1,Fields("id",pair.Item2.ToString()));
        Check(deleted.StatusCode==HttpStatusCode.Redirect,"deletion with learning records returns a controlled redirect rather than 500");
        Check((await t.GetStringAsync("/InstructorCourses/Curriculum/"+publicId)).Contains("cannot be deleted"),"curriculum explains why student records prevent deletion");
    }
    Check((await s.GetStringAsync("/Identity/Account/Dashboard")).Contains("31 Dec 2025, 20:30:00"),"new Dashboard timestamps use the viewer's saved zone");
    Check((await s.GetStringAsync($"/Courses/Learn/{publicId}?challengeId={challengeId}")).Contains("31 Dec 2025, 20:30:00"),"new learning history uses the viewer's saved zone");
    Check((await t.GetStringAsync("/InstructorCourses")).Contains("31 Dec 2025"),"Instructor course creation dates use the viewer's saved zone");
    Check((await t.GetStringAsync("/InstructorCourses/Curriculum/"+publicId)).Contains("31 Dec 2025, 20:30:00"),"Instructor submission timestamps use the viewer's saved zone");
    await Post(t,"/InstructorCourses/TogglePublish",Fields("id",publicId.ToString()));
    Check((await s.GetAsync("/Courses/Learn/"+publicId)).StatusCode==HttpStatusCode.NotFound,"unpublishing revokes learning access for existing enrollments");
    Check((await Post(s,"/Courses/SubmitFlag",Fields("courseId",publicId.ToString(),"challengeId",challengeId.ToString(),"flag",flag))).StatusCode==HttpStatusCode.NotFound,"unpublishing also blocks completion submissions");
    Console.WriteLine($"All {checks} course security checks passed. No live email, SMS, shell or network commands were used.");
}
finally
{
    using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var ids=await db.Users.Where(u=>u.Email!=null&&u.Email.StartsWith(prefix)).Select(u=>u.Id).ToListAsync();
    var courses=await db.Courses.Where(c=>c.Title.StartsWith(prefix)).Select(c=>c.CourseId).ToListAsync();
    var enrollmentIds=await db.Enrollments.Where(e=>ids.Contains(e.UserId)).Select(e=>e.EnrollmentId).ToListAsync();
    await db.ProgressRecords.Where(p=>enrollmentIds.Contains(p.EnrollmentId)).ExecuteDeleteAsync();
    await db.ChallengeSubmissions.Where(c=>ids.Contains(c.UserId)).ExecuteDeleteAsync();
    await db.Enrollments.Where(e=>ids.Contains(e.UserId)).ExecuteDeleteAsync();
    await db.Courses.Where(c=>courses.Contains(c.CourseId)).ExecuteDeleteAsync();
    var manager=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    foreach(var id in ids){var user=await manager.FindByIdAsync(id);if(user!=null)await manager.DeleteAsync(user);}
    foreach(var path in uploadedPaths)if(File.Exists(path))File.Delete(path);
}
