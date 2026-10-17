using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using MimeKit;

namespace HackAcad.Web.Services;

// Gmail delivery uses a dedicated sender's offline grant, never a visitor's login token.
public class GmailEmail(HttpClient http, IConfiguration config)
{
    private string? ClientId => config["Email:GmailApi:ClientId"] ?? config["Authentication:Google:ClientId"];
    private string? ClientSecret => config["Email:GmailApi:ClientSecret"] ?? config["Authentication:Google:ClientSecret"];
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(config["Email:GmailApi:RefreshToken"]);

    public async Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        using var grant = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId!, ["client_secret"] = ClientSecret!,
            ["refresh_token"] = config["Email:GmailApi:RefreshToken"]!, ["grant_type"] = "refresh_token"
        });
        using var tokenResponse = await http.PostAsync("https://oauth2.googleapis.com/token", grant, cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (!token.TryGetProperty("access_token", out var access) || string.IsNullOrWhiteSpace(access.GetString()))
            throw new InvalidOperationException("Gmail authorization returned no access token.");
        using var content = new MemoryStream();
        await message.WriteToAsync(content, cancellationToken);
        using var send = new HttpRequestMessage(HttpMethod.Post, "https://gmail.googleapis.com/gmail/v1/users/me/messages/send");
        send.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.GetString());
        send.Content = JsonContent.Create(new { raw = WebEncoders.Base64UrlEncode(content.ToArray()) });
        using var sent = await http.SendAsync(send, cancellationToken);
        // Do not retry sends automatically: a lost response may already have delivered the message.
        sent.EnsureSuccessStatusCode();
    }
}
