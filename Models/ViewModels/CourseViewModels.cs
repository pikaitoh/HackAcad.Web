using System.ComponentModel.DataAnnotations;
using HackAcad.Web.Models.Entities;
using Microsoft.AspNetCore.Http;

namespace HackAcad.Web.Models.ViewModels;

public class CourseCardViewModel
{
    public int CourseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? ThumbnailUrl { get; set; }
<<<<<<< Updated upstream
    public string InstructorName { get; set; } = string.Empty;
    public bool IsEnrolled { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsSaved { get; set; }
=======
    public string TeacherName { get; set; } = string.Empty;
    public bool IsEnrolled { get; set; }
>>>>>>> Stashed changes
    public int ProgressPercent { get; set; }
    public int LessonCount { get; set; }
    public int ChallengeCount { get; set; }
    public int SimulationCount { get; set; }
    public int? LastLessonId { get; set; }
}

public class CourseIndexViewModel
{
    public List<CourseCardViewModel> Courses { get; set; } = [];
    public string? Search { get; set; }
    public string? SelectedCategory { get; set; }
    public List<string> Categories { get; set; } = [];
}

<<<<<<< Updated upstream
public class SavedCoursesViewModel
{
    public List<CourseCardViewModel> Courses { get; set; } = [];
}

public class CourseDetailsViewModel
{
    public Course Course { get; set; } = null!;
    public string InstructorName { get; set; } = string.Empty;
    public bool IsEnrolled { get; set; }
    public bool IsOwnCourse { get; set; }
    public bool IsSaved { get; set; }
=======
public class CourseDetailsViewModel
{
    public Course Course { get; set; } = null!;
    public string TeacherName { get; set; } = string.Empty;
    public bool IsEnrolled { get; set; }
>>>>>>> Stashed changes
    public int ProgressPercent { get; set; }
    public int? ResumeLessonId { get; set; }
    public List<Lesson> Lessons { get; set; } = [];
    public List<Challenge> Challenges { get; set; } = [];
    public List<SimulationTask> SimulationTasks { get; set; } = [];
<<<<<<< Updated upstream
    public List<CourseNotebook> CourseNotebooks { get; set; } = [];
=======
>>>>>>> Stashed changes
    public HashSet<int> CompletedLessonIds { get; set; } = [];
    public HashSet<int> CompletedChallengeIds { get; set; } = [];
    public HashSet<int> CompletedSimulationIds { get; set; } = [];
}

public enum LearnContentType
{
    Lesson,
    Challenge,
<<<<<<< Updated upstream
    Simulation,
    Notebook
=======
    Simulation
>>>>>>> Stashed changes
}

public class CourseLearnViewModel
{
    public Course Course { get; set; } = null!;
    public Enrollment Enrollment { get; set; } = null!;
    public int ProgressPercent { get; set; }
<<<<<<< Updated upstream
    public bool IsCourseCompleted { get; set; }
=======
>>>>>>> Stashed changes

    public List<Lesson> Lessons { get; set; } = [];
    public List<Challenge> Challenges { get; set; } = [];
    public List<SimulationTask> SimulationTasks { get; set; } = [];
<<<<<<< Updated upstream
    public List<CourseNotebook> CourseNotebooks { get; set; } = [];
=======
>>>>>>> Stashed changes

    public HashSet<int> CompletedLessonIds { get; set; } = [];
    public HashSet<int> CompletedChallengeIds { get; set; } = [];
    public HashSet<int> CompletedSimulationIds { get; set; } = [];

    public LearnContentType CurrentType { get; set; }
<<<<<<< Updated upstream

    public Lesson? CurrentLesson { get; set; }
    public Challenge? CurrentChallenge { get; set; }
    public SimulationTask? CurrentSimulation { get; set; }
    public CourseNotebook? CurrentNotebook { get; set; }
=======
    public Lesson? CurrentLesson { get; set; }
    public Challenge? CurrentChallenge { get; set; }
    public SimulationTask? CurrentSimulation { get; set; }
>>>>>>> Stashed changes

    public int? PrevLessonId { get; set; }
    public int? NextLessonId { get; set; }
    public bool CurrentItemCompleted { get; set; }
<<<<<<< Updated upstream

    public List<ChallengeSubmission> ChallengeHistory { get; set; } = [];

    // Hints for the currently selected challenge.
    public List<ChallengeHint> CurrentChallengeHints { get; set; } = [];

    // IDs of hints already unlocked by the current learner.
    public HashSet<int> UnlockedChallengeHintIds { get; set; } = [];

    public int CurrentChallengeHintPointsDeducted { get; set; }
=======
    public List<ChallengeSubmission> ChallengeHistory { get; set; } = [];
>>>>>>> Stashed changes
}

public class CourseCreateEditModel
{
<<<<<<< Updated upstream
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public string ReviewStatus { get; set; } = CourseReviewStatus.Draft;

=======
>>>>>>> Stashed changes
    public int CourseId { get; set; }

    [Required(ErrorMessage = "Enter a course title."), StringLength(150, ErrorMessage = "Title cannot exceed 150 characters.")]
    public string Title { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters.")]
    public string? Description { get; set; }

    [StringLength(50, ErrorMessage = "Category cannot exceed 50 characters.")]
    public string? Category { get; set; }

    [StringLength(300, ErrorMessage = "Thumbnail URL cannot exceed 300 characters.")]
    public string? ThumbnailUrl { get; set; }

    public bool IsPublished { get; set; }
}

public class LessonCreateEditModel
{
    public int LessonId { get; set; }
    public int CourseId { get; set; }

    [Required(ErrorMessage = "Enter a lesson title."), StringLength(150, ErrorMessage = "Title cannot exceed 150 characters.")]
    public string Title { get; set; } = string.Empty;

    public string? Content { get; set; }

    [Range(0, 999, ErrorMessage = "Order must be between 0 and 999.")]
    public int OrderIndex { get; set; }

    [StringLength(300, ErrorMessage = "Video URL cannot exceed 300 characters.")]
    public string? VideoUrl { get; set; }
}

public class ChallengeCreateEditModel
{
    public int ChallengeId { get; set; }
    public int CourseId { get; set; }

    [Required(ErrorMessage = "Enter a challenge title."), StringLength(150, ErrorMessage = "Title cannot exceed 150 characters.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Select a category."), StringLength(50)]
    public string Category { get; set; } = "Web";

    [Required(ErrorMessage = "Select difficulty."), StringLength(20)]
    public string Difficulty { get; set; } = "Easy";

    [StringLength(2000, ErrorMessage = "Description cannot exceed 2000 characters.")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Points are required."), Range(10, 1000, ErrorMessage = "Points must be between 10 and 1000.")]
    public int Points { get; set; } = 100;

    [Display(Name = "Plaintext Flag")]
    public string? PlaintextFlag { get; set; }

<<<<<<< Updated upstream
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
=======
>>>>>>> Stashed changes
    public string? ExistingFilePath { get; set; }

    [Display(Name = "Challenge File (Optional)")]
    public IFormFile? UploadFile { get; set; }

    public bool IsPublished { get; set; } = true;
<<<<<<< Updated upstream

    // Hints configured by the instructor.
    public List<ChallengeHintEditModel> Hints { get; set; } = [];
}

public class ChallengeHintEditModel
{
    public int ChallengeHintId { get; set; }

    [Required(ErrorMessage = "Enter the hint text.")]
    [StringLength(2000, ErrorMessage = "Hint cannot exceed 2000 characters.")]
    public string HintText { get; set; } = string.Empty;

    [Range(1, 100, ErrorMessage = "Hint order must be between 1 and 100.")]
    public int DisplayOrder { get; set; }

    [Range(0, 1000, ErrorMessage = "Deduction must be between 0 and 1000 points.")]
    public int DeductionPoints { get; set; } = 10;
=======
>>>>>>> Stashed changes
}

public class SimulationCreateEditModel
{
    public int TaskId { get; set; }
    public int CourseId { get; set; }
<<<<<<< Updated upstream
    public int? NotebookId { get; set; }
    public int? CourseNotebookId { get; set; }
    public string? BlockId { get; set; }
    public string? ReturnUrl { get; set; }
=======
>>>>>>> Stashed changes

    [Required(ErrorMessage = "Enter a task title."), StringLength(150, ErrorMessage = "Title cannot exceed 150 characters.")]
    public string Title { get; set; } = string.Empty;

<<<<<<< Updated upstream
    [StringLength(2000, ErrorMessage = "Description cannot exceed 2000 characters.")]
    public string? Description { get; set; }

    [StringLength(50)]
    public string? Difficulty { get; set; }

=======
>>>>>>> Stashed changes
    public string? Instructions { get; set; }

    [Display(Name = "Virtual File System (JSON)")]
    public string? VirtualFileSystemJson { get; set; }

    [Display(Name = "Reward Flag (revealed upon completion, optional)")]
    public string? RewardFlag { get; set; }

    public bool IsPublished { get; set; } = true;
}

<<<<<<< Updated upstream
public class InstructorCourseRow
=======
public class TeacherCourseRow
>>>>>>> Stashed changes
{
    public Course Course { get; set; } = null!;
    public int LessonCount { get; set; }
    public int ChallengeCount { get; set; }
    public int SimulationCount { get; set; }
    public int EnrolledStudentsCount { get; set; }
    public int CompletedStudentsCount { get; set; }
}

<<<<<<< Updated upstream
public class InstructorDashboardViewModel
{
    public List<InstructorCourseRow> Courses { get; set; } = [];
=======
public class TeacherDashboardViewModel
{
    public List<TeacherCourseRow> Courses { get; set; } = [];
>>>>>>> Stashed changes
    public int TotalStudentsEnrolled { get; set; }
    public int TotalSubmissionsReceived { get; set; }
}

public class StudentCourseActivityRow
{
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string StudentEmail { get; set; } = string.Empty;
    public DateTime EnrolledDate { get; set; }
    public int ProgressPercent { get; set; }
<<<<<<< Updated upstream
    public TimeSpan? ElapsedCourseTime { get; set; }
    public DateTime? LatestActivityUtc { get; set; }
    public string Status { get; set; } = string.Empty;

    public string ElapsedCourseTimeLabel
    {
        get
        {
            if (ElapsedCourseTime is null)
                return "No completed activity";

            var totalMinutes = Math.Max(0, (int)ElapsedCourseTime.Value.TotalMinutes);

            if (totalMinutes >= 1440)
                return $"{totalMinutes / 1440}d {(totalMinutes % 1440) / 60}h";

            if (totalMinutes >= 60)
                return $"{totalMinutes / 60}h {totalMinutes % 60}m";

            return $"{totalMinutes}m";
        }
    }
=======
    public string Status { get; set; } = string.Empty;
>>>>>>> Stashed changes
}

public class CourseCurriculumViewModel
{
    public Course Course { get; set; } = null!;
    public List<Lesson> Lessons { get; set; } = [];
    public List<Challenge> Challenges { get; set; } = [];
    public List<SimulationTask> SimulationTasks { get; set; } = [];
<<<<<<< Updated upstream
    public List<CourseNotebook> CourseNotebooks { get; set; } = [];
    public List<StudentCourseActivityRow> EnrolledStudents { get; set; } = [];
    public List<ChallengeSubmission> RecentSubmissions { get; set; } = [];
}

public class CourseNotebookCreateEditModel
{
    public int CourseNotebookId { get; set; }
    public int CourseId { get; set; }

    [Required(ErrorMessage = "Notebook title is required."), StringLength(150, ErrorMessage = "Title cannot exceed 150 characters.")]
    public string Title { get; set; } = string.Empty;

    public string? ContentJson { get; set; }
    public bool IsPublished { get; set; } = true;
    public int OrderIndex { get; set; }
}

public class ChallengePreviewViewModel
{
    public int CourseId { get; set; }
    public int ChallengeId { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Points { get; set; }
    public string? FileName { get; set; }
    public bool IsPublished { get; set; }
    public List<ChallengeHint> Hints { get; set; } = [];
}
=======
    public List<StudentCourseActivityRow> EnrolledStudents { get; set; } = [];
    public List<ChallengeSubmission> RecentSubmissions { get; set; } = [];
}
>>>>>>> Stashed changes
