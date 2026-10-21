using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HackAcad.Web.Models.Entities;

// Pure role marker. Courses.InstructorId points HERE, not at users directly —
// that's what makes "only Instructors own courses" a database-enforced fact.
public class Instructor
{
    [Key, ForeignKey(nameof(User))]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }
}
