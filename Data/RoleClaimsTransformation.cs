using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Data;

// Makes Students/Teachers/Admins the single source of truth for role
// membership, instead of Identity's AspNetRoles. Runs once per request after
// login; attaches a standard Role claim so [Authorize(Roles = "Teacher")]
// keeps working exactly as before. See Database_Schema.md v3.0.
public class RoleClaimsTransformation : IClaimsTransformation
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<RoleClaimsTransformation> _logger;

    public RoleClaimsTransformation(ApplicationDbContext db, ILogger<RoleClaimsTransformation> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null || principal.HasClaim(c => c.Type == ClaimTypes.Role))
        {
            return principal; // not logged in, or already transformed this request
        }

        try
        {
            string? role = null;
            if (await _db.Admins.AnyAsync(a => a.UserId == userId))
            {
                role = "Admin";
            }
            else if (await _db.Teachers.AnyAsync(t => t.UserId == userId))
            {
                role = "Teacher";
            }
            else if (await _db.Students.AnyAsync(s => s.UserId == userId))
            {
                role = "Student";
            }

            if (role is not null && principal.Identity is ClaimsIdentity identity)
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
            }
        }
        catch (Exception ex)
        {
            // Database not reachable (e.g. MySQL not set up yet) — skip role
            // resolution so the site still serves pages. Same philosophy as the
            // seeding try/catch in Program.cs.
            _logger.LogWarning(ex, "Role claims resolution skipped — is MySQL running?");
        }

        return principal;
    }
}
