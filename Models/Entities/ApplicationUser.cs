using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HackAcad.Web.Models.Entities;

public class ApplicationUser : IdentityUser
{
    public bool IsDisabled { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    [MaxLength(100)]
    public string? FullName { get; set; }
    public DateTime JoinedDate { get; set; } = DateTime.UtcNow;
    [Column(TypeName = "mediumblob")]
    public byte[]? AvatarImage { get; set; }
}
