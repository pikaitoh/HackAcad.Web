using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HackAcad.Web.Models.Entities;

// Pure role marker.
public class Admin
{
    [Key, ForeignKey(nameof(User))]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }
}
