using HackAcad.Web.Models.Entities;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        // Shared development fixtures, not production credentials. Existing
        // accounts (including the original admin) are never reset or re-promoted.
        if (!services.GetRequiredService<IWebHostEnvironment>().IsDevelopment()) return;
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var accounts = services.GetRequiredService<UserAccounts>();
        foreach (var role in AccountRoles.All)
        {
            var email = $"demo.{role.ToLowerInvariant()}@hackacad.local";
            if (await users.FindByEmailAsync(email) is not null) continue;
            var result = await accounts.CreateAsync(new ApplicationUser
            {
                UserName = email, Email = email, DisplayName = $"Demo {role}", EmailConfirmed = true
            }, "HackAcad@123!", role);
            if (!result.Succeeded)
                throw new InvalidOperationException($"Could not create the {role} development fixture: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
    }
}
