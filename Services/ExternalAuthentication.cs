using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Identity;

namespace HackAcad.Web.Services;

public static class ExternalAuthentication
{
    public static readonly string[] Providers = ["Google", "GitHub"];

    public static IServiceCollection AddHackAcadExternalAuthentication(this IServiceCollection services, IConfiguration config)
    {
        bool Configured(string provider) => !string.IsNullOrWhiteSpace(config[$"Authentication:{provider}:ClientId"])
            && !string.IsNullOrWhiteSpace(config[$"Authentication:{provider}:ClientSecret"]);
        void Configure(OAuthOptions options, string provider)
        {
            options.ClientId = config[$"Authentication:{provider}:ClientId"]!;
            options.ClientSecret = config[$"Authentication:{provider}:ClientSecret"]!;
            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.UsePkce = true;
            options.SaveTokens = false;
            options.Events.OnRemoteFailure = async context =>
            {
                await context.HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                context.HandleResponse();
                context.Response.Redirect($"/Identity/Account/Login?externalError={provider}");
            };
        }
        var auth = services.AddAuthentication();
        if (Configured("Google")) auth.AddGoogle(options =>
        {
            Configure(options, "Google");
            options.ClaimActions.MapJsonKey("google:email_verified", "email_verified");
            options.ClaimActions.MapJsonKey("google:hosted_domain", "hd");
        });
        if (Configured("GitHub")) auth.AddOAuth("GitHub", "GitHub", options =>
        {
            Configure(options, "GitHub");
            options.CallbackPath = "/signin-github";
            options.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
            options.TokenEndpoint = "https://github.com/login/oauth/access_token";
            options.UserInformationEndpoint = "https://api.github.com/user";
            options.Scope.Add("user:email");
            options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
            options.ClaimActions.MapJsonKey(ClaimTypes.Name, "login");
            options.Events.OnCreatingTicket = async context =>
            {
                async Task<JsonDocument> Get(string url)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
                    request.Headers.UserAgent.ParseAdd("HackAcad/1.0");
                    request.Headers.Accept.ParseAdd("application/vnd.github+json");
                    request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
                    using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
                    response.EnsureSuccessStatusCode();
                    return JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
                }
                using var profile = await Get(context.Options.UserInformationEndpoint);
                context.RunClaimActions(profile.RootElement);
                // The public profile email can be private/null or unverified. Use the protected email list.
                using var emails = await Get("https://api.github.com/user/emails?per_page=100");
                var primary = emails.RootElement.EnumerateArray().FirstOrDefault(e =>
                    e.TryGetProperty("primary", out var p) && p.ValueKind == JsonValueKind.True
                    && e.TryGetProperty("verified", out var v) && v.ValueKind == JsonValueKind.True);
                if (primary.ValueKind == JsonValueKind.Object && primary.TryGetProperty("email", out var email)
                    && email.ValueKind == JsonValueKind.String && email.GetString() is { Length: > 0 and <= 256 } address)
                {
                    context.Identity!.AddClaim(new Claim(ClaimTypes.Email, address));
                    context.Identity.AddClaim(new Claim("github:email_verified", "true"));
                }
            };
        });
        return services;
    }
}
