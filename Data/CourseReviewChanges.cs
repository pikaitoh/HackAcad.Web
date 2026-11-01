
using HackAcad.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Data;

// ADMIN - Invalidate approval when course content changes.

public partial class ApplicationDbContext
{
    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();

        // ADMIN - Child edits must invalidate the parent even when the Course itself is unchanged.
        var changedEntries = ChangeTracker.Entries()
            .Where(e =>
                e.State is EntityState.Added
                    or EntityState.Modified
                    or EntityState.Deleted)
            .Where(e =>
                e.State != EntityState.Modified ||
                e.Properties.Any(p =>
                    p.IsModified &&
                    p.Metadata.Name != "UpdatedAtUtc"))
            .ToList();

        // Include both parents if an activity moves between courses.
        var changedCourseIds = changedEntries
            .Where(e => e.Entity is Lesson or Challenge or SimulationTask or CourseNotebook)
            .SelectMany(e => new[]
            {
                (int)e.Property(nameof(Lesson.CourseId)).CurrentValue!,
                (int)e.Property(nameof(Lesson.CourseId)).OriginalValue!
            })
            .Where(id => id > 0)
            .ToHashSet();

        // Hints reference a challenge rather than a course directly.
        var hintChallengeIds = changedEntries
            .Where(e => e.Entity is ChallengeHint)
            .SelectMany(e => new[]
            {
                (int)e.Property(nameof(ChallengeHint.ChallengeId)).CurrentValue!,
                (int)e.Property(nameof(ChallengeHint.ChallengeId)).OriginalValue!
            })
            .Where(id => id > 0)
            .Distinct()
            .ToArray();
        if (hintChallengeIds.Length > 0)
        {
            changedCourseIds.UnionWith(await Challenges.AsNoTracking()
                .Where(c => hintChallengeIds.Contains(c.ChallengeId))
                .Select(c => c.CourseId)
                .ToListAsync(cancellationToken));
        }

        // Load affected parent courses.
        foreach (var id in changedCourseIds)
        {
            if (!Courses.Local.Any(c => c.CourseId == id))
            {
                await Courses.FindAsync(
                    [id],
                    cancellationToken);
            }
        }

        // Only process courses that are actually being modified
        // or whose curriculum content has changed.
        foreach (var entry in ChangeTracker.Entries<Course>().ToList())
        {
            if (entry.State == EntityState.Deleted)
            {
                continue;
            }

            var contentChanged =
                changedCourseIds.Contains(entry.Entity.CourseId) ||
                (entry.State == EntityState.Modified &&
                 entry.Properties.Any(p =>
                     p.IsModified &&
                     p.Metadata.Name is
                         nameof(Course.Title) or
                         nameof(Course.Description) or
                         nameof(Course.Category) or
                         nameof(Course.ThumbnailUrl)));

            if (contentChanged)
            {
                entry.Entity.ReviewStatus = CourseReviewStatus.Draft;
                entry.Entity.IsPublished = false;
                entry.Entity.SubmittedForReviewUtc = null;
                entry.Entity.ReviewedUtc = null;
                entry.Entity.ReviewVersion =
                    Guid.NewGuid().ToString("N");
            }
        }

        return await base.SaveChangesAsync(
            acceptAllChangesOnSuccess,
            cancellationToken);
    }
}
