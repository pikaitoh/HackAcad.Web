using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using HackAcad.Web.Services;

namespace HackAcad.Web.Data;

// Resolve permissions from the role tables on each request, never from a
// posted form or an old role claim in a cookie.
public class RoleClaimsTransformation(ApplicationDbContext db, ILogger<RoleClaimsTransformation> logger) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        // External Google subjects are not local account IDs; preserve their temporary identity ticket.
        if (userId is null || principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated
            || identity.AuthenticationType != IdentityConstants.ApplicationScheme) return principal;
        foreach (var claim in identity.FindAll(identity.RoleClaimType).ToList()) identity.RemoveClaim(claim);
        try
        {
            var user = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new
            {
                u.IsDisabled,
                u.EmailConfirmed,
                IsAdmin = db.Admins.Any(a => a.UserId == u.Id),
                IsTeacher = db.Teachers.Any(t => t.UserId == u.Id),
                IsStudent = db.Students.Any(s => s.UserId == u.Id)
            }).SingleOrDefaultAsync();
            if (user is null || user.IsDisabled || !user.EmailConfirmed) return new ClaimsPrincipal(new ClaimsIdentity());
            var role = user.IsAdmin ? AccountRoles.Admin : user.IsTeacher ? AccountRoles.Teacher : user.IsStudent ? AccountRoles.Student : null;
            if (role is not null) identity.AddClaim(new Claim(identity.RoleClaimType, role));
        }
        catch (Exception ex)
        {
            // Fail closed: authenticated requests get no role if role lookup fails.
            logger.LogWarning(ex, "Unable to resolve account permissions.");
        }
        return principal;
    }
}
