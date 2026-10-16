using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

[Authorize(Roles = AccountRoles.Admin)]
public class AdminController(ApplicationDbContext db, UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn, UserAccounts accounts) : Controller
{
    public async Task<IActionResult> Index() => View(new AdminOverviewModel
    {
        Users = await db.Users.CountAsync(), DisabledUsers = await db.Users.CountAsync(u => u.IsDisabled),
        Teachers = await db.Teachers.CountAsync(), Courses = await db.Courses.CountAsync(),
        PublishedCourses = await db.Courses.CountAsync(c => c.IsPublished), Enrollments = await db.Enrollments.CountAsync(),
        RecentUsers = await accounts.Query().OrderByDescending(u => u.JoinedDate).Take(5).ToListAsync()
    });

    public async Task<IActionResult> Users(string? search, string? role, string? status, int page = 1)
    {
        var query = accounts.Query();
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search)) query = query.Where(u => u.DisplayName.Contains(search) || u.Email.Contains(search));
        if (AccountRoles.All.Contains(role)) query = query.Where(u => u.Role == role);
        if (status == "active") query = query.Where(u => !u.IsDisabled);
        if (status == "disabled") query = query.Where(u => u.IsDisabled);
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 20d)));
        return View(new UserListModel { Search = search, Role = role, Status = status, Total = total, Page = page,
            Users = await query.OrderByDescending(u => u.JoinedDate).ThenBy(u => u.Id).Skip((page - 1) * 20).Take(20).ToListAsync() });
    }

    [HttpGet]
    public IActionResult CreateUser() => View(new CreateUserModel());

    [HttpPost]
    public async Task<IActionResult> CreateUser(CreateUserModel input)
    {
        if (!AccountRoles.All.Contains(input.Role)) ModelState.AddModelError(nameof(input.Role), "Choose a valid role.");
        if (!ModelState.IsValid) return View(input);
        var user = new ApplicationUser { UserName = input.Email.Trim(), Email = input.Email.Trim(), DisplayName = input.DisplayName.Trim() };
        var result = await accounts.CreateAsync(user, input.Password, input.Role);
        if (!result.Succeeded) { AddErrors(result); return View(input); }
        TempData["Success"] = $"Account created for {user.Email}.";
        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public async Task<IActionResult> EditUser(string id)
    {
        var row = await accounts.Query().SingleOrDefaultAsync(u => u.Id == id);
        return row is null ? NotFound() : View(new EditUserModel { Id = row.Id, DisplayName = row.DisplayName,
            Email = row.Email, Role = row.Role, IsDisabled = row.IsDisabled });
    }

    [HttpPost]
    public async Task<IActionResult> EditUser(string id, EditUserModel input)
    {
        input.Id = id;
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (!ModelState.IsValid) return View(input);
        try
        {
            var result = await accounts.UpdateAsync(user, input, users.GetUserId(User)!);
            if (!result.Succeeded) { AddErrors(result); return View(input); }
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError("", "This account changed or has linked content. Reload the page and check its course ownership before trying again.");
            return View(input);
        }
        if (id == users.GetUserId(User)) await signIn.RefreshSignInAsync(user);
        TempData["Success"] = "Account updated. Role and status changes revoke existing sessions.";
        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var row = await accounts.Query().SingleOrDefaultAsync(u => u.Id == id);
        return row is null ? NotFound() : View(row);
    }

    [HttpPost, ActionName("DeleteUser")]
    public async Task<IActionResult> DeleteUserConfirmed(string id, string? emailConfirmation)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (!string.Equals(emailConfirmation?.Trim(), user.Email, StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError("", "Enter the account's email address to confirm deletion.");
        if (ModelState.IsValid)
        {
            try
            {
                var result = await accounts.DeleteAsync(user, users.GetUserId(User)!);
                if (result.Succeeded) { TempData["Success"] = "Account deleted."; return RedirectToAction(nameof(Users)); }
                AddErrors(result);
            }
            catch (DbUpdateException) { ModelState.AddModelError("", "This account has linked records that prevent deletion. Disable it to preserve its content."); }
        }
        return View("DeleteUser", await accounts.Query().SingleAsync(u => u.Id == id));
    }

    public async Task<IActionResult> Courses(string? search, string? status, int page = 1)
    {
        var query = from c in db.Courses.AsNoTracking()
                    join teacher in db.Users on c.TeacherId equals teacher.Id
                    select new CourseRow { CourseId = c.CourseId, Title = c.Title, Category = c.Category,
                        IsPublished = c.IsPublished, Teacher = teacher.DisplayName,
                        Enrollments = db.Enrollments.Count(e => e.CourseId == c.CourseId) };
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search)) query = query.Where(c => c.Title.Contains(search) || c.Teacher.Contains(search));
        if (status == "published") query = query.Where(c => c.IsPublished);
        if (status == "draft") query = query.Where(c => !c.IsPublished);
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 20d)));
        return View(new CourseListModel { Search = search, Status = status, Page = page, Total = total,
            Courses = await query.OrderBy(c => c.Title).ThenBy(c => c.CourseId).Skip((page - 1) * 20).Take(20).ToListAsync() });
    }

    [HttpGet]
    public async Task<IActionResult> EditCourse(int id)
    {
        var course = await db.Courses.AsNoTracking().SingleOrDefaultAsync(c => c.CourseId == id);
        if (course is null) return NotFound();
        return View(new EditCourseModel { CourseId = id, Title = course.Title, Description = course.Description,
            Category = course.Category, IsPublished = course.IsPublished, Teacher = await TeacherName(course.TeacherId) });
    }

    [HttpPost]
    public async Task<IActionResult> EditCourse(int id, EditCourseModel input)
    {
        var course = await db.Courses.SingleOrDefaultAsync(c => c.CourseId == id);
        if (course is null) return NotFound();
        input.CourseId = id;
        input.Teacher = await TeacherName(course.TeacherId);
        if (!ModelState.IsValid) return View(input);
        course.Title = input.Title.Trim();
        course.Description = input.Description?.Trim();
        course.Category = input.Category?.Trim();
        course.IsPublished = input.IsPublished;
        await db.SaveChangesAsync();
        TempData["Success"] = "Course details and publication status updated.";
        return RedirectToAction(nameof(Courses));
    }

    [HttpGet]
    public async Task<IActionResult> DeleteCourse(int id)
    {
        var course = await db.Courses.AsNoTracking().SingleOrDefaultAsync(c => c.CourseId == id);
        return course is null ? NotFound() : View(course);
    }

    [HttpPost, ActionName("DeleteCourse")]
    public async Task<IActionResult> DeleteCourseConfirmed(int id)
    {
        var course = await db.Courses.SingleOrDefaultAsync(c => c.CourseId == id);
        if (course is null) return NotFound();
        var hasLearningData = await db.Enrollments.AnyAsync(e => e.CourseId == id)
            || await db.ChallengeSubmissions.AnyAsync(s => db.Challenges.Any(c => c.ChallengeId == s.ChallengeId && c.CourseId == id));
        if (hasLearningData) ModelState.AddModelError("", "This course has learning records. Unpublish it to preserve students' work.");
        if (ModelState.IsValid)
        {
            try
            {
                db.Courses.Remove(course);
                await db.SaveChangesAsync();
                TempData["Success"] = "Course and its unused learning activities deleted.";
                return RedirectToAction(nameof(Courses));
            }
            catch (DbUpdateException) { ModelState.AddModelError("", "Linked learning records prevent deletion. Unpublish this course instead."); }
        }
        return View("DeleteCourse", course);
    }

    private async Task<string> TeacherName(string id) => await db.Users.Where(u => u.Id == id).Select(u => u.DisplayName).FirstOrDefaultAsync() ?? "Unknown teacher";
    private void AddErrors(IdentityResult result)
    {
        foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
    }
}
