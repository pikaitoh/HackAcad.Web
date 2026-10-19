using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

public class CoursesController(
    ApplicationDbContext db,
    IWebHostEnvironment environment, Personalization personalization) : Controller
{
    // ==========================================
    // 1. PUBLIC / STUDENT CATALOGUE
    // ==========================================
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? category)
    {
        if (User.IsInRole(AccountRoles.Admin)) return RedirectToAction("Courses", "Admin");
        if (User.IsInRole(AccountRoles.Instructor)) return RedirectToAction("Index", "InstructorCourses");
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var query = db.Courses.AsNoTracking().Where(c => c.IsPublished);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(c => c.Title.Contains(s) || (c.Description != null && c.Description.Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var cat = category.Trim();
            query = query.Where(c => c.Category == cat);
        }

        var coursesList = await query.OrderByDescending(c => c.CreatedDate).ToListAsync();

        var categories = await db.Courses
            .Where(c => c.IsPublished && c.Category != null)
            .Select(c => c.Category!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

        // Get enrollments for the current user
        var userEnrollments = currentUserId != null
            ? await db.Enrollments.AsNoTracking().Where(e => e.UserId == currentUserId).ToListAsync()
            : [];

        var enrollmentMap = userEnrollments.ToDictionary(e => e.CourseId);

        // Preload instructor display names
        var instructorIds = coursesList.Select(c => c.InstructorId).Distinct().ToList();
        var instructorUsers = await db.Users.AsNoTracking()
            .Where(u => instructorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        var courseCards = new List<CourseCardViewModel>();

        foreach (var c in coursesList)
        {
            var isEnrolled = enrollmentMap.TryGetValue(c.CourseId, out var enrollment);
            var lessonCount = await db.Lessons.CountAsync(l => l.CourseId == c.CourseId);
            var challengeCount = await db.Challenges.CountAsync(ch => ch.CourseId == c.CourseId && ch.IsPublished);
            var simulationCount = await db.SimulationTasks.CountAsync(s => s.CourseId == c.CourseId && s.IsPublished);
            var totalItems = lessonCount + challengeCount + simulationCount;

            var progressPercent = 0;
            if (isEnrolled && totalItems > 0 && enrollment != null)
            {
                var completedCount = await db.ProgressRecords
                    .CountAsync(p => p.EnrollmentId == enrollment.EnrollmentId && p.IsCompleted);
                progressPercent = Math.Clamp((int)Math.Round((double)completedCount / totalItems * 100), 0, 100);
            }

            instructorUsers.TryGetValue(c.InstructorId, out var instructorName);

            courseCards.Add(new CourseCardViewModel
            {
                CourseId = c.CourseId,
                Title = c.Title,
                Description = c.Description,
                Category = c.Category,
                ThumbnailUrl = c.ThumbnailUrl,
                InstructorName = instructorName ?? "Instructor",
                IsEnrolled = isEnrolled,
                ProgressPercent = progressPercent,
                LessonCount = lessonCount,
                ChallengeCount = challengeCount,
                SimulationCount = simulationCount,
                LastLessonId = enrollment?.LastLessonId
            });
        }

        var model = new CourseIndexViewModel
        {
            Courses = courseCards,
            Search = search,
            SelectedCategory = category,
            Categories = categories
        };

        return View(model);
    }

    // ==========================================
    // 2. COURSE DETAILS & SYLLABUS
    // ==========================================
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var course = await db.Courses.FindAsync(id);
        if (course == null || !CanReadCourse(course))
        {
            return NotFound("Course not found or is currently private.");
        }

        if (User.IsInRole(AccountRoles.Admin)) return RedirectToAction("EditCourse", "Admin", new { id });

        var instructor = await db.Users.FindAsync(course.InstructorId);
        var lessons = await db.Lessons.Where(l => l.CourseId == id).OrderBy(l => l.OrderIndex).ToListAsync();
        var challenges = await db.Challenges.Where(c => c.CourseId == id && c.IsPublished).ToListAsync();
        var simulations = await db.SimulationTasks.Where(s => s.CourseId == id && s.IsPublished).ToListAsync();

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var enrollment = User.IsInRole(AccountRoles.Student) && currentUserId != null
            ? await db.Enrollments.FirstOrDefaultAsync(e => e.UserId == currentUserId && e.CourseId == id)
            : null;

        var completedLessons = new HashSet<int>();
        var completedChallenges = new HashSet<int>();
        var completedSimulations = new HashSet<int>();
        var progressPercent = 0;

        if (enrollment != null)
        {
            var progress = await db.ProgressRecords
                .Where(p => p.EnrollmentId == enrollment.EnrollmentId && p.IsCompleted)
                .ToListAsync();

            completedLessons = progress.Where(p => p.LessonId.HasValue).Select(p => p.LessonId!.Value).ToHashSet();
            completedChallenges = progress.Where(p => p.ChallengeId.HasValue).Select(p => p.ChallengeId!.Value).ToHashSet();
            completedSimulations = progress.Where(p => p.SimulationTaskId.HasValue).Select(p => p.SimulationTaskId!.Value).ToHashSet();

            var totalItems = lessons.Count + challenges.Count + simulations.Count;
            if (totalItems > 0)
            {
                progressPercent = Math.Clamp((int)Math.Round((double)progress.Count / totalItems * 100), 0, 100);
            }
        }

        var model = new CourseDetailsViewModel
        {
            Course = course,
            InstructorName = instructor?.DisplayName ?? "Instructor",
            IsEnrolled = enrollment != null,
            ProgressPercent = progressPercent,
            ResumeLessonId = enrollment?.LastLessonId ?? lessons.FirstOrDefault()?.LessonId,
            Lessons = lessons,
            Challenges = challenges,
            SimulationTasks = simulations,
            CompletedLessonIds = completedLessons,
            CompletedChallengeIds = completedChallenges,
            CompletedSimulationIds = completedSimulations
        };

        return View(model);
    }

    // ==========================================
    // 3. ENROLL IN COURSE
    // ==========================================
    [Authorize(Roles = AccountRoles.Student)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enroll(int id)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FindAsync(id);
        if (course == null || !CanReadCourse(course)) return NotFound("Course does not exist.");

        var existingEnrollment = await db.Enrollments
            .FirstOrDefaultAsync(e => e.UserId == currentUserId && e.CourseId == id);

        if (existingEnrollment == null)
        {
            var firstLesson = await db.Lessons
                .Where(l => l.CourseId == id)
                .OrderBy(l => l.OrderIndex)
                .FirstOrDefaultAsync();

            var newEnrollment = new Enrollment
            {
                UserId = currentUserId,
                CourseId = id,
                Status = "InProgress",
                EnrolledDate = DateTime.UtcNow,
                LastLessonId = firstLesson?.LessonId
            };

            db.Enrollments.Add(newEnrollment);
            await db.SaveChangesAsync();
            TempData["Success"] = $"Enrolled in {course.Title}! Welcome to your training.";
        }

        return RedirectToAction(nameof(Learn), new { id });
    }

    // ==========================================
    // 4. INTERACTIVE LEARNING PORTAL (PLAYER)
    // ==========================================
    [Authorize(Roles = AccountRoles.Student)]
    [HttpGet]
    public async Task<IActionResult> Learn(int id, int? lessonId, int? challengeId, int? simulationId)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var course = await db.Courses.FindAsync(id);
        if (course == null || !CanReadCourse(course)) return NotFound("Course not found.");

        var enrollment = await db.Enrollments
            .FirstOrDefaultAsync(e => e.UserId == currentUserId && e.CourseId == id);

        if (enrollment == null)
            return RedirectToAction(nameof(Details), new { id });

        var lessons = await db.Lessons.Where(l => l.CourseId == id).OrderBy(l => l.OrderIndex).ToListAsync();
        var challenges = await db.Challenges.Where(c => c.CourseId == id && c.IsPublished).ToListAsync();
        var simulations = await db.SimulationTasks.Where(s => s.CourseId == id && s.IsPublished).ToListAsync();

        var progressList = await db.ProgressRecords
            .Where(p => p.EnrollmentId == enrollment.EnrollmentId && p.IsCompleted)
            .ToListAsync();

        var completedLessonIds = progressList.Where(p => p.LessonId.HasValue).Select(p => p.LessonId!.Value).ToHashSet();
        var completedChallengeIds = progressList.Where(p => p.ChallengeId.HasValue).Select(p => p.ChallengeId!.Value).ToHashSet();
        var completedSimulationIds = progressList.Where(p => p.SimulationTaskId.HasValue).Select(p => p.SimulationTaskId!.Value).ToHashSet();

        var totalItems = lessons.Count + challenges.Count + simulations.Count;
        var progressPercent = totalItems > 0
            ? Math.Clamp((int)Math.Round((double)progressList.Count / totalItems * 100), 0, 100)
            : 0;

        // Determine current view item
        LearnContentType currentType;
        Lesson? currentLesson = null;
        Challenge? currentChallenge = null;
        SimulationTask? currentSimulation = null;
        bool isCurrentCompleted = false;

        if (simulationId.HasValue)
        {
            currentSimulation = simulations.FirstOrDefault(s => s.TaskId == simulationId.Value);
            if (currentSimulation != null)
            {
                currentType = LearnContentType.Simulation;
                isCurrentCompleted = completedSimulationIds.Contains(currentSimulation.TaskId);
            }
            else
            {
                currentType = LearnContentType.Lesson;
                currentLesson = lessons.FirstOrDefault();
            }
        }
        else if (challengeId.HasValue)
        {
            currentChallenge = challenges.FirstOrDefault(c => c.ChallengeId == challengeId.Value);
            if (currentChallenge != null)
            {
                currentType = LearnContentType.Challenge;
                isCurrentCompleted = completedChallengeIds.Contains(currentChallenge.ChallengeId);
            }
            else
            {
                currentType = LearnContentType.Lesson;
                currentLesson = lessons.FirstOrDefault();
            }
        }
        else
        {
            currentType = LearnContentType.Lesson;
            if (lessonId.HasValue)
            {
                currentLesson = lessons.FirstOrDefault(l => l.LessonId == lessonId.Value);
            }

            // Fallback to enrollment resume pointer, or first lesson
            if (currentLesson == null && enrollment.LastLessonId.HasValue)
            {
                currentLesson = lessons.FirstOrDefault(l => l.LessonId == enrollment.LastLessonId.Value);
            }
            currentLesson ??= lessons.FirstOrDefault();

            if (currentLesson != null)
            {
                isCurrentCompleted = completedLessonIds.Contains(currentLesson.LessonId);
            }
        }

        // Previous and Next lesson calculation
        int? prevLessonId = null;
        int? nextLessonId = null;
        if (currentLesson != null)
        {
            var currentIndex = lessons.IndexOf(currentLesson);
            if (currentIndex > 0) prevLessonId = lessons[currentIndex - 1].LessonId;
            if (currentIndex < lessons.Count - 1) nextLessonId = lessons[currentIndex + 1].LessonId;
        }

        // Challenge history if current item is challenge
        var challengeHistory = new List<ChallengeSubmission>();
        if (currentChallenge != null)
        {
            challengeHistory = await db.ChallengeSubmissions
                .Where(s => s.UserId == currentUserId && s.ChallengeId == currentChallenge.ChallengeId)
                .OrderByDescending(s => s.SubmittedDate)
                .Take(5)
                .ToListAsync();
        }

        var viewModel = new CourseLearnViewModel
        {
            Course = course,
            Enrollment = enrollment,
            ProgressPercent = progressPercent,
            Lessons = lessons,
            Challenges = challenges,
            SimulationTasks = simulations,
            CompletedLessonIds = completedLessonIds,
            CompletedChallengeIds = completedChallengeIds,
            CompletedSimulationIds = completedSimulationIds,
            CurrentType = currentType,
            CurrentLesson = currentLesson,
            CurrentChallenge = currentChallenge,
            CurrentSimulation = currentSimulation,
            PrevLessonId = prevLessonId,
            NextLessonId = nextLessonId,
            CurrentItemCompleted = isCurrentCompleted,
            ChallengeHistory = challengeHistory
        };

        return View(viewModel);
    }

    // ==========================================
    // 5. COMPLETE LESSON
    // ==========================================
    [Authorize(Roles = AccountRoles.Student)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteLesson(int lessonId, int courseId)
    {
        var course = await db.Courses.FindAsync(courseId);
        if (course is null || !CanReadCourse(course)) return NotFound();
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var enrollment = await db.Enrollments.FirstOrDefaultAsync(e => e.UserId == currentUserId && e.CourseId == courseId);
        if (enrollment == null) return NotFound("Enrollment not found.");

        var lesson = await db.Lessons.FindAsync(lessonId);
        if (lesson == null || lesson.CourseId != courseId) return NotFound("Lesson not found.");

        var existingProgress = await db.ProgressRecords
            .FirstOrDefaultAsync(p => p.EnrollmentId == enrollment.EnrollmentId && p.LessonId == lessonId);

        if (existingProgress == null)
        {
            db.ProgressRecords.Add(new Progress
            {
                EnrollmentId = enrollment.EnrollmentId,
                ContentType = ProgressContentType.Lesson,
                LessonId = lessonId,
                IsCompleted = true,
                LastUpdatedDate = DateTime.UtcNow
            });
        }
        else
        {
            existingProgress.IsCompleted = true;
            existingProgress.LastUpdatedDate = DateTime.UtcNow;
        }

        enrollment.LastLessonId = lessonId;

        // Check if all items in course are complete
        await SaveProgressAndStatus(enrollment, courseId);

        // Redirect to next lesson if available, or stay
        var nextLesson = await db.Lessons
            .Where(l => l.CourseId == courseId && l.OrderIndex > lesson.OrderIndex)
            .OrderBy(l => l.OrderIndex)
            .FirstOrDefaultAsync();

        if (nextLesson != null)
        {
            return RedirectToAction(nameof(Learn), new { id = courseId, lessonId = nextLesson.LessonId });
        }

        TempData["Success"] = $"Lesson \"{lesson.Title}\" marked as complete!";
        return RedirectToAction(nameof(Learn), new { id = courseId, lessonId });
    }

    // ==========================================
    // 6. CTF FLAG SUBMISSION
    // ==========================================
    [Authorize(Roles = AccountRoles.Student)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitFlag(int challengeId, int courseId, string flag)
    {
        var course = await db.Courses.FindAsync(courseId);
        if (course is null || !CanReadCourse(course)) return NotFound();
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var enrollment = await db.Enrollments.FirstOrDefaultAsync(e => e.UserId == currentUserId && e.CourseId == courseId);
        if (enrollment == null) return NotFound("Enrollment not found.");

        var challenge = await db.Challenges.FindAsync(challengeId);
        if (challenge == null || challenge.CourseId != courseId || !CanReadActivity(course, challenge.IsPublished)) return NotFound("Challenge not found.");

        if (string.IsNullOrWhiteSpace(flag))
        {
            TempData["FlagError"] = "Please enter a flag before submitting.";
            return RedirectToAction(nameof(Learn), new { id = courseId, challengeId });
        }

        var trimmedFlag = flag.Trim();
        var hashedInput = ComputeSha256(trimmedFlag);
        var isCorrect = string.Equals(hashedInput, challenge.FlagHash, StringComparison.OrdinalIgnoreCase);

        // Record submission attempt
        var submission = new ChallengeSubmission
        {
            UserId = currentUserId,
            ChallengeId = challengeId,
            SubmittedFlag = trimmedFlag,
            IsCorrect = isCorrect,
            SubmittedDate = DateTime.UtcNow
        };
        db.ChallengeSubmissions.Add(submission);

        if (isCorrect)
        {
            var existingProgress = await db.ProgressRecords
                .FirstOrDefaultAsync(p => p.EnrollmentId == enrollment.EnrollmentId && p.ChallengeId == challengeId);

            if (existingProgress == null)
            {
                db.ProgressRecords.Add(new Progress
                {
                    EnrollmentId = enrollment.EnrollmentId,
                    ContentType = ProgressContentType.Challenge,
                    ChallengeId = challengeId,
                    IsCompleted = true,
                    LastUpdatedDate = DateTime.UtcNow
                });
            }
            else
            {
                existingProgress.IsCompleted = true;
                existingProgress.LastUpdatedDate = DateTime.UtcNow;
            }

            await SaveProgressAndStatus(enrollment, courseId);
            TempData["FlagSuccess"] = $"🎉 Correct Flag! You earned {challenge.Points} points!";
        }
        else
        {
            TempData["FlagError"] = "❌ Incorrect flag. Review the challenge files or question clues and try again!";
            await db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Learn), new { id = courseId, challengeId });
    }

    // ==========================================
    // 7. DOWNLOAD CTF CHALLENGE FILE
    // ==========================================
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> DownloadChallengeFile(int id)
    {
        var challenge = await db.Challenges.FindAsync(id);
        if (challenge == null) return NotFound("Challenge not found.");

        if (string.IsNullOrEmpty(challenge.FilePath))
        {
            return NotFound("No file attached to this challenge.");
        }

        var course = await db.Courses.FindAsync(challenge.CourseId);
        if (course is null || !CanReadActivity(course, challenge.IsPublished)) return NotFound();
        var fullPath = CourseFiles.Resolve(environment, challenge.FilePath);

        if (fullPath is null || !System.IO.File.Exists(fullPath))
        {
            return NotFound("Challenge file is missing from the server.");
        }

        var fileName = Path.GetFileName(fullPath);
        return PhysicalFile(fullPath, "application/octet-stream", fileName);
    }

    // ==========================================
    // 8. COMPLETE SIMULATION TASK
    // ==========================================
    [Authorize(Roles = AccountRoles.Student)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(128 * 1024)]
    public async Task<IActionResult> CompleteSimulation(int taskId, int courseId, string[]? commands)
    {
        var course = await db.Courses.FindAsync(courseId);
        if (course is null || !CanReadCourse(course)) return NotFound();
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var enrollment = await db.Enrollments.FirstOrDefaultAsync(e => e.UserId == currentUserId && e.CourseId == courseId);
        if (enrollment == null) return NotFound(new { success = false, message = "Enrollment not found." });

        var simulation = await db.SimulationTasks.FindAsync(taskId);
        if (simulation == null || simulation.CourseId != courseId || !CanReadActivity(course, simulation.IsPublished))
        {
            return NotFound(new { success = false, message = "Simulation task not found." });
        }

        if (!SimulationVerifier.IsComplete(simulation.VirtualFileSystemJson, commands))
            return BadRequest(new { success = false, message = "The lab objectives are not complete. Run the required commands and try again." });

        var existingProgress = await db.ProgressRecords
            .FirstOrDefaultAsync(p => p.EnrollmentId == enrollment.EnrollmentId && p.SimulationTaskId == taskId);

        if (existingProgress == null)
        {
            db.ProgressRecords.Add(new Progress
            {
                EnrollmentId = enrollment.EnrollmentId,
                ContentType = ProgressContentType.Simulation,
                SimulationTaskId = taskId,
                IsCompleted = true,
                LastUpdatedDate = DateTime.UtcNow
            });
        }
        else
        {
            existingProgress.IsCompleted = true;
            existingProgress.LastUpdatedDate = DateTime.UtcNow;
        }

        await SaveProgressAndStatus(enrollment, courseId);

        return Json(new
        {
            success = true,
            message = "Lab exercise completed successfully! Progress saved.",
            reward = simulation.RewardFlagHash
        });
    }

    // ==========================================
    // HELPERS
    // ==========================================
    private bool CanReadCourse(Course course) => course.IsPublished
        || User.IsInRole(AccountRoles.Admin) || IsCourseOwner(course.InstructorId);

    private bool CanReadActivity(Course course, bool published) => CanReadCourse(course)
        && (published || User.IsInRole(AccountRoles.Admin) || IsCourseOwner(course.InstructorId));

    private bool IsCourseOwner(string instructorId)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return currentUserId != null && currentUserId == instructorId;
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task SaveProgressAndStatus(Enrollment enrollment, int courseId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        var totalLessons = await db.Lessons.CountAsync(l => l.CourseId == courseId);
        var totalChallenges = await db.Challenges.CountAsync(c => c.CourseId == courseId && c.IsPublished);
        var totalSimulations = await db.SimulationTasks.CountAsync(s => s.CourseId == courseId && s.IsPublished);
        var total = totalLessons + totalChallenges + totalSimulations;

        if (total > 0)
        {
            var completedCount = await db.ProgressRecords
                .CountAsync(p => p.EnrollmentId == enrollment.EnrollmentId && p.IsCompleted
                    && (p.ContentType == ProgressContentType.Lesson && db.Lessons.Any(l => l.LessonId == p.LessonId && l.CourseId == courseId)
                        || p.ContentType == ProgressContentType.Challenge && db.Challenges.Any(c => c.ChallengeId == p.ChallengeId && c.CourseId == courseId && c.IsPublished)
                        || p.ContentType == ProgressContentType.Simulation && db.SimulationTasks.Any(t => t.TaskId == p.SimulationTaskId && t.CourseId == courseId && t.IsPublished)));

            if (completedCount >= total)
            {
                enrollment.Status = "Completed";
            }
            else enrollment.Status = "InProgress";
        }
        await db.SaveChangesAsync();
        await personalization.SyncRewards(enrollment.UserId);
        await transaction.CommitAsync();
    }
}
