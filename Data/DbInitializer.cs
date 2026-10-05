using HackAcad.Web.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<ApplicationDbContext>();

        // Applies any pending EF Core migrations.
        await context.Database.MigrateAsync();

        // Default Admin account so there's always someone who can log in and
        // promote other users to Teacher. CHANGE THIS PASSWORD before any real demo.
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        const string adminEmail = "admin@hackacad.local";

        var admin = await userManager.FindByEmailAsync(adminEmail);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                DisplayName = "Platform Administrator",
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(admin, "Admin@123");
            if (!result.Succeeded)
            {
                return;
            }
        }

        // Role membership lives in the Admins table (v3.0), not AspNetRoles —
        // see Database_Schema.md and RoleClaimsTransformation.cs.
        if (!context.Admins.Any(a => a.UserId == admin.Id))
        {
            context.Admins.Add(new Admin { UserId = admin.Id });
            await context.SaveChangesAsync();
        }

        // Note: every other registration (via /Identity/Account/Register) should
        // insert a matching row into Students — that logic lives wherever your
        // team wires up registration (Member 1's module), not here.
    }
}
