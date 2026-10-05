using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HackAcad.Web.Models.Entities;

// Pure role marker: a row here means this user currently holds the Student role.
// See Database_Schema.md v3.0 for why role membership lives here instead of
// AspNetRoles, and how RoleClaimsTransformation wires it into [Authorize(Roles=...)].
public class Student
{
    [Key, ForeignKey(nameof(User))]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }
}
