
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

        // Identify courses whose curriculum content has changed.
        var changedCourseIds = ChangeTracker.Entries()
            .Where(e =>
                e.State is EntityState.Added
                    or EntityState.Modified
                    or EntityState.Deleted)
            .Where(e =>
                e.State != EntityState.Modified ||
                e.Properties.Any(p =>
                    p.IsModified &&
                    p.Metadata.Name != "UpdatedAtUtc"))
            .Select(e => e.Entity switch
            {
                Lesson lesson => (int?)lesson.CourseId,
                Challenge challenge => challenge.CourseId,
                SimulationTask task => task.CourseId,
                CourseNotebook notebook => notebook.CourseId,
                _ => null
            })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

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
            if (entry.State == EntityState.Deleted ||
                entry.State == EntityState.Unchanged)
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
