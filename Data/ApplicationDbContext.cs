using HackAcad.Web.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Data;

public partial class ApplicationDbContext : IdentityUserContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Role membership lives only in these business tables; Identity stores users,
    // claims, external logins and MFA tokens without a second role schema.
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Instructor> Instructors => Set<Instructor>();
    public DbSet<Admin> Admins => Set<Admin>();

    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseReviewHistory> CourseReviewHistories => Set<CourseReviewHistory>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<SavedCourse> SavedCourses => Set<SavedCourse>();
    public DbSet<UserBadgeAward> UserBadgeAwards => Set<UserBadgeAward>();
    public DbSet<Progress> ProgressRecords => Set<Progress>();
    public DbSet<Challenge> Challenges => Set<Challenge>();
    public DbSet<ChallengeSubmission> ChallengeSubmissions => Set<ChallengeSubmission>();
    public DbSet<ChallengeHint> ChallengeHints => Set<ChallengeHint>();
    public DbSet<ChallengeHintUnlock> ChallengeHintUnlocks => Set<ChallengeHintUnlock>();
    public DbSet<ChallengeScoreAward> ChallengeScoreAwards => Set<ChallengeScoreAward>();
    public DbSet<SimulationTask> SimulationTasks => Set<SimulationTask>();
    public DbSet<LoginRecord> LoginRecords => Set<LoginRecord>();
    public DbSet<AdminActivity> AdminActivities => Set<AdminActivity>();
    public DbSet<AccountSession> AccountSessions => Set<AccountSession>();
    public DbSet<InstructorApplication> InstructorApplications => Set<InstructorApplication>();
    public DbSet<InstructorReview> InstructorReviews => Set<InstructorReview>();

    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<BannerReward> BannerRewards => Set<BannerReward>();

    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();
    public DbSet<SecurityAlert> SecurityAlerts => Set<SecurityAlert>();
    public DbSet<SupportAccess> SupportAccesses => Set<SupportAccess>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();

    public DbSet<Notebook> Notebooks => Set<Notebook>();
    public DbSet<CourseNotebook> CourseNotebooks => Set<CourseNotebook>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // Explicit table names keep Identity and application tables in snake_case.
        builder.Entity<ApplicationUser>().ToTable("users");
        builder.Entity<IdentityUserClaim<string>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<string>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<string>>().ToTable("user_tokens");
        builder.Entity<Student>().ToTable("students");
        builder.Entity<Instructor>().ToTable("instructors");
        builder.Entity<Admin>().ToTable("admins");
        builder.Entity<Course>().ToTable("courses");
        // ADMIN - The course version is checked in UPDATE predicates, preventing approval of overwritten content.
        builder.Entity<Course>().Property(c => c.ReviewVersion).IsConcurrencyToken();
        builder.Entity<Course>().HasIndex(c => new { c.ReviewStatus, c.SubmittedForReviewUtc });
        builder.Entity<Lesson>().ToTable("lessons");
        builder.Entity<Enrollment>().ToTable("enrollments");
        builder.Entity<SavedCourse>().ToTable("saved_courses");
        builder.Entity<UserBadgeAward>().ToTable("user_badge_awards");
        builder.Entity<Progress>().ToTable("progress_records");
        builder.Entity<Challenge>().ToTable("challenges");
        builder.Entity<ChallengeSubmission>().ToTable("challenge_submissions");
        builder.Entity<ChallengeHint>().ToTable("challenge_hints");
        builder.Entity<ChallengeHintUnlock>().ToTable("challenge_hint_unlocks");
        builder.Entity<ChallengeScoreAward>().ToTable("challenge_score_awards");
        builder.Entity<SimulationTask>().ToTable("simulation_tasks");
        builder.Entity<LoginRecord>().ToTable("login_records");
        builder.Entity<AdminActivity>().ToTable("admin_activities");
        builder.Entity<AccountSession>().ToTable("account_sessions");
        builder.Entity<InstructorApplication>().ToTable("instructor_applications");
        builder.Entity<InstructorReview>().ToTable("instructor_reviews");
        builder.Entity<UserNotification>().ToTable("user_notifications");
        builder.Entity<BannerReward>().ToTable("banner_rewards");
        builder.Entity<SecurityEvent>().ToTable("security_events");
        builder.Entity<SecurityAlert>().ToTable("security_alerts");
        builder.Entity<SupportAccess>().ToTable("support_accesses");
        builder.Entity<SupportTicket>().ToTable("support_tickets");
        builder.Entity<SupportMessage>().ToTable("support_messages");
        builder.Entity<Notebook>().ToTable("notebooks");
        builder.Entity<CourseNotebook>().ToTable("course_notebooks");

        builder.Entity<AdminActivity>().Property(a => a.Before).HasColumnType("text");
        builder.Entity<AdminActivity>().Property(a => a.After).HasColumnType("text");
        builder.Entity<SecurityEvent>().HasIndex(e => new { e.IpAddress, e.OccurredUtc });
        builder.Entity<SecurityEvent>().HasIndex(e => new { e.SubjectHash, e.OccurredUtc });
        builder.Entity<SecurityAlert>().HasIndex(e => e.EventKey).IsUnique();
        builder.Entity<SecurityAlert>().HasIndex(e => new { e.Status, e.CreatedUtc });
        builder.Entity<SupportAccess>().HasIndex(e => e.TokenHash).IsUnique();
        builder.Entity<SupportAccess>().HasIndex(e => new { e.UserId, e.CreatedUtc });
        builder.Entity<SupportAccess>().HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SupportTicket>().HasIndex(e => new { e.UserId, e.UpdatedUtc });
        builder.Entity<SupportTicket>().HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupportMessage>().HasOne<SupportTicket>().WithMany().HasForeignKey(e => e.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<UserNotification>().HasIndex(n => new { n.UserId, n.EventKey }).IsUnique();
        builder.Entity<UserNotification>().HasIndex(n => new { n.UserId, n.ReadUtc, n.CreatedUtc });
        builder.Entity<UserNotification>().HasOne<ApplicationUser>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<BannerReward>().HasIndex(r => new { r.UserId, r.CourseId }).IsUnique();
        builder.Entity<BannerReward>().HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SavedCourse>().HasIndex(s => new { s.UserId, s.CourseId }).IsUnique();
        builder.Entity<SavedCourse>().HasIndex(s => new { s.UserId, s.SavedUtc });
        builder.Entity<SavedCourse>().HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SavedCourse>().HasOne<Course>().WithMany().HasForeignKey(s => s.CourseId).OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ApplicationUser>().Property(u => u.TimeZoneId).HasDefaultValue("Asia/Kuala_Lumpur");
        builder.Entity<ApplicationUser>().Property(u => u.PhoneNumber).HasMaxLength(16);
        builder.Entity<ApplicationUser>().HasIndex(u => u.PhoneNumber).IsUnique();

        builder.Entity<LoginRecord>().HasIndex(r => new { r.UserId, r.OccurredUtc });
        builder.Entity<LoginRecord>().HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<AccountSession>().HasIndex(r => new { r.UserId, r.ExpiresUtc });
        builder.Entity<AccountSession>().HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<AdminActivity>().HasIndex(r => new { r.ActorUserId, r.OccurredUtc });
        // Keep the audit entry when its actor/target is removed; only the actor relation is cleared.
        // ADMIN - Deleting an account must not erase its audit history; nullable actors preserve the event.
        builder.Entity<AdminActivity>().HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.ActorUserId).OnDelete(DeleteBehavior.SetNull);

        builder.Entity<InstructorApplication>().HasIndex(a => new { a.UserId, a.Revision }).IsUnique();
        builder.Entity<InstructorApplication>().HasIndex(a => new { a.Status, a.SubmittedUtc });
        builder.Entity<InstructorApplication>().Property(a => a.Status).IsConcurrencyToken();
        builder.Entity<InstructorApplication>().HasOne<ApplicationUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<InstructorReview>().HasIndex(r => r.ApplicationId).IsUnique();
        builder.Entity<InstructorReview>().Property(r => r.VerifiedYears).HasPrecision(5, 2);
        builder.Entity<InstructorReview>().HasOne<InstructorApplication>().WithMany().HasForeignKey(r => r.ApplicationId).OnDelete(DeleteBehavior.Restrict);

        // --- Role tables: UserId is PK and FK to users in one step ---
        builder.Entity<Student>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Instructor>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Admin>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // --- Courses: InstructorId points at Instructors, not users directly ---
        builder.Entity<Course>()
            .HasOne<Instructor>()
            .WithMany()
            .HasForeignKey(c => c.InstructorId)
            .HasPrincipalKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- Courses -> Lessons / Challenges / SimulationTasks: cascade ---
        builder.Entity<Lesson>()
            .HasOne<Course>()
            .WithMany()
            .HasForeignKey(l => l.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Challenge>()
            .HasOne<Course>()
            .WithMany()
            .HasForeignKey(c => c.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<SimulationTask>()
            .HasOne<Course>()
            .WithMany()
            .HasForeignKey(t => t.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        // --- Terminal Notebooks ---
        builder.Entity<Notebook>().Property(n => n.ContentJson).HasColumnType("longtext");
        builder.Entity<Notebook>().HasIndex(n => new { n.UserId, n.UpdatedAtUtc });
        builder.Entity<Notebook>()
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CourseNotebook>().Property(n => n.ContentJson).HasColumnType("longtext");
        builder.Entity<CourseNotebook>().HasIndex(n => new { n.CourseId, n.OrderIndex });
        builder.Entity<CourseNotebook>()
            .HasOne<Course>()
            .WithMany()
            .HasForeignKey(n => n.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        // --- Enrollments: one per (user, course); LastLessonId is a nullable resume cache ---
        builder.Entity<Enrollment>()
            .HasIndex(e => new { e.UserId, e.CourseId })
            .IsUnique();

        builder.Entity<Enrollment>()
            .HasOne<Lesson>()
            .WithMany()
            .HasForeignKey(e => e.LastLessonId)
            .OnDelete(DeleteBehavior.SetNull);

        // Badge awards are permanent per-user history, independent of enrollment data.
        builder.Entity<UserBadgeAward>().HasIndex(a => new { a.UserId, a.BadgeId }).IsUnique();
        builder.Entity<UserBadgeAward>()
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // --- Progress: exactly one of Lesson/Challenge/SimulationTask per row ---
        builder.Entity<Progress>()
            .HasIndex(p => new { p.EnrollmentId, p.LessonId })
            .IsUnique();
        builder.Entity<Progress>()
            .HasIndex(p => new { p.EnrollmentId, p.ChallengeId })
            .IsUnique();
        builder.Entity<Progress>()
            .HasIndex(p => new { p.EnrollmentId, p.SimulationTaskId })
            .IsUnique();
        builder.Entity<Progress>()
            .HasIndex(p => new { p.EnrollmentId, p.CompletedUtc });

        builder.Entity<Progress>()
            .HasOne<Enrollment>()
            .WithMany()
            .HasForeignKey(p => p.EnrollmentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Progress>()
            .HasOne<Lesson>()
            .WithMany()
            .HasForeignKey(p => p.LessonId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Progress>()
            .HasOne<Challenge>()
            .WithMany()
            .HasForeignKey(p => p.ChallengeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Progress>()
            .HasOne<SimulationTask>()
            .WithMany()
            .HasForeignKey(p => p.SimulationTaskId)
            .OnDelete(DeleteBehavior.Restrict);

        // Matches schema.sql's CK_Progress_ExactlyOneContentRef. EF Core stores
        // the ProgressContentType enum as an int by declaration order:
        // Lesson=0, Challenge=1, Simulation=2.
        builder.Entity<Progress>().ToTable(t => t.HasCheckConstraint(
            "CK_Progress_ExactlyOneContentRef",
            "(ContentType = 0 AND LessonId IS NOT NULL AND ChallengeId IS NULL AND SimulationTaskId IS NULL) OR " +
            "(ContentType = 1 AND ChallengeId IS NOT NULL AND LessonId IS NULL AND SimulationTaskId IS NULL) OR " +
            "(ContentType = 2 AND SimulationTaskId IS NOT NULL AND LessonId IS NULL AND ChallengeId IS NULL)"));

        // --- ChallengeSubmissions: fast "already solved?" lookups ---
        builder.Entity<ChallengeSubmission>()
            .HasIndex(s => new { s.UserId, s.ChallengeId });

                // --- CTF Hints and Scoring ---

        // Hints belong to a challenge.
        builder.Entity<ChallengeHint>()
            .HasIndex(h => new { h.ChallengeId, h.DisplayOrder })
            .IsUnique();

        builder.Entity<ChallengeHint>()
            .HasOne<Challenge>()
            .WithMany()
            .HasForeignKey(h => h.ChallengeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Each learner can unlock each hint only once.
        builder.Entity<ChallengeHintUnlock>()
            .HasIndex(u => new { u.UserId, u.ChallengeHintId })
            .IsUnique();

        builder.Entity<ChallengeHintUnlock>()
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(u => u.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ChallengeHintUnlock>()
            .HasOne<ChallengeHint>()
            .WithMany()
            .HasForeignKey(u => u.ChallengeHintId)
            .OnDelete(DeleteBehavior.Cascade);

        // Each learner receives only one score award per challenge.
        builder.Entity<ChallengeScoreAward>()
            .HasIndex(a => new { a.UserId, a.ChallengeId })
            .IsUnique();

        builder.Entity<ChallengeScoreAward>()
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ChallengeScoreAward>()
            .HasOne<Challenge>()
            .WithMany()
            .HasForeignKey(a => a.ChallengeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}