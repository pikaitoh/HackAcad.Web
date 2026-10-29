// ADMIN - Course approval evidence: role checks, mandatory rejection feedback, resubmission, publication and stale-version protection.
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
if (!(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ?? "").Contains("database=hackacad_course_review_20261026",StringComparison.OrdinalIgnoreCase)
    || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Email__DevelopmentPickupDirectory")) || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Sms__DevelopmentPickupDirectory"))) throw new Exception("Use isolated review database and local message captures only.");
var checks=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
await using var factory=new WebApplicationFactory<AccountController>();
var prefix="coursereview."+Guid.NewGuid().ToString("N")[..8];const string password="HackAcad@123!";
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

async Task<Course> Read(int id) { using var scope=factory.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Courses.AsNoTracking().SingleAsync(c=>c.CourseId==id); }
async Task<int> Create(HttpClient client, string title) {
 var response=await Post(client,"/InstructorCourses/Create",Fields("Title",title,"Category","Web Security","Description","Cybersecurity course for review.","IsPublished","true","ReviewStatus","Approved"));
 Check(response.StatusCode==HttpStatusCode.Redirect,"instructor saves a course");
 return int.Parse(Regex.Match(response.Headers.Location!.ToString(),@"/Curriculum/(\d+)").Groups[1].Value);
}
async Task<HttpResponseMessage> SubmitCourse(HttpClient client,int id,string? version=null) => await Post(client,"/InstructorCourses/SubmitForReview/"+id,Fields("version",version??(await Read(id)).ReviewVersion));
async Task<HttpResponseMessage> Decide(HttpClient client,int id,string decision,string reason="",string? version=null) => await Post(client,"/Admin/ReviewCourse/"+id,Fields("Input.Version",version??(await Read(id)).ReviewVersion,"Input.Decision",decision,"Input.Reason",reason));
async Task<int> AddLesson(HttpClient client,int id) {
 var response=await Post(client,"/InstructorCourses/SaveLesson",Fields("CourseId",id.ToString(),"Title","Security fundamentals","Content","Read logs safely. <script>alert('test')</script>","OrderIndex","0"));
 Check(response.StatusCode==HttpStatusCode.Redirect,"instructor adds curriculum");
 using var scope=factory.Services.CreateScope();return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Lessons.Where(l=>l.CourseId==id).Select(l=>l.LessonId).FirstAsync();
}
async Task EditTitle(HttpClient client,int id,string title) {
 var response=await Post(client,"/InstructorCourses/Edit",Fields("CourseId",id.ToString(),"Title",title,"Category","Web Security","IsPublished","false"));
 Check(response.StatusCode==HttpStatusCode.Redirect,"instructor saves revised details");
}
bool Denied(HttpResponseMessage response) => response.StatusCode==HttpStatusCode.Forbidden || response.Headers.Location?.ToString().Contains("AccessDenied")==true;
var createdUsers=new List<string>();
try {
 var instructor=await Fixture("instructor",AccountRoles.Instructor);var other=await Fixture("other",AccountRoles.Instructor);
 var admin=await Fixture("admin",AccountRoles.Admin);var auditor=await Fixture("auditor",AccountRoles.Admin);var student=await Fixture("student",AccountRoles.Student);
 createdUsers.AddRange(new[]{instructor.Id,other.Id,admin.Id,auditor.Id,student.Id});
 using(var scope=factory.Services.CreateScope()) { var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var row=await db.Admins.SingleAsync(a=>a.UserId==auditor.Id);row.IsReadOnly=true;await db.SaveChangesAsync(); }
 using var t=Client();using var t2=Client();using var a=Client();using var ro=Client();using var s=Client();using var guest=Client();
 await Login(t,instructor.Email);await Login(t2,other.Email);await Login(a,admin.Email);await Login(ro,auditor.Email);await Login(s,student.Email);
 var createPage=await t.GetStringAsync("/InstructorCourses/Create");
 Check(!createPage.Contains("Publish immediately")&&!createPage.Contains("name=\"IsPublished\""),"create page has no publication control");
 var id=await Create(t,"Course review test");var course=await Read(id);
 Check(!course.IsPublished&&course.ReviewStatus==CourseReviewStatus.Draft,"forged publication and approval fields cannot publish a new course");
 Check((await SubmitCourse(t,id)).StatusCode==HttpStatusCode.Redirect&&(await Read(id)).ReviewStatus==CourseReviewStatus.Draft,"empty courses cannot request review");
 var lesson=await AddLesson(t,id);
 Check((await SubmitCourse(t2,id)).StatusCode==HttpStatusCode.NotFound,"another instructor cannot submit this course");
 Check((await guest.PostAsync("/InstructorCourses/SubmitForReview/"+id,new FormUrlEncodedContent(Fields("version",course.ReviewVersion)))).StatusCode!=HttpStatusCode.OK,"anonymous submission is denied");
 Check((await t.PostAsync("/InstructorCourses/SubmitForReview/"+id,new FormUrlEncodedContent(Fields("version",course.ReviewVersion)))).StatusCode==HttpStatusCode.BadRequest,"submission requires CSRF protection");
 await Post(t,"/InstructorCourses/TogglePublish/"+id,Fields());
 Check(!(await Read(id)).IsPublished,"publish toggle rejects unapproved courses");
 var forged=await Post(t,"/InstructorCourses/Edit",Fields("CourseId",id.ToString(),"Title","Forged","Category","Web Security","IsPublished","true"));
 Check(forged.StatusCode==HttpStatusCode.OK&&!(await Read(id)).IsPublished,"settings POST cannot publish an unapproved course");
 Check((await SubmitCourse(t,id)).StatusCode==HttpStatusCode.Redirect,"course with curriculum submits for review");
 course=await Read(id);Check(course.ReviewStatus==CourseReviewStatus.Pending&&course.ReviewRevision==1&&!course.IsPublished,"submission is pending and unpublished");
 Check((await SubmitCourse(t,id)).StatusCode==HttpStatusCode.Conflict,"duplicate submission is rejected");
 Check((await t.GetStringAsync("/InstructorCourses/Curriculum/"+id)).Contains("Awaiting Admin approval"),"instructor sees pending state");
 var review=await a.GetStringAsync("/Admin/ReviewCourse/"+id);
 if (Environment.GetEnvironmentVariable("COURSE_REVIEW_PREVIEW") is string preview) {
  Directory.CreateDirectory(preview);
  await File.WriteAllTextAsync(Path.Combine(preview,"admin.html"),review);
  await File.WriteAllTextAsync(Path.Combine(preview,"instructor.html"),await t.GetStringAsync("/InstructorCourses/Curriculum/"+id));
 }
 Check(review.Contains("Security fundamentals")&&review.Contains("&lt;script&gt;")&&!review.Contains("<script>alert('test')</script>"),"Admin can inspect safely encoded curriculum");
 Check((await a.GetStringAsync("/Admin/Courses?status=Pending")).Contains("Course review test"),"Admin pending filter includes submitted course");
 Check(Denied(await ro.GetAsync("/Admin/ReviewCourse/"+id)),"read-only auditor cannot review courses");
 Check(Denied(await Decide(ro,id,CourseReviewStatus.Approved)),"read-only auditor cannot approve by POST");
 Check((await Decide(t,id,CourseReviewStatus.Approved)).StatusCode!=HttpStatusCode.OK,"instructor cannot approve courses");
 Check((await s.GetAsync("/Courses/Details/"+id)).StatusCode==HttpStatusCode.NotFound,"pending course is hidden from students");
 var missingReason=await Decide(a,id,CourseReviewStatus.Rejected,"   ");
 Check(missingReason.StatusCode==HttpStatusCode.OK&&(await missingReason.Content.ReadAsStringAsync()).Contains("Explain why this course is rejected"),"rejecting requires a nonblank reason");
 Check((await Read(id)).ReviewStatus==CourseReviewStatus.Pending,"invalid rejection leaves submission pending");
 Check((await Decide(a,id,"Published")).StatusCode==HttpStatusCode.OK&&(await Read(id)).ReviewStatus==CourseReviewStatus.Pending,"unsupported review decisions are rejected");
 const string reason="Add cybersecurity learning outcomes before resubmitting.";
 var rejectedResponse=await Decide(a,id,CourseReviewStatus.Rejected,reason);
 Check(rejectedResponse.StatusCode==HttpStatusCode.Redirect&&rejectedResponse.Headers.Location?.OriginalString=="/Admin/Courses","rejection after MFA returns to all courses without a status filter");
 if (Environment.GetEnvironmentVariable("COURSE_REVIEW_PREVIEW") is string rejectedPreview)
  await File.WriteAllTextAsync(Path.Combine(rejectedPreview,"rejected.html"),await t.GetStringAsync("/InstructorCourses/Curriculum/"+id));
 course=await Read(id);Check(course.ReviewStatus==CourseReviewStatus.Rejected&&course.ReviewReason==reason&&!course.IsPublished,"rejection and full reason are persisted");
 Check((await t.GetStringAsync("/InstructorCourses/Curriculum/"+id)).Contains(reason),"instructor can read rejection feedback");
 Check((await SubmitCourse(t,id)).StatusCode==HttpStatusCode.Conflict,"unchanged rejected course cannot be resubmitted");
 await EditTitle(t,id,"Revised cybersecurity course");
 Check((await Read(id)).ReviewStatus==CourseReviewStatus.Draft,"revision returns rejected course to draft");
 Check((await SubmitCourse(t,id)).StatusCode==HttpStatusCode.Redirect&&(await Read(id)).ReviewRevision==2,"revised course can request another review");
 var stale=(await Read(id)).ReviewVersion;
 await Post(t,"/InstructorCourses/SaveLesson",Fields("CourseId",id.ToString(),"LessonId",lesson.ToString(),"Title","Revised fundamentals","Content","Read secure audit logs.","OrderIndex","0"));
 Check((await Read(id)).ReviewStatus==CourseReviewStatus.Draft,"editing pending curriculum cancels submission");
 Check((await Decide(a,id,CourseReviewStatus.Approved,version:stale)).StatusCode==HttpStatusCode.Conflict,"Admin cannot approve outdated content");
 await SubmitCourse(t,id);
 var approvedResponse=await Decide(a,id,CourseReviewStatus.Approved);
 Check(approvedResponse.StatusCode==HttpStatusCode.Redirect&&approvedResponse.Headers.Location?.OriginalString=="/Admin/Courses","approval returns to all courses without a status filter");
 course=await Read(id);Check(course.ReviewStatus==CourseReviewStatus.Approved&&!course.IsPublished,"approval does not automatically publish");
 Check((await t.GetStringAsync("/InstructorCourses/Curriculum/"+id)).Contains("Publish course"),"approved instructor is offered publication choice");
 await Post(t,"/InstructorCourses/TogglePublish/"+id,Fields());
 Check((await Read(id)).IsPublished,"instructor can publish approved content");
 Check((await s.GetAsync("/Courses/Details/"+id)).StatusCode==HttpStatusCode.OK,"published approved course is visible to students");
 await Post(t,"/InstructorCourses/TogglePublish/"+id,Fields());await Post(t,"/InstructorCourses/TogglePublish/"+id,Fields());
 Check((await Read(id)).ReviewStatus==CourseReviewStatus.Approved&&(await Read(id)).IsPublished,"publication-only toggles preserve approval");
 await Post(t,"/InstructorCourses/SaveLesson",Fields("CourseId",id.ToString(),"LessonId",lesson.ToString(),"Title","Revised fundamentals","Content","Read secure audit logs.","OrderIndex","0"));
 Check((await Read(id)).ReviewStatus==CourseReviewStatus.Approved&&(await Read(id)).IsPublished,"unchanged curriculum saves preserve approval");
 await Post(t,"/InstructorCourses/SaveLesson",Fields("CourseId",id.ToString(),"LessonId",lesson.ToString(),"Title","Changed after approval","Content","New course material.","OrderIndex","0"));
 Check((await Read(id)).ReviewStatus==CourseReviewStatus.Draft&&!(await Read(id)).IsPublished,"editing approved lesson atomically unpublishes and cancels approval");
 Check((await s.GetAsync("/Courses/Details/"+id)).StatusCode==HttpStatusCode.NotFound,"students cannot read modified unapproved content");
 Check((await Post(a,"/Admin/EditCourse/"+id,Fields("Title","Unapproved admin publish","Category","Web Security","IsPublished","true"))).StatusCode==HttpStatusCode.OK&&!(await Read(id)).IsPublished,"Admin settings cannot bypass course approval");
 // Exercise every content type through the shared persistence boundary, including moves and deletion.
 async Task ApproveDirect() { using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var c=await db.Courses.SingleAsync(c=>c.CourseId==id);c.ReviewStatus=CourseReviewStatus.Approved;c.IsPublished=true;await db.SaveChangesAsync(); }
 async Task Mutate(string label,Action<ApplicationDbContext> mutate) { await ApproveDirect(); using(var scope=factory.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();mutate(db);await db.SaveChangesAsync();} Check(!(await Read(id)).IsPublished&&(await Read(id)).ReviewStatus==CourseReviewStatus.Draft,label); }
 await Mutate("adding CTF invalidates approval",db=>db.Challenges.Add(new Challenge{CourseId=id,Title="CTF",FlagHash="test",IsPublished=true}));
 await Mutate("changing CTF visibility invalidates approval",db=>{var c=db.Challenges.First(c=>c.CourseId==id);c.IsPublished=false;});
 await Mutate("adding lab invalidates approval",db=>db.SimulationTasks.Add(new SimulationTask{CourseId=id,Title="Lab"}));
 await Mutate("deleting lab invalidates approval",db=>db.SimulationTasks.Remove(db.SimulationTasks.First(s=>s.CourseId==id)));
 await Mutate("adding notebook invalidates approval",db=>db.CourseNotebooks.Add(new CourseNotebook{CourseId=id,Title="Notes",ContentJson="{\"version\":1,\"blocks\":[]}"}));
 await Mutate("editing notebook invalidates approval",db=>db.CourseNotebooks.First(n=>n.CourseId==id).ContentJson="{\"version\":1,\"blocks\":[{\"type\":\"markdown\",\"content\":\"Changed\"}]}");
 await Mutate("editing course metadata invalidates approval",db=>db.Courses.Single(c=>c.CourseId==id).Description="Changed description");
 await ApproveDirect();
 using(var left=factory.Services.CreateScope()) using(var right=factory.Services.CreateScope()) {
  var db1=left.ServiceProvider.GetRequiredService<ApplicationDbContext>();var db2=right.ServiceProvider.GetRequiredService<ApplicationDbContext>();
  var c1=await db1.Courses.SingleAsync(c=>c.CourseId==id);var c2=await db2.Courses.SingleAsync(c=>c.CourseId==id);
  c1.Title="Concurrent new content";await db1.SaveChangesAsync();c2.ReviewStatus=CourseReviewStatus.Approved;c2.ReviewedUtc=DateTime.UtcNow;
  var blocked=false;try{await db2.SaveChangesAsync();}catch(DbUpdateConcurrencyException){blocked=true;}Check(blocked,"concurrent review cannot overwrite a newer content version");
 }
 using(var scope=factory.Services.CreateScope()) {
  var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
  Check(await db.AdminActivities.AnyAsync(x=>x.TargetId==id.ToString()&&x.Action=="Rejected course")&&await db.AdminActivities.AnyAsync(x=>x.TargetId==id.ToString()&&x.Action=="Approved course"),"review decisions are recorded in audit log");
  Check(await db.UserNotifications.AnyAsync(n=>n.UserId==instructor.Id&&n.EventKey.StartsWith("course-decision:"+id+":")),"instructor receives decision notification");
  Check(await db.UserNotifications.AnyAsync(n=>n.UserId==admin.Id&&n.EventKey.StartsWith("course-review:"+id+":")),"Admin receives submission notification");
 }

 // The teammate's shared notebook builder must preserve ownership and approval rules.
 const string notebookJson="{\"version\":1,\"blocks\":[{\"id\":\"terminal-test\",\"type\":\"terminal_interactive\",\"title\":\"Demo terminal\",\"allowedCommands\":[\"pwd\"],\"fileSystem\":{\"home\":{\"student\":{}}}}]}";
 int personalId,courseNotebookId;
 using(var scope=factory.Services.CreateScope()) {
  var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
  var personal=new Notebook{UserId=student.Id,Title="Personal terminal",ContentJson=notebookJson};
  var shared=new CourseNotebook{CourseId=id,Title="Course terminal",ContentJson=notebookJson};
  db.AddRange(personal,shared);await db.SaveChangesAsync();personalId=personal.NotebookId;courseNotebookId=shared.CourseNotebookId;
 }
 var editor="/InstructorCourses/EditSimulation?notebookId="+personalId+"&blockId=terminal-test";
 Check((await s.GetAsync(editor)).StatusCode==HttpStatusCode.OK,"Student can open their personal terminal configuration");
 Check((await t2.GetAsync(editor)).StatusCode==HttpStatusCode.NotFound,"another user cannot open personal terminal configuration");
 Check(Denied(await s.GetAsync("/InstructorCourses/EditSimulation?courseNotebookId="+courseNotebookId+"&blockId=terminal-test")),"Student cannot edit course notebook terminal configuration");
 Check(Denied(await SubmitCourse(s,id)),"course review submission remains Instructor-only after the shared controller change");
 const string terminalConfiguration="{\"initialDirectory\":\"/home/student\",\"allowedCommands\":[\"pwd\"],\"fileSystem\":{\"home\":{\"student\":{}}},\"customCommands\":[{\"command\":\"sysinfo\",\"output\":\"Notebook output\",\"enabled\":true}]}";
 Dictionary<string,string> NotebookFields(string key,int notebook,string title="Updated terminal",string config=terminalConfiguration)=>Fields(key,notebook.ToString(),"BlockId","terminal-test","Title",title,"VirtualFileSystemJson",config,"ReturnUrl","https://example.test/untrusted");
 var badTitle=await Post(s,"/InstructorCourses/SaveSimulation",NotebookFields("NotebookId",personalId,title:""));
 Check(badTitle.StatusCode==HttpStatusCode.OK&&(await badTitle.Content.ReadAsStringAsync()).Contains("Enter a task title."),"notebook builder enforces title validation on the server");
 foreach(var invalid in new[]{"[]","null","42"}) {
  var response=await Post(s,"/InstructorCourses/SaveSimulation",NotebookFields("NotebookId",personalId,config:invalid));
  Check(response.StatusCode==HttpStatusCode.OK&&(await response.Content.ReadAsStringAsync()).Contains("Configuration must be a JSON object."),"non-object notebook configuration shows validation rather than crashing: "+invalid);
 }
 var savePersonal=await Post(s,"/InstructorCourses/SaveSimulation",NotebookFields("NotebookId",personalId));
 Check(savePersonal.Headers.Location?.OriginalString=="/Notebooks/Edit/"+personalId,"personal builder returns to its editor and ignores external return URLs");
 using(var scope=factory.Services.CreateScope()) {
  var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
  Check((await db.Notebooks.SingleAsync(n=>n.NotebookId==personalId)).ContentJson.Contains("Notebook output"),"personal notebook custom command is persisted");
 }
 Check(Denied(await Post(t2,"/InstructorCourses/SaveSimulation",NotebookFields("CourseNotebookId",courseNotebookId))),"another Instructor cannot modify course notebook terminal configuration");
 await ApproveDirect();
 var saveShared=await Post(t,"/InstructorCourses/SaveSimulation",NotebookFields("CourseNotebookId",courseNotebookId));
 Check(saveShared.Headers.Location?.OriginalString=="/InstructorCourses/EditCourseNotebook/"+courseNotebookId,"course builder returns to its own editor and ignores external return URLs");
 Check((await Read(id)).ReviewStatus==CourseReviewStatus.Draft&&!(await Read(id)).IsPublished,"new course terminal configuration route also cancels approval and unpublishes");
 using(var scope=factory.Services.CreateScope()) {
  var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var personal=await db.Notebooks.SingleAsync(n=>n.NotebookId==personalId);personal.ContentJson="{\"blocks\":null}";await db.SaveChangesAsync();
 }
 Check((await s.GetAsync(editor)).StatusCode==HttpStatusCode.NotFound,"missing terminal block list is handled without a server error");

 await ApproveDirect();
 await Post(a,"/Admin/EditCourse/"+id,Fields("Title","Admin revised course","Category","Web Security","IsPublished","true"));
 Check(!(await Read(id)).IsPublished&&(await Read(id)).ReviewStatus==CourseReviewStatus.Draft,"Admin metadata changes also require a new review");
 using(var scope=factory.Services.CreateScope()) {
  var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
  var audit=await db.AdminActivities.Where(x=>x.TargetId==id.ToString()&&x.Action=="Updated course").OrderByDescending(x=>x.Id).FirstAsync();
  Check(audit.After!.Contains("\"IsPublished\":false")&&audit.After.Contains("\"ReviewStatus\":\"Draft\""),"audit records actual publication and approval state after editing");
 }
 Console.WriteLine($"Completed {checks} course approval checks.");
} finally {
 using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
 var courseIds=await db.Courses.Where(c=>createdUsers.Contains(c.InstructorId)).Select(c=>c.CourseId).ToListAsync();
 await db.Courses.Where(c=>courseIds.Contains(c.CourseId)).ExecuteDeleteAsync();
 await db.AdminActivities.Where(a=>createdUsers.Contains(a.ActorUserId!)).ExecuteDeleteAsync();
 var manager=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
 foreach(var id in createdUsers)if(await manager.FindByIdAsync(id) is { } user)await manager.DeleteAsync(user);
}
