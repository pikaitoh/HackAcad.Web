using Microsoft.AspNetCore.Identity;

namespace HackAcad.Web.Models.Entities;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public DateTime JoinedDate { get; set; } = DateTime.UtcNow;
}
