using HackAcad.Web.Data;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Mvc;
using HackAcad.Web.Models.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// --- Database (MySQL / MariaDB via Pomelo provider) ---
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' not found. Add it to appsettings.Development.json.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    // AutoDetect needs a live connection. If MySQL isn't up yet, fall back to a
    // known server version so the site still starts and serves pages (migration
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

// --- Identity: password and external sign-in. Roles live in Students/Instructors/Admins,
// resolved on each request by Data/RoleClaimsTransformation.cs. ---
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedEmail = true;
        options.User.RequireUniqueEmail = true;
        // UserName is the email address; permit the ASCII email characters validated by the input model.
        options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+!#$%&'*+/=?^`{|}~";
        options.Password.RequiredLength = 8;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
    options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
}).AddIdentityCookies();

// Each provider is enabled only when its local OAuth credentials are configured.
builder.Services.AddHackAcadExternalAuthentication(builder.Configuration);
builder.Services.ConfigureExternalCookie(options => options.ExpireTimeSpan = TimeSpan.FromMinutes(10));

builder.Services.AddTransient<IClaimsTransformation, RoleClaimsTransformation>();

builder.Services.AddScoped<UserAccounts>();
builder.Services.AddScoped<UserTimeZone>();
builder.Services.AddScoped<EmailVerification>();
builder.Services.AddHttpClient<GmailEmail>(client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddScoped<AccountRecords>();
builder.Services.AddScoped<SecurityMonitor>();
builder.Services.AddScoped<AdminSecurity>();
builder.Services.AddScoped<AdminOperations>();
// ADMIN - Register the shared Admin gate and its verification/operation dependencies in request scope.
builder.Services.AddScoped<AdminSecurityFilter>();
builder.Services.AddScoped<SupportAccessService>();
builder.Services.AddAuthentication().AddCookie(SupportAccessService.Scheme, options => {
    options.Cookie.Name = "HackAcad.Support";
    options.Cookie.Path = "/Support";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = false;
    options.LoginPath = "/Support";
    options.AccessDeniedPath = "/Support";
    options.Events.OnValidatePrincipal = context => context.HttpContext.RequestServices.GetRequiredService<SupportAccessService>().ValidateAsync(context);
});
builder.Services.AddScoped<AccountConnections>();
builder.Services.AddScoped<AccountSessions>();
builder.Services.AddScoped<BadgeService>();
builder.Services.AddScoped<ActivityCalendarService>();
builder.Services.AddScoped<InstructorApplications>();
 builder.Services.AddScoped<Personalization>();
builder.Services.AddScoped<NotebookExportService>();
builder.Services.AddSingleton<PhoneVerification>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(SupportAccessService.Scheme)
    .Configure<TimeProvider>((options, clock) => options.TimeProvider = clock);
builder.Services.AddHttpClient("twilio-verify", client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddMemoryCache();
builder.Services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromMinutes(10));
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("password-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 30, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy("support-access", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 10, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy("support-message", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 20, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy("verification-email", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 30, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy("phone-otp-send", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 15, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy("email-change", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 10, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy("phone-otp-check", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 30, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy("security-action", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 20, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy("two-factor-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 30, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = "600";
        await context.HttpContext.Response.WriteAsync("Too many verification requests. Please wait 10 minutes and try again.", token);
    };
});
// Check security stamps on every request so disabling / changing roles revokes
// existing cookies immediately. Suitable for this locally hosted class project.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.Zero;
    options.OnRefreshingPrincipal = context =>
    {
        if (context.NewPrincipal?.Identity is ClaimsIdentity identity)
            foreach (var type in new[] { AccountSessions.ClaimName, AccountSessions.GoogleTimeClaim, AccountSessions.ExternalTimeClaim, AccountSessions.ExternalProviderClaim })
                if (context.CurrentPrincipal?.FindFirst(type) is { } claim) identity.AddClaim(claim);
        return Task.CompletedTask;
    };
});
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Events.OnSigningIn = context => context.HttpContext.RequestServices.GetRequiredService<AccountSessions>().SigningInAsync(context);
    options.Events.OnValidatePrincipal = context => context.HttpContext.RequestServices.GetRequiredService<AccountSessions>().ValidateAsync(context);
});
builder.Services.AddControllersWithViews(options => {
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    // ADMIN - Apply the Admin policy centrally so new actions cannot bypass it by omitting a local check.
    options.Filters.AddService<AdminSecurityFilter>();
});
builder.Services.AddRazorPages();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/uploads/ctf"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next(context);
});
app.UseStaticFiles();

app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var user = await context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>().GetUserAsync(context.User);
        context.RequestServices.GetRequiredService<UserTimeZone>().Use(user?.TimeZoneId);
    }
    await next(context);
});
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

// Apply schema migrations at startup; accounts and course content are managed through the application.
// Keep the site available with a warning when the local database cannot be reached.
using (var scope = app.Services.CreateScope())
{
    try
    {
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "Database migration failed — is MySQL running and is the connection string in appsettings.Development.json correct?");
    }
}

app.Run();
