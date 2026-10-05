using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- Database (MySQL / MariaDB via Pomelo provider) ---
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' not found. Add it to appsettings.Development.json.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    // AutoDetect needs a live connection. If MySQL isn't up yet, fall back to a
    // known server version so the site still starts and serves pages (seeding
    // and role resolution are each wrapped in their own try/catch too).
    ServerVersion serverVersion;
    try
    {
        serverVersion = ServerVersion.AutoDetect(connectionString);
    }
    catch (Exception)
    {
        serverVersion = new MySqlServerVersion(new Version(8, 0, 0));
    }

    options.UseMySql(connectionString, serverVersion);
});

// --- Identity: login/password only. Roles live in Students/Teachers/Admins,
// not AspNetRoles — see Data/RoleClaimsTransformation.cs and Database_Schema.md v3.0. ---
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultUI()
    .AddDefaultTokenProviders();

builder.Services.AddTransient<IClaimsTransformation, RoleClaimsTransformation>();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

// --- Seed a default Admin account ---
// Wrapped in try/catch on purpose: if MySQL isn't installed/running yet,
// the site still starts and serves pages, it just skips seeding and logs a
// warning. This lets you confirm "does dotnet run even work" before the
// database is set up.
using (var scope = app.Services.CreateScope())
{
    try
    {
        await DbInitializer.SeedAsync(scope.ServiceProvider);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "Database seeding skipped — is MySQL running and is the connection string in appsettings.Development.json correct?");
    }
}

app.Run();
