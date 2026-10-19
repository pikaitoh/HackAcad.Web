using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

[Authorize(Roles = AccountRoles.Teacher)]
public class TeacherCoursesController(
    ApplicationDbContext db,
    IWebHostEnvironment environment) : Controller
{
    // ==========================================
    // 1. TEACHER STUDIO DASHBOARD
    // ==========================================
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var courses = await db.Courses
            .Where(c => c.TeacherId == teacherId)
            .OrderByDescending(c => c.CreatedDate)
            .ToListAsync();

        var rows = new List<TeacherCourseRow>();
        var totalStudents = 0;
        var totalSubmissions = 0;

        foreach (var c in courses)
        {
            var lessonCount = await db.Lessons.CountAsync(l => l.CourseId == c.CourseId);
            var challengeCount = await db.Challenges.CountAsync(ch => ch.CourseId == c.CourseId);
            var simulationCount = await db.SimulationTasks.CountAsync(s => s.CourseId == c.CourseId);

            var enrolledCount = await db.Enrollments.CountAsync(e => e.CourseId == c.CourseId);
            var completedCount = await db.Enrollments.CountAsync(e => e.CourseId == c.CourseId && e.Status == "Completed");

            var challengeIds = await db.Challenges
                .Where(ch => ch.CourseId == c.CourseId)
                .Select(ch => ch.ChallengeId)
                .ToListAsync();

            var submissionsInCourse = await db.ChallengeSubmissions
                .CountAsync(s => challengeIds.Contains(s.ChallengeId));

            totalStudents += enrolledCount;
            totalSubmissions += submissionsInCourse;

            rows.Add(new TeacherCourseRow
            {
                Course = c,
                LessonCount = lessonCount,
                ChallengeCount = challengeCount,
                SimulationCount = simulationCount,
                EnrolledStudentsCount = enrolledCount,
                CompletedStudentsCount = completedCount
            });
        }

        var model = new TeacherDashboardViewModel
        {
            Courses = rows,
            TotalStudentsEnrolled = totalStudents,
            TotalSubmissionsReceived = totalSubmissions
        };

        return View(model);
    }

    // ==========================================
    // 2. CREATE COURSE
    // ==========================================
    [HttpGet]
    public IActionResult Create()
    {
        return View(new CourseCreateEditModel
        {
            IsPublished = true,
            Category = "Web Security"
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CourseCreateEditModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var course = new Course
        {
            Title = model.Title.Trim(),
            Description = model.Description?.Trim(),
            Category = model.Category?.Trim(),
            ThumbnailUrl = model.ThumbnailUrl?.Trim(),
            IsPublished = model.IsPublished,
            TeacherId = teacherId,
            CreatedDate = DateTime.UtcNow
        };

        db.Courses.Add(course);
        await db.SaveChangesAsync();

        TempData["Success"] = $"Course \"{course.Title}\" created! Now add lessons, CTF challenges, and simulations.";
        return RedirectToAction(nameof(Curriculum), new { id = course.CourseId });
    }

    // ==========================================
    // 3. EDIT COURSE
    // ==========================================
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == id && c.TeacherId == teacherId);
        if (course == null) return NotFound("Course not found or you do not own it.");

        var model = new CourseCreateEditModel
        {
            CourseId = course.CourseId,
            Title = course.Title,
            Description = course.Description,
            Category = course.Category,
            ThumbnailUrl = course.ThumbnailUrl,
            IsPublished = course.IsPublished
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CourseCreateEditModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == model.CourseId && c.TeacherId == teacherId);
        if (course == null) return NotFound("Course not found or you do not own it.");

        course.Title = model.Title.Trim();
        course.Description = model.Description?.Trim();
        course.Category = model.Category?.Trim();
        course.ThumbnailUrl = model.ThumbnailUrl?.Trim();
        course.IsPublished = model.IsPublished;

        await db.SaveChangesAsync();
        TempData["Success"] = "Course settings updated successfully.";
        return RedirectToAction(nameof(Curriculum), new { id = course.CourseId });
    }

    // ==========================================
    // 4. CURRICULUM MANAGEMENT HUB
    // ==========================================
    [HttpGet]
    public async Task<IActionResult> Curriculum(int id)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == id && c.TeacherId == teacherId);
        if (course == null) return NotFound("Course not found or you do not own it.");

        var lessons = await db.Lessons.Where(l => l.CourseId == id).OrderBy(l => l.OrderIndex).ToListAsync();
        var challenges = await db.Challenges.Where(c => c.CourseId == id).ToListAsync();
        var simulations = await db.SimulationTasks.Where(s => s.CourseId == id).ToListAsync();

        var enrollments = await db.Enrollments.Where(e => e.CourseId == id).ToListAsync();
        var studentIds = enrollments.Select(e => e.UserId).Distinct().ToList();
        var studentUsers = await db.Users.Where(u => studentIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id);

        var studentRows = new List<StudentCourseActivityRow>();
        var totalItems = lessons.Count + challenges.Count + simulations.Count;

        foreach (var enrollment in enrollments)
        {
            studentUsers.TryGetValue(enrollment.UserId, out var user);
            var completedCount = await db.ProgressRecords
                .CountAsync(p => p.EnrollmentId == enrollment.EnrollmentId && p.IsCompleted);

            var pct = totalItems > 0 ? (int)Math.Round((double)completedCount / totalItems * 100) : 0;

            studentRows.Add(new StudentCourseActivityRow
            {
                StudentId = enrollment.UserId,
                StudentName = user?.DisplayName ?? "Student",
                StudentEmail = user?.Email ?? "",
                EnrolledDate = enrollment.EnrolledDate,
                ProgressPercent = Math.Clamp(pct, 0, 100),
                Status = enrollment.Status
            });
        }

        var challengeIds = challenges.Select(c => c.ChallengeId).ToList();
        var recentSubmissions = await db.ChallengeSubmissions
            .Where(s => challengeIds.Contains(s.ChallengeId))
            .OrderByDescending(s => s.SubmittedDate)
            .Take(15)
            .ToListAsync();

        var model = new CourseCurriculumViewModel
        {
            Course = course,
            Lessons = lessons,
            Challenges = challenges,
            SimulationTasks = simulations,
            EnrolledStudents = studentRows,
            RecentSubmissions = recentSubmissions
        };

        return View(model);
    }

    // ==========================================
    // 5. LESSONS AUTHORING
    // ==========================================
    [HttpGet]
    public async Task<IActionResult> AddLesson(int courseId)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == courseId && c.TeacherId == teacherId);
        if (course == null) return NotFound("Course not found.");

        var nextOrder = await db.Lessons.Where(l => l.CourseId == courseId).CountAsync() + 1;

        return View("LessonForm", new LessonCreateEditModel
        {
            CourseId = courseId,
            OrderIndex = nextOrder
        });
    }

    [HttpGet]
    public async Task<IActionResult> EditLesson(int id)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var lesson = await db.Lessons.FindAsync(id);
        if (lesson == null) return NotFound("Lesson not found.");

        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == lesson.CourseId && c.TeacherId == teacherId);
        if (course == null) return NotFound("You do not own this course.");

        return View("LessonForm", new LessonCreateEditModel
        {
            LessonId = lesson.LessonId,
            CourseId = lesson.CourseId,
            Title = lesson.Title,
            Content = lesson.Content,
            OrderIndex = lesson.OrderIndex,
            VideoUrl = lesson.VideoUrl
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLesson(LessonCreateEditModel model)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == model.CourseId && c.TeacherId == teacherId);
        if (course == null) return NotFound("Course not found.");

        if (!ModelState.IsValid) return View("LessonForm", model);

        if (model.LessonId == 0)
        {
            var newLesson = new Lesson
            {
                CourseId = model.CourseId,
                Title = model.Title.Trim(),
                Content = model.Content,
                OrderIndex = model.OrderIndex,
                VideoUrl = model.VideoUrl?.Trim()
            };
            db.Lessons.Add(newLesson);
            TempData["Success"] = $"Lesson \"{newLesson.Title}\" added successfully.";
        }
        else
        {
            var existing = await db.Lessons.FindAsync(model.LessonId);
            if (existing == null || existing.CourseId != model.CourseId) return NotFound("Lesson not found.");

            existing.Title = model.Title.Trim();
            existing.Content = model.Content;
            existing.OrderIndex = model.OrderIndex;
            existing.VideoUrl = model.VideoUrl?.Trim();
            TempData["Success"] = $"Lesson \"{existing.Title}\" updated.";
        }

        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteLesson(int id)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var lesson = await db.Lessons.FindAsync(id);
        if (lesson == null) return NotFound("Lesson not found.");

        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == lesson.CourseId && c.TeacherId == teacherId);
        if (course == null) return NotFound("You do not own this course.");

        var courseId = lesson.CourseId;
        if (await db.ProgressRecords.AnyAsync(p => p.LessonId == id) || await db.Enrollments.AnyAsync(e => e.LastLessonId == id))
        {
            TempData["CourseError"] = "This lesson has student learning records and cannot be deleted. Keep it to preserve their work.";
            return RedirectToAction(nameof(Curriculum), new { id = courseId });
        }
        try
        {
            db.Lessons.Remove(lesson);
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            TempData["CourseError"] = "Linked learning records prevent deletion. Reload the curriculum and try again.";
            return RedirectToAction(nameof(Curriculum), new { id = courseId });
        }

        TempData["Success"] = "Lesson removed from curriculum.";
        return RedirectToAction(nameof(Curriculum), new { id = courseId });
    }

    // ==========================================
    // 6. CTF CHALLENGES AUTHORING
    // ==========================================
    [HttpGet]
    public async Task<IActionResult> AddChallenge(int courseId)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == courseId && c.TeacherId == teacherId);
        if (course == null) return NotFound("Course not found.");

        return View("ChallengeForm", new ChallengeCreateEditModel
        {
            CourseId = courseId,
            Points = 100,
            Category = "Web",
            Difficulty = "Easy",
            IsPublished = true
        });
    }

    [HttpGet]
    public async Task<IActionResult> EditChallenge(int id)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var challenge = await db.Challenges.FindAsync(id);
        if (challenge == null) return NotFound("Challenge not found.");

        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == challenge.CourseId && c.TeacherId == teacherId);
        if (course == null) return NotFound("You do not own this course.");

        return View("ChallengeForm", new ChallengeCreateEditModel
        {
            ChallengeId = challenge.ChallengeId,
            CourseId = challenge.CourseId,
            Title = challenge.Title,
            Category = challenge.Category ?? "Web",
            Difficulty = challenge.Difficulty ?? "Easy",
            Description = challenge.Description,
            Points = challenge.Points,
            ExistingFilePath = challenge.FilePath,
            IsPublished = challenge.IsPublished
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveChallenge(ChallengeCreateEditModel model)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == model.CourseId && c.TeacherId == teacherId);
        if (course == null) return NotFound("Course not found.");

        var existingChallenge = model.ChallengeId == 0 ? null : await db.Challenges.FindAsync(model.ChallengeId);
        if (model.ChallengeId != 0 && (existingChallenge is null || existingChallenge.CourseId != model.CourseId)) return NotFound();
        model.ExistingFilePath = existingChallenge?.FilePath;

        if (model.ChallengeId == 0 && string.IsNullOrWhiteSpace(model.PlaintextFlag))
        {
            ModelState.AddModelError(nameof(model.PlaintextFlag), "A secret flag is required when creating a CTF challenge.");
        }

        if (!ModelState.IsValid) return View("ChallengeForm", model);

        string? uploadedFilePath = existingChallenge?.FilePath;
        if (model.UploadFile is not null)
        {
            try { uploadedFilePath = await CourseFiles.SaveAsync(environment, model.UploadFile); }
            catch (ArgumentException ex)
            {
                ModelState.AddModelError(nameof(model.UploadFile), ex.Message);
                return View("ChallengeForm", model);
            }
        }

        if (model.ChallengeId == 0)
        {
            var flagHash = ComputeSha256(model.PlaintextFlag!.Trim());

            var challenge = new Challenge
            {
                CourseId = model.CourseId,
                Title = model.Title.Trim(),
                Category = model.Category.Trim(),
                Difficulty = model.Difficulty.Trim(),
                Description = model.Description?.Trim(),
                FlagHash = flagHash,
                Points = model.Points,
                FilePath = uploadedFilePath,
                IsPublished = model.IsPublished
            };

            db.Challenges.Add(challenge);
            TempData["Success"] = $"CTF challenge \"{challenge.Title}\" created successfully!";
        }
        else
        {
            var existing = await db.Challenges.FindAsync(model.ChallengeId);
            if (existing == null || existing.CourseId != model.CourseId) return NotFound("Challenge not found.");

            existing.Title = model.Title.Trim();
            existing.Category = model.Category.Trim();
            existing.Difficulty = model.Difficulty.Trim();
            existing.Description = model.Description?.Trim();
            existing.Points = model.Points;
            existing.IsPublished = model.IsPublished;
            existing.FilePath = uploadedFilePath;

            // Only update flag hash if a new plaintext flag was entered
            if (!string.IsNullOrWhiteSpace(model.PlaintextFlag))
            {
                existing.FlagHash = ComputeSha256(model.PlaintextFlag.Trim());
            }

            TempData["Success"] = $"CTF challenge \"{existing.Title}\" updated.";
        }

        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteChallenge(int id)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var challenge = await db.Challenges.FindAsync(id);
        if (challenge == null) return NotFound("Challenge not found.");

        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == challenge.CourseId && c.TeacherId == teacherId);
        if (course == null) return NotFound("You do not own this course.");

        var courseId = challenge.CourseId;
        if (await db.ProgressRecords.AnyAsync(p => p.ChallengeId == id) || await db.ChallengeSubmissions.AnyAsync(s => s.ChallengeId == id))
        {
            TempData["CourseError"] = "This challenge has student learning records and cannot be deleted. Keep it to preserve their work.";
            return RedirectToAction(nameof(Curriculum), new { id = courseId });
        }
        try
        {
            db.Challenges.Remove(challenge);
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            TempData["CourseError"] = "Linked learning records prevent deletion. Reload the curriculum and try again.";
            return RedirectToAction(nameof(Curriculum), new { id = courseId });
        }

        TempData["Success"] = "CTF challenge removed.";
        return RedirectToAction(nameof(Curriculum), new { id = courseId });
    }

    // ==========================================
    // 7. SIMULATION TASKS AUTHORING
    // ==========================================
    [HttpGet]
    public async Task<IActionResult> AddSimulation(int courseId)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == courseId && c.TeacherId == teacherId);
        if (course == null) return NotFound("Course not found.");

        // Supply a default ready-to-run Linux lab template
        var defaultJson = @"{
  ""allowedCommands"": [""help"", ""clear"", ""pwd"", ""ls"", ""cd"", ""cat"", ""grep"", ""whoami"", ""chmod""],
  ""initialDirectory"": ""/home/student"",
  ""fileSystem"": {
    ""home"": {
      ""student"": {
        ""notes.txt"": ""Mission: Find the sensitive audit script in /var/secret and view its secret key."",
        ""readme.md"": ""Welcome to HackAcad simulated terminal environment.""
      }
    },
    ""var"": {
      ""secret"": {
        ""audit.sh"": ""# HackAcad Secret Key\nFLAG_KEY=flag{l1nux_sh3ll_m4st3ry}""
      }
    }
  },
  ""objectives"": [
    { ""id"": ""nav_var"", ""text"": ""Navigate to /var/secret directory"", ""type"": ""cwd"", ""target"": ""/var/secret"" },
    { ""id"": ""view_audit"", ""text"": ""Inspect audit.sh to find the secret key"", ""type"": ""cat"", ""target"": ""audit.sh"" }
  ]
}";

        return View("SimulationForm", new SimulationCreateEditModel
        {
            CourseId = courseId,
            Title = "Linux File Navigation & Secret Inspection",
            Instructions = "Practice basic Linux terminal navigation. Navigate to /var/secret and inspect audit.sh to complete the lab exercise.",
            VirtualFileSystemJson = defaultJson,
            RewardFlag = "flag{l1nux_sh3ll_m4st3ry}",
            IsPublished = true
        });
    }

    [HttpGet]
    public async Task<IActionResult> EditSimulation(int id)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var task = await db.SimulationTasks.FindAsync(id);
        if (task == null) return NotFound("Simulation task not found.");

        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == task.CourseId && c.TeacherId == teacherId);
        if (course == null) return NotFound("You do not own this course.");

        return View("SimulationForm", new SimulationCreateEditModel
        {
            TaskId = task.TaskId,
            CourseId = task.CourseId,
            Title = task.Title,
            Instructions = task.Instructions,
            VirtualFileSystemJson = task.VirtualFileSystemJson,
            RewardFlag = task.RewardFlagHash,
            IsPublished = task.IsPublished
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSimulation(SimulationCreateEditModel model)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == model.CourseId && c.TeacherId == teacherId);
        if (course == null) return NotFound("Course not found.");

        if (!SimulationVerifier.TryConfiguration(model.VirtualFileSystemJson, out _, out var configError))
            ModelState.AddModelError(nameof(model.VirtualFileSystemJson), configError);

        if (!ModelState.IsValid) return View("SimulationForm", model);

        if (model.TaskId == 0)
        {
            var task = new SimulationTask
            {
                CourseId = model.CourseId,
                Title = model.Title.Trim(),
                Instructions = model.Instructions?.Trim(),
                VirtualFileSystemJson = model.VirtualFileSystemJson?.Trim(),
                RewardFlagHash = model.RewardFlag?.Trim(),
                IsPublished = model.IsPublished
            };

            db.SimulationTasks.Add(task);
            TempData["Success"] = $"Simulation task \"{task.Title}\" created successfully!";
        }
        else
        {
            var existing = await db.SimulationTasks.FindAsync(model.TaskId);
            if (existing == null || existing.CourseId != model.CourseId) return NotFound("Task not found.");

            existing.Title = model.Title.Trim();
            existing.Instructions = model.Instructions?.Trim();
            existing.VirtualFileSystemJson = model.VirtualFileSystemJson?.Trim();
            existing.RewardFlagHash = model.RewardFlag?.Trim();
            existing.IsPublished = model.IsPublished;

            TempData["Success"] = $"Simulation task \"{existing.Title}\" updated.";
        }

        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSimulation(int id)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var task = await db.SimulationTasks.FindAsync(id);
        if (task == null) return NotFound("Simulation task not found.");

        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == task.CourseId && c.TeacherId == teacherId);
        if (course == null) return NotFound("You do not own this course.");

        var courseId = task.CourseId;
        if (await db.ProgressRecords.AnyAsync(p => p.SimulationTaskId == id))
        {
            TempData["CourseError"] = "This lab has student learning records and cannot be deleted. Keep it to preserve their work.";
            return RedirectToAction(nameof(Curriculum), new { id = courseId });
        }
        try
        {
            db.SimulationTasks.Remove(task);
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            TempData["CourseError"] = "Linked learning records prevent deletion. Reload the curriculum and try again.";
            return RedirectToAction(nameof(Curriculum), new { id = courseId });
        }

        TempData["Success"] = "Simulation lab task removed.";
        return RedirectToAction(nameof(Curriculum), new { id = courseId });
    }

    // ==========================================
    // 8. PUBLISH / UNPUBLISH TOGGLE & DELETE COURSE
    // ==========================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePublish(int id)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == id && c.TeacherId == teacherId);
        if (course == null) return NotFound();

        course.IsPublished = !course.IsPublished;
        await db.SaveChangesAsync();

        TempData["Success"] = course.IsPublished
            ? $"Course \"{course.Title}\" is now published to the public catalogue!"
            : $"Course \"{course.Title}\" is now hidden (draft mode).";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == id && c.TeacherId == teacherId);
        if (course == null) return NotFound();

        var hasEnrollments = await db.Enrollments.AnyAsync(e => e.CourseId == id);
        if (hasEnrollments)
        {
            TempData["FlagError"] = "Cannot delete a course with active student enrolments. You can unpublish it instead.";
            return RedirectToAction(nameof(Index));
        }

        db.Courses.Remove(course);
        await db.SaveChangesAsync();

        TempData["Success"] = $"Course \"{course.Title}\" deleted.";
        return RedirectToAction(nameof(Index));
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
