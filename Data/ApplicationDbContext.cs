using HackAcad.Web.Models.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Role tables (v3.0) — the single source of truth for role membership.
    // AspNetRoles/AspNetUserRoles still exist (from IdentityDbContext) but are
    // intentionally left unused. See Database_Schema.md v3.0.
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<Admin> Admins => Set<Admin>();

    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<Progress> ProgressRecords => Set<Progress>();
    public DbSet<Challenge> Challenges => Set<Challenge>();
    public DbSet<ChallengeSubmission> ChallengeSubmissions => Set<ChallengeSubmission>();
    public DbSet<SimulationTask> SimulationTasks => Set<SimulationTask>();
    public DbSet<LoginRecord> LoginRecords => Set<LoginRecord>();
    public DbSet<AdminActivity> AdminActivities => Set<AdminActivity>();
    public DbSet<AccountSession> AccountSessions => Set<AccountSession>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<LoginRecord>().HasIndex(r => new { r.UserId, r.OccurredUtc });
        builder.Entity<LoginRecord>().HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<AccountSession>().HasIndex(r => new { r.UserId, r.ExpiresUtc });
        builder.Entity<AccountSession>().HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<AdminActivity>().HasIndex(r => new { r.ActorUserId, r.OccurredUtc });
        // Keep the audit entry when its actor/target is removed; only the actor relation is cleared.
        builder.Entity<AdminActivity>().HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.ActorUserId).OnDelete(DeleteBehavior.SetNull);

        // --- Role tables: UserId is PK and FK to AspNetUsers in one step ---
        builder.Entity<Student>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Teacher>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Admin>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // --- Courses: TeacherId points at Teachers, not AspNetUsers directly ---
        builder.Entity<Course>()
            .HasOne<Teacher>()
            .WithMany()
            .HasForeignKey(c => c.TeacherId)
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

        // --- Enrollments: one per (user, course); LastLessonId is a nullable resume cache ---
        builder.Entity<Enrollment>()
            .HasIndex(e => new { e.UserId, e.CourseId })
            .IsUnique();

        builder.Entity<Enrollment>()
            .HasOne<Lesson>()
            .WithMany()
            .HasForeignKey(e => e.LastLessonId)
            .OnDelete(DeleteBehavior.SetNull);

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
    }
}
