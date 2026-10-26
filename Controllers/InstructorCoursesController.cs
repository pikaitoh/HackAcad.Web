using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

[Authorize]
public class InstructorCoursesController(
    ApplicationDbContext db,
    IWebHostEnvironment environment,
    NotebookExportService exportService) : Controller
{
    // ==========================================
    // 1. INSTRUCTOR STUDIO DASHBOARD
    // ==========================================
    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var courses = await db.Courses
            .Where(c => c.InstructorId == instructorId)
            .OrderByDescending(c => c.CreatedDate)
            .ToListAsync();

        var rows = new List<InstructorCourseRow>();
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

            rows.Add(new InstructorCourseRow
            {
                Course = c,
                LessonCount = lessonCount,
                ChallengeCount = challengeCount,
                SimulationCount = simulationCount,
                EnrolledStudentsCount = enrolledCount,
                CompletedStudentsCount = completedCount
            });
        }

        var model = new InstructorDashboardViewModel
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
    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpGet]
    public IActionResult Create()
    {
        return View(new CourseCreateEditModel
        {
            IsPublished = true,
            Category = "Web Security"
        });
    }

    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CourseCreateEditModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var course = new Course
        {
            Title = model.Title.Trim(),
            Description = model.Description?.Trim(),
            Category = model.Category?.Trim(),
            ThumbnailUrl = model.ThumbnailUrl?.Trim(),
            IsPublished = model.IsPublished,
            InstructorId = instructorId,
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
    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == id && c.InstructorId == instructorId);
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

    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CourseCreateEditModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == model.CourseId && c.InstructorId == instructorId);
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
    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpGet]
    public async Task<IActionResult> Curriculum(int id)
    {
        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == id && c.InstructorId == instructorId);
        if (course == null) return NotFound("Course not found or you do not own it.");

        var lessons = await db.Lessons.Where(l => l.CourseId == id).OrderBy(l => l.OrderIndex).ToListAsync();
        var challenges = await db.Challenges.Where(c => c.CourseId == id).ToListAsync();
        var simulations = await db.SimulationTasks.Where(s => s.CourseId == id).ToListAsync();
        var courseNotebooks = await db.CourseNotebooks
            .Where(nb => nb.CourseId == id)
            .OrderBy(nb => nb.OrderIndex)
            .ThenBy(nb => nb.CourseNotebookId)
            .ToListAsync();

        var personalNotebooks = await db.Notebooks
            .Where(n => n.UserId == instructorId)
            .OrderByDescending(n => n.UpdatedAtUtc)
            .Select(n => new { n.NotebookId, n.Title })
            .ToListAsync();
        ViewBag.PersonalNotebooks = personalNotebooks;

        var enrollments = await db.Enrollments.Where(e => e.CourseId == id).ToListAsync();
        var studentIds = enrollments.Select(e => e.UserId).Distinct().ToList();
        var studentUsers = await db.Users.Where(u => studentIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id);
        var enrollmentIds = enrollments.Select(e => e.EnrollmentId).ToList();
        var lessonIds = lessons.Select(l => l.LessonId).ToHashSet();
        var publishedChallengeIds = challenges.Where(c => c.IsPublished).Select(c => c.ChallengeId).ToHashSet();
        var publishedLabIds = simulations.Where(s => s.IsPublished).Select(s => s.TaskId).ToHashSet();

        // Count only activities students can complete and show their latest activity.
        var completedProgress = await db.ProgressRecords.AsNoTracking()
            .Where(p => enrollmentIds.Contains(p.EnrollmentId)
                && p.IsCompleted
                && ((p.ContentType == ProgressContentType.Lesson
                        && p.LessonId.HasValue
                        && lessonIds.Contains(p.LessonId.Value))
                    || (p.ContentType == ProgressContentType.Challenge
                        && p.ChallengeId.HasValue
                        && publishedChallengeIds.Contains(p.ChallengeId.Value))
                    || (p.ContentType == ProgressContentType.Simulation
                        && p.SimulationTaskId.HasValue
                        && publishedLabIds.Contains(p.SimulationTaskId.Value))))
            .Select(p => new
            {
                p.EnrollmentId,
                p.ContentType,
                p.LessonId,
                p.ChallengeId,
                p.SimulationTaskId,
                p.LastUpdatedDate
            })
            .ToListAsync();
        var progressByEnrollment = completedProgress.GroupBy(p => p.EnrollmentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var studentRows = new List<StudentCourseActivityRow>();
        var totalItems = lessons.Count + publishedChallengeIds.Count + publishedLabIds.Count;

        foreach (var enrollment in enrollments)
        {
            studentUsers.TryGetValue(enrollment.UserId, out var user);
            progressByEnrollment.TryGetValue(enrollment.EnrollmentId, out var activityDates);
            var completedCount = activityDates?
                .DistinctBy(p => (p.ContentType, p.LessonId, p.ChallengeId, p.SimulationTaskId))
                .Count() ?? 0;
            var latestActivityUtc = activityDates?.Count > 0
                ? activityDates.Max(p => p.LastUpdatedDate)
                : (DateTime?)null;

            var pct = totalItems > 0 ? (int)Math.Round((double)completedCount / totalItems * 100) : 0;
            var elapsedCourseTime = latestActivityUtc.HasValue
                ? latestActivityUtc.Value - enrollment.EnrolledDate
                : (TimeSpan?)null;
            if (elapsedCourseTime < TimeSpan.Zero) elapsedCourseTime = TimeSpan.Zero;

            studentRows.Add(new StudentCourseActivityRow
            {
                StudentId = enrollment.UserId,
                StudentName = user?.DisplayName ?? "Student",
                StudentEmail = user?.Email ?? "",
                EnrolledDate = enrollment.EnrolledDate,
                ProgressPercent = Math.Clamp(pct, 0, 100),
                ElapsedCourseTime = elapsedCourseTime,
                LatestActivityUtc = latestActivityUtc,
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
            CourseNotebooks = courseNotebooks,
            EnrolledStudents = studentRows,
            RecentSubmissions = recentSubmissions
        };

        return View(model);
    }

    // ==========================================
    // 5. LESSONS AUTHORING
    // ==========================================
    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpGet]
    public async Task<IActionResult> AddLesson(int courseId)
    {
        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == courseId && c.InstructorId == instructorId);
        if (course == null) return NotFound("Course not found.");

        var nextOrder = await db.Lessons.Where(l => l.CourseId == courseId).CountAsync() + 1;

        return View("LessonForm", new LessonCreateEditModel
        {
            CourseId = courseId,
            OrderIndex = nextOrder
        });
    }

    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpGet]
    public async Task<IActionResult> EditLesson(int id)
    {
        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var lesson = await db.Lessons.FindAsync(id);
        if (lesson == null) return NotFound("Lesson not found.");

        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == lesson.CourseId && c.InstructorId == instructorId);
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

    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLesson(LessonCreateEditModel model)
    {
        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == model.CourseId && c.InstructorId == instructorId);
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

    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteLesson(int id)
    {
        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var lesson = await db.Lessons.FindAsync(id);
        if (lesson == null) return NotFound("Lesson not found.");

        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == lesson.CourseId && c.InstructorId == instructorId);
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
    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpGet]
    public async Task<IActionResult> AddChallenge(int courseId)
    {
        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == courseId && c.InstructorId == instructorId);
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

    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpGet]
    public async Task<IActionResult> EditChallenge(int id)
    {
        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var challenge = await db.Challenges.FindAsync(id);
        if (challenge == null) return NotFound("Challenge not found.");

        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == challenge.CourseId && c.InstructorId == instructorId);
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

    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveChallenge(ChallengeCreateEditModel model)
    {
        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == model.CourseId && c.InstructorId == instructorId);
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

    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteChallenge(int id)
    {
        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var challenge = await db.Challenges.FindAsync(id);
        if (challenge == null) return NotFound("Challenge not found.");

        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == challenge.CourseId && c.InstructorId == instructorId);
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
    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpGet]
    public async Task<IActionResult> AddSimulation(int courseId)
    {
        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == courseId && c.InstructorId == instructorId);
        if (course == null) return NotFound("Course not found.");

        // Predefined Linux filesystem template containing common directories (/home/student, /etc, /tmp, /var, /var/log)
        var defaultJson = @"{
  ""description"": ""Explore basic Linux filesystem navigation, inspect log files, and verify commands."",
  ""difficulty"": ""Beginner"",
  ""allowedCommands"": [""help"", ""clear"", ""pwd"", ""ls"", ""cd"", ""cat"", ""grep"", ""whoami"", ""chmod""],
  ""initialDirectory"": ""/home/student"",
  ""fileSystem"": {
    ""home"": {
      ""student"": {
        ""notes.txt"": ""Mission: Inspect system configuration and find the access token in /var/log/auth.log."",
        ""welcome.txt"": ""Welcome to the HackAcad Linux environment.\nUse ls to inspect files and cat to read them.""
      }
    },
    ""etc"": {
      ""motd"": ""HackAcad Virtual Security Lab v2.0\nAuthorized access only."",
      ""hostname"": ""hackacad-sandbox""
    },
    ""tmp"": {
      ""sample.tmp"": ""Temporary session data.""
    },
    ""var"": {
      ""log"": {
        ""syslog"": ""systemd[1]: Started HackAcad Lab Environment."",
        ""auth.log"": ""Oct 18 10:08:14 server audit[1400]: FLAG_TOKEN=flag{l1nux_sh3ll_m4st3ry}""
      }
    }
  },
  ""objectives"": [
    { ""id"": ""nav_log"", ""text"": ""Navigate to /var/log directory"", ""type"": ""cwd"", ""target"": ""/var/log"", ""hint"": ""Run 'cd /var/log'"" },
    { ""id"": ""read_auth"", ""text"": ""Inspect auth.log using cat to find the secret token"", ""type"": ""cat"", ""target"": ""auth.log"", ""hint"": ""Run 'cat auth.log'"" }
  ]
}";

        return View("SimulationForm", new SimulationCreateEditModel
        {
            CourseId = courseId,
            Title = "Linux File Navigation & Secret Inspection",
            Description = "Explore basic Linux filesystem navigation, inspect log files, and verify commands.",
            Difficulty = "Beginner",
            Instructions = "Practice basic Linux terminal navigation. Navigate to /var/log and inspect auth.log to complete the lab exercise.",
            VirtualFileSystemJson = defaultJson,
            RewardFlag = "flag{l1nux_sh3ll_m4st3ry}",
            IsPublished = true
        });
    }

    [HttpGet]
    public async Task<IActionResult> EditSimulation(int? id, int? notebookId, int? courseNotebookId, string? blockId)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // 1. Personal notebook terminal block mode (Student or Instructor)
        if (notebookId.HasValue && notebookId.Value > 0)
        {
            var notebook = await db.Notebooks
                .FirstOrDefaultAsync(n => n.NotebookId == notebookId.Value && n.UserId == currentUserId);
            if (notebook == null) return NotFound("Notebook not found or you do not have permission.");

            NotebookDocument doc;
            try
            {
                doc = JsonSerializer.Deserialize<NotebookDocument>(notebook.ContentJson) ?? new NotebookDocument();
            }
            catch
            {
                doc = new NotebookDocument();
            }

            var block = doc.Blocks.FirstOrDefault(b => b.Id == blockId);
            if (block == null) return NotFound("Terminal block not found.");

            var vfsObj = new
            {
                allowedCommands = block.AllowedCommands ?? ["help", "clear", "pwd", "ls", "cd", "cat", "whoami", "chmod", "echo", "history", "date", "id", "touch", "nano"],
                initialDirectory = block.InitialDirectory ?? "/home/student",
                fileSystem = block.FileSystem ?? new Dictionary<string, object>
                {
                    ["home"] = new Dictionary<string, object>
                    {
                        ["student"] = new Dictionary<string, object>
                        {
                            ["notes.txt"] = "Welcome to your personal terminal sandbox.\nPractice commands here freely."
                        }
                    }
                },
                customCommands = block.CustomCommands ?? []
            };

            var returnUrl = Url.Action("Edit", "Notebooks", new { id = notebook.NotebookId });
            var model = new SimulationCreateEditModel
            {
                NotebookId = notebook.NotebookId,
                BlockId = block.Id,
                Title = block.Title ?? "Interactive Terminal Demonstration",
                Description = "Personal interactive sandbox block.",
                Difficulty = "Beginner",
                VirtualFileSystemJson = JsonSerializer.Serialize(vfsObj, new JsonSerializerOptions { WriteIndented = true }),
                IsPublished = true,
                ReturnUrl = returnUrl
            };

            return View("SimulationForm", model);
        }

        // 2. Course notebook terminal block mode (Instructor only)
        if (courseNotebookId.HasValue && courseNotebookId.Value > 0)
        {
            if (!User.IsInRole(AccountRoles.Instructor)) return Forbid();

            var courseNotebook = await db.CourseNotebooks
                .FirstOrDefaultAsync(cn => cn.CourseNotebookId == courseNotebookId.Value);
            if (courseNotebook == null) return NotFound("Course notebook not found.");

            var isOwner = await db.Courses.AnyAsync(c => c.CourseId == courseNotebook.CourseId && c.InstructorId == currentUserId);
            if (!isOwner) return Forbid();

            NotebookDocument doc;
            try
            {
                doc = JsonSerializer.Deserialize<NotebookDocument>(courseNotebook.ContentJson) ?? new NotebookDocument();
            }
            catch
            {
                doc = new NotebookDocument();
            }

            var block = doc.Blocks.FirstOrDefault(b => b.Id == blockId);
            if (block == null) return NotFound("Terminal block not found.");

            var vfsObj = new
            {
                allowedCommands = block.AllowedCommands ?? ["help", "clear", "pwd", "ls", "cd", "cat", "whoami", "chmod", "echo", "history", "date", "id", "touch", "nano"],
                initialDirectory = block.InitialDirectory ?? "/home/student",
                fileSystem = block.FileSystem ?? new Dictionary<string, object>
                {
                    ["home"] = new Dictionary<string, object>
                    {
                        ["student"] = new Dictionary<string, object>
                        {
                            ["welcome.txt"] = "Welcome to the course demonstration sandbox."
                        }
                    }
                },
                customCommands = block.CustomCommands ?? []
            };

            var returnUrl = Url.Action("EditCourseNotebook", "InstructorCourses", new { id = courseNotebook.CourseNotebookId });
            var model = new SimulationCreateEditModel
            {
                CourseId = courseNotebook.CourseId,
                CourseNotebookId = courseNotebook.CourseNotebookId,
                BlockId = block.Id,
                Title = block.Title ?? "Interactive Course Terminal Sandbox",
                Description = "Course interactive sandbox block.",
                Difficulty = "Beginner",
                VirtualFileSystemJson = JsonSerializer.Serialize(vfsObj, new JsonSerializerOptions { WriteIndented = true }),
                IsPublished = true,
                ReturnUrl = returnUrl
            };

            return View("SimulationForm", model);
        }

        // 3. Course simulation task mode (Instructor only)
        if (!User.IsInRole(AccountRoles.Instructor)) return Forbid();

        if (!id.HasValue || id.Value <= 0) return BadRequest("Task ID is required.");

        var task = await db.SimulationTasks.FindAsync(id.Value);
        if (task == null) return NotFound("Simulation task not found.");

        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == task.CourseId && c.InstructorId == currentUserId);
        if (course == null) return NotFound("You do not own this course.");

        string? description = null;
        string? difficulty = null;
        if (!string.IsNullOrWhiteSpace(task.VirtualFileSystemJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(task.VirtualFileSystemJson);
                if (doc.RootElement.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String)
                    description = d.GetString();
                if (doc.RootElement.TryGetProperty("difficulty", out var diff) && diff.ValueKind == JsonValueKind.String)
                    difficulty = diff.GetString();
            }
            catch { }
        }

        return View("SimulationForm", new SimulationCreateEditModel
        {
            TaskId = task.TaskId,
            CourseId = task.CourseId,
            Title = task.Title,
            Description = description,
            Difficulty = difficulty ?? "Beginner",
            Instructions = task.Instructions,
            VirtualFileSystemJson = task.VirtualFileSystemJson,
            RewardFlag = task.RewardFlagHash,
            IsPublished = task.IsPublished,
            ReturnUrl = Url.Action("Curriculum", "InstructorCourses", new { id = task.CourseId })
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSimulation(SimulationCreateEditModel model)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // 1. Personal notebook terminal block mode (Student or Instructor)
        if (model.NotebookId.HasValue && model.NotebookId.Value > 0)
        {
            var notebook = await db.Notebooks
                .FirstOrDefaultAsync(n => n.NotebookId == model.NotebookId.Value && n.UserId == currentUserId);
            if (notebook == null) return NotFound("Notebook not found or you do not have permission.");

            NotebookDocument doc;
            try
            {
                doc = JsonSerializer.Deserialize<NotebookDocument>(notebook.ContentJson) ?? new NotebookDocument();
            }
            catch
            {
                doc = new NotebookDocument();
            }

            var block = doc.Blocks.FirstOrDefault(b => b.Id == model.BlockId);
            if (block == null) return NotFound("Terminal block not found.");

            if (!string.IsNullOrWhiteSpace(model.VirtualFileSystemJson))
            {
                try
                {
                    using var jdoc = JsonDocument.Parse(model.VirtualFileSystemJson);
                    var root = jdoc.RootElement;
                    if (root.TryGetProperty("initialDirectory", out var initEl) && initEl.ValueKind == JsonValueKind.String)
                        block.InitialDirectory = initEl.GetString();
                    if (root.TryGetProperty("allowedCommands", out var cmdEl) && cmdEl.ValueKind == JsonValueKind.Array)
                        block.AllowedCommands = JsonSerializer.Deserialize<List<string>>(cmdEl.GetRawText());
                    if (root.TryGetProperty("fileSystem", out var fsEl) && fsEl.ValueKind == JsonValueKind.Object)
                        block.FileSystem = JsonSerializer.Deserialize<object>(fsEl.GetRawText());
                    if (root.TryGetProperty("customCommands", out var ccEl) && ccEl.ValueKind == JsonValueKind.Array)
                        block.CustomCommands = JsonSerializer.Deserialize<List<NotebookCustomCommand>>(ccEl.GetRawText());
                }
                catch (JsonException ex)
                {
                    ModelState.AddModelError(nameof(model.VirtualFileSystemJson), "Invalid JSON format: " + ex.Message);
                    return View("SimulationForm", model);
                }
            }

            if (!string.IsNullOrWhiteSpace(model.Title))
                block.Title = model.Title.Trim();

            notebook.ContentJson = JsonSerializer.Serialize(doc);
            notebook.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();

            TempData["Success"] = "Terminal block configuration applied to notebook.";
            var retUrl = !string.IsNullOrWhiteSpace(model.ReturnUrl)
                ? model.ReturnUrl
                : Url.Action("Edit", "Notebooks", new { id = notebook.NotebookId });
            return Redirect(retUrl!);
        }

        // 2. Course notebook terminal block mode (Instructor only)
        if (model.CourseNotebookId.HasValue && model.CourseNotebookId.Value > 0)
        {
            if (!User.IsInRole(AccountRoles.Instructor)) return Forbid();

            var courseNotebook = await db.CourseNotebooks
                .FirstOrDefaultAsync(cn => cn.CourseNotebookId == model.CourseNotebookId.Value);
            if (courseNotebook == null) return NotFound("Course notebook not found.");

            var isOwner = await db.Courses.AnyAsync(c => c.CourseId == courseNotebook.CourseId && c.InstructorId == currentUserId);
            if (!isOwner) return Forbid();

            NotebookDocument doc;
            try
            {
                doc = JsonSerializer.Deserialize<NotebookDocument>(courseNotebook.ContentJson) ?? new NotebookDocument();
            }
            catch
            {
                doc = new NotebookDocument();
            }

            var block = doc.Blocks.FirstOrDefault(b => b.Id == model.BlockId);
            if (block == null) return NotFound("Terminal block not found.");

            if (!string.IsNullOrWhiteSpace(model.VirtualFileSystemJson))
            {
                try
                {
                    using var jdoc = JsonDocument.Parse(model.VirtualFileSystemJson);
                    var root = jdoc.RootElement;
                    if (root.TryGetProperty("initialDirectory", out var initEl) && initEl.ValueKind == JsonValueKind.String)
                        block.InitialDirectory = initEl.GetString();
                    if (root.TryGetProperty("allowedCommands", out var cmdEl) && cmdEl.ValueKind == JsonValueKind.Array)
                        block.AllowedCommands = JsonSerializer.Deserialize<List<string>>(cmdEl.GetRawText());
                    if (root.TryGetProperty("fileSystem", out var fsEl) && fsEl.ValueKind == JsonValueKind.Object)
                        block.FileSystem = JsonSerializer.Deserialize<object>(fsEl.GetRawText());
                    if (root.TryGetProperty("customCommands", out var ccEl) && ccEl.ValueKind == JsonValueKind.Array)
                        block.CustomCommands = JsonSerializer.Deserialize<List<NotebookCustomCommand>>(ccEl.GetRawText());
                }
                catch (JsonException ex)
                {
                    ModelState.AddModelError(nameof(model.VirtualFileSystemJson), "Invalid JSON format: " + ex.Message);
                    return View("SimulationForm", model);
                }
            }

            if (!string.IsNullOrWhiteSpace(model.Title))
                block.Title = model.Title.Trim();

            courseNotebook.ContentJson = JsonSerializer.Serialize(doc);
            courseNotebook.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();

            TempData["Success"] = "Terminal block configuration applied to course notebook.";
            var retUrl = !string.IsNullOrWhiteSpace(model.ReturnUrl)
                ? model.ReturnUrl
                : Url.Action("EditCourseNotebook", "InstructorCourses", new { id = courseNotebook.CourseNotebookId });
            return Redirect(retUrl!);
        }

        // 3. Course simulation task mode (Instructor only)
        if (!User.IsInRole(AccountRoles.Instructor)) return Forbid();

        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == model.CourseId && c.InstructorId == currentUserId);
        if (course == null) return NotFound("Course not found.");

        // Inject description and difficulty into JSON root if not present
        var jsonToSave = model.VirtualFileSystemJson;
        if (!string.IsNullOrWhiteSpace(jsonToSave))
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonToSave);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var dict = new Dictionary<string, object>();
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        dict[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText())!;
                    }
                    if (!string.IsNullOrWhiteSpace(model.Description))
                        dict["description"] = model.Description.Trim();
                    if (!string.IsNullOrWhiteSpace(model.Difficulty))
                        dict["difficulty"] = model.Difficulty.Trim();
                    jsonToSave = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
                    model.VirtualFileSystemJson = jsonToSave;
                }
            }
            catch { }
        }

        if (model.IsPublished)
        {
            if (!SimulationVerifier.TryConfiguration(model.VirtualFileSystemJson, out _, out var configError))
                ModelState.AddModelError(nameof(model.VirtualFileSystemJson), configError);
        }
        else
        {
            // For drafts, allow incomplete configuration as long as it parses as a JSON object if provided
            if (!string.IsNullOrWhiteSpace(model.VirtualFileSystemJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(model.VirtualFileSystemJson);
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                        ModelState.AddModelError(nameof(model.VirtualFileSystemJson), "Configuration must be a valid JSON object.");
                }
                catch (JsonException ex)
                {
                    ModelState.AddModelError(nameof(model.VirtualFileSystemJson), $"Invalid JSON format: {ex.Message}");
                }
            }
        }

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
            TempData["Success"] = model.IsPublished 
                ? $"Simulation task \"{task.Title}\" published successfully!" 
                : $"Simulation draft \"{task.Title}\" saved.";
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

            TempData["Success"] = model.IsPublished 
                ? $"Simulation task \"{existing.Title}\" published." 
                : $"Simulation draft \"{existing.Title}\" updated.";
        }

        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
    }

    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSimulation(int id)
    {
        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var task = await db.SimulationTasks.FindAsync(id);
        if (task == null) return NotFound("Simulation task not found.");

        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == task.CourseId && c.InstructorId == instructorId);
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
    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePublish(int id)
    {
        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == id && c.InstructorId == instructorId);
        if (course == null) return NotFound();

        course.IsPublished = !course.IsPublished;
        await db.SaveChangesAsync();

        TempData["Success"] = course.IsPublished
            ? $"Course \"{course.Title}\" is now published to the public catalogue!"
            : $"Course \"{course.Title}\" is now hidden (draft mode).";

        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == id && c.InstructorId == instructorId);
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

    // ==========================================
    // 9. COURSE NOTEBOOKS MANAGEMENT
    // ==========================================
    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddNotebookToCourse(AddNotebookToCourseModel model)
    {
        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.CourseId == model.CourseId && c.InstructorId == instructorId);
        if (course == null) return NotFound("Course not found or unauthorized.");

        string title;
        string contentJson;

        if (model.SelectedNotebookId.HasValue && model.SelectedNotebookId.Value > 0)
        {
            var personalNotebook = await db.Notebooks
                .FirstOrDefaultAsync(n => n.NotebookId == model.SelectedNotebookId.Value && n.UserId == instructorId);
            if (personalNotebook == null)
            {
                TempData["FlagError"] = "Selected personal notebook could not be found or you do not own it.";
                return RedirectToAction(nameof(Curriculum), new { id = model.CourseId });
            }

            title = personalNotebook.Title;
            contentJson = personalNotebook.ContentJson;
        }
        else
        {
            title = string.IsNullOrWhiteSpace(model.NewNotebookTitle) ? "Course Notebook" : model.NewNotebookTitle.Trim();
            if (title.Length > 150) title = title[..150];

            var initialDoc = new NotebookDocument
            {
                Version = 1,
                Blocks =
                [
                    new NotebookBlock
                    {
                        Id = "blk_" + Guid.NewGuid().ToString("n")[..8],
                        Type = "markdown",
                        Content = $"# {title}\n\nWelcome to this course terminal notebook.\nUse the notes and terminal demonstrations below to follow along with the instructor."
                    }
                ]
            };
            contentJson = JsonSerializer.Serialize(initialDoc);
        }

        var maxOrder = await db.CourseNotebooks
            .Where(cn => cn.CourseId == model.CourseId)
            .MaxAsync(cn => (int?)cn.OrderIndex) ?? 0;

        var courseNotebook = new CourseNotebook
        {
            CourseId = model.CourseId,
            Title = title,
            ContentJson = contentJson,
            OrderIndex = maxOrder + 1,
            IsPublished = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        db.CourseNotebooks.Add(courseNotebook);
        await db.SaveChangesAsync();

        TempData["Success"] = $"Added notebook \"{courseNotebook.Title}\" to the course curriculum.";
        return RedirectToAction(nameof(EditCourseNotebook), new { id = courseNotebook.CourseNotebookId });
    }

    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpGet]
    public async Task<IActionResult> EditCourseNotebook(int id)
    {
        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var notebook = await db.CourseNotebooks
            .FirstOrDefaultAsync(cn => cn.CourseNotebookId == id);

        if (notebook == null) return NotFound("Course notebook not found.");

        var isOwner = await db.Courses.AnyAsync(c => c.CourseId == notebook.CourseId && c.InstructorId == instructorId);
        if (!isOwner) return Forbid();

        return View(notebook);
    }

    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCourseNotebook(int id, [FromBody] NotebookSaveModel model)
    {
        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var notebook = await db.CourseNotebooks
            .FirstOrDefaultAsync(cn => cn.CourseNotebookId == id);

        if (notebook == null)
        {
            return NotFound(new { success = false, message = "Course notebook not found." });
        }

        var isOwner = await db.Courses.AnyAsync(c => c.CourseId == notebook.CourseId && c.InstructorId == instructorId);
        if (!isOwner)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(model.Title))
        {
            return BadRequest(new { success = false, message = "Notebook title is required." });
        }

        if (!string.IsNullOrWhiteSpace(model.ContentJson))
        {
            try
            {
                var doc = JsonSerializer.Deserialize<NotebookDocument>(model.ContentJson);
                if (doc == null)
                {
                    return BadRequest(new { success = false, message = "Invalid notebook document format." });
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = "JSON parse error: " + ex.Message });
            }
        }

        notebook.Title = model.Title.Trim();
        if (notebook.Title.Length > 150) notebook.Title = notebook.Title[..150];

        notebook.ContentJson = model.ContentJson ?? "{\"version\":1,\"blocks\":[]}";
        notebook.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            title = notebook.Title,
            updatedAt = notebook.UpdatedAtUtc.ToString("o"),
            message = "Course notebook saved successfully."
        });
    }

    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleCourseNotebookPublish(int id)
    {
        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var notebook = await db.CourseNotebooks
            .FirstOrDefaultAsync(cn => cn.CourseNotebookId == id);

        if (notebook == null) return NotFound();

        var isOwner = await db.Courses.AnyAsync(c => c.CourseId == notebook.CourseId && c.InstructorId == instructorId);
        if (!isOwner) return Forbid();

        notebook.IsPublished = !notebook.IsPublished;
        notebook.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = notebook.IsPublished
            ? $"Course notebook \"{notebook.Title}\" is now published for enrolled students."
            : $"Course notebook \"{notebook.Title}\" is now hidden (draft mode).";

        return RedirectToAction(nameof(Curriculum), new { id = notebook.CourseId });
    }

    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCourseNotebook(int id)
    {
        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var notebook = await db.CourseNotebooks
            .FirstOrDefaultAsync(cn => cn.CourseNotebookId == id);

        if (notebook == null) return NotFound();

        var isOwner = await db.Courses.AnyAsync(c => c.CourseId == notebook.CourseId && c.InstructorId == instructorId);
        if (!isOwner) return Forbid();

        var courseId = notebook.CourseId;
        var title = notebook.Title;
        db.CourseNotebooks.Remove(notebook);
        await db.SaveChangesAsync();

        TempData["Success"] = $"Course notebook \"{title}\" deleted.";
        return RedirectToAction(nameof(Curriculum), new { id = courseId });
    }

    [Authorize(Roles = AccountRoles.Instructor)]
    [HttpGet]
    public async Task<IActionResult> ExportCourseNotebook(int id)
    {
        var instructorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var notebook = await db.CourseNotebooks
            .AsNoTracking()
            .FirstOrDefaultAsync(cn => cn.CourseNotebookId == id);

        if (notebook == null) return NotFound();

        var isOwner = await db.Courses.AnyAsync(c => c.CourseId == notebook.CourseId && c.InstructorId == instructorId);
        if (!isOwner) return Forbid();

        var html = exportService.GenerateStandaloneHtml(notebook.Title, notebook.ContentJson);
        var safeFilename = System.Text.RegularExpressions.Regex.Replace(notebook.Title, @"[^a-zA-Z0-9_\-]+", "_").Trim('_');
        if (string.IsNullOrWhiteSpace(safeFilename)) safeFilename = "course_terminal_notebook";

        return File(Encoding.UTF8.GetBytes(html), "text/html", $"{safeFilename}.html");
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
