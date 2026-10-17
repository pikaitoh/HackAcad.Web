using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace HackAcad.Web.Models.Entities;

public class ApplicationUser : IdentityUser
{
    public bool IsDisabled { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public DateTime JoinedDate { get; set; } = DateTime.UtcNow;
    [Column(TypeName = "mediumblob")]
    public byte[]? AvatarImage { get; set; }
}
