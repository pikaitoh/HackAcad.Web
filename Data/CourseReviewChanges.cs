using HackAcad.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Data;

public partial class ApplicationDbContext
{
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        // Every curriculum write also updates the parent course in the same transaction.
        // Review decisions and concurrent content edits therefore cannot approve different content.
        var changed = ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => e.State != EntityState.Modified || e.Properties.Any(p => p.IsModified && p.Metadata.Name != "UpdatedAtUtc"))
            .Select(e => e.Entity switch {
                Lesson lesson => (int?)lesson.CourseId,
                Challenge challenge => challenge.CourseId,
                SimulationTask task => task.CourseId,
                CourseNotebook notebook => notebook.CourseId,
                _ => null
            }).Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
        foreach (var id in changed)
            if (!Courses.Local.Any(c => c.CourseId == id)) await Courses.FindAsync([id], cancellationToken);
        foreach (var entry in ChangeTracker.Entries<Course>().ToList())
        {
            if (entry.State == EntityState.Deleted) continue;
            var contentChanged = changed.Contains(entry.Entity.CourseId)
                || (entry.State == EntityState.Modified && entry.Properties.Any(p => p.IsModified
                    && p.Metadata.Name is nameof(Course.Title) or nameof(Course.Description) or nameof(Course.Category) or nameof(Course.ThumbnailUrl)));
            if (contentChanged)
            {
                entry.Entity.ReviewStatus = CourseReviewStatus.Draft;
                entry.Entity.IsPublished = false;
                entry.Entity.SubmittedForReviewUtc = null;
                entry.Entity.ReviewedUtc = null;
            }
            if (entry.Entity.ReviewStatus != CourseReviewStatus.Approved) entry.Entity.IsPublished = false;
            if (contentChanged || entry.State == EntityState.Modified) entry.Entity.ReviewVersion = Guid.NewGuid().ToString("N");
        }
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
