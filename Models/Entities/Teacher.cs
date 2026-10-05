using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HackAcad.Web.Models.Entities;

// Pure role marker. Courses.TeacherId points HERE, not at AspNetUsers directly —
// that's what makes "only Teachers own courses" a database-enforced fact.
public class Teacher
{
    [Key, ForeignKey(nameof(User))]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }
}
