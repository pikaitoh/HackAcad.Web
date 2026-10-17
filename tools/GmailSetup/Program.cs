using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// Run locally from the repository. Tokens are saved only to the ignored development settings.
const string redirect = "http://localhost:53682/";
const string sendScope = "https://www.googleapis.com/auth/gmail.send";
var root = new DirectoryInfo(Directory.GetCurrentDirectory());
while (root is not null && !File.Exists(Path.Combine(root.FullName, "HackAcad.Web.csproj"))) root = root.Parent;
if (root is null) { Console.Error.WriteLine("Run this command from the HackAcad.Web folder."); return 1; }
var path = Path.Combine(root.FullName, "appsettings.Development.json");
try
{
    var config = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
    string? Setting(string section, string key) => config["Email"]?["GmailApi"]?[key]?.GetValue<string>() ?? config[section]?["Google"]?[key]?.GetValue<string>();
    var clientId = Setting("Authentication", "ClientId");
    var clientSecret = Setting("Authentication", "ClientSecret");
    if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        throw new InvalidOperationException("Configure the Google OAuth client locally first.");
    if (args.Contains("--check"))
    {
        Console.WriteLine("OAuth client is configured. Gmail sender authorization: " +
            (string.IsNullOrWhiteSpace(config["Email"]?["GmailApi"]?["RefreshToken"]?.GetValue<string>()) ? "still required." : "stored locally (not tested online)."));
        return 0;
    }
    using var listener = new HttpListener();
    listener.Prefixes.Add(redirect);
    listener.Start();
    string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    var state = Encode(RandomNumberGenerator.GetBytes(32));
    var verifier = Encode(RandomNumberGenerator.GetBytes(32));
    var query = new Dictionary<string,string>
    {
        ["client_id"]=clientId, ["redirect_uri"]=redirect, ["response_type"]="code",
        ["scope"]="openid email " + sendScope, ["access_type"]="offline", ["prompt"]="consent select_account",
        ["state"]=state, ["code_challenge"]=Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), ["code_challenge_method"]="S256"
    };
    Console.WriteLine("Select the HackAcad sender Gmail account in the browser and authorize sending email.");
    Console.WriteLine("Cloud setup required: enable Gmail API, add the sender as a test user, and add redirect URI " + redirect);
    var url = "https://accounts.google.com/o/oauth2/v2/auth?" + string.Join("&", query.Select(pair => Uri.EscapeDataString(pair.Key)+"="+Uri.EscapeDataString(pair.Value)));
    Process.Start(new ProcessStartInfo(url) { UseShellExecute=true });
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    var callback = await listener.GetContextAsync().WaitAsync(timeout.Token);
    var valid = callback.Request.Url?.AbsolutePath == "/" && callback.Request.QueryString["state"] == state
        && string.IsNullOrEmpty(callback.Request.QueryString["error"]) && !string.IsNullOrEmpty(callback.Request.QueryString["code"]);
    callback.Response.StatusCode = valid ? 200 : 400;
    callback.Response.ContentType = "text/plain; charset=utf-8";
    callback.Response.Headers["Cache-Control"]="no-store";
    callback.Response.Headers["Referrer-Policy"]="no-referrer";
    await callback.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(valid ? "Authorization received. Check the terminal for the result. You can close this tab." : "Authorization failed or was cancelled. Close this tab and retry from the terminal."));
    callback.Response.Close();
    if (!valid) throw new InvalidOperationException("Authorization was cancelled or its state did not match. Nothing was saved.");
    using var http = new HttpClient { Timeout=TimeSpan.FromSeconds(20) };
    using var grant = new FormUrlEncodedContent(new Dictionary<string,string>
    {
        ["client_id"]=clientId, ["client_secret"]=clientSecret, ["code"]=callback.Request.QueryString["code"]!,
        ["redirect_uri"]=redirect, ["grant_type"]="authorization_code", ["code_verifier"]=verifier
    });
    using var exchange = await http.PostAsync("https://oauth2.googleapis.com/token",grant,timeout.Token);
    exchange.EnsureSuccessStatusCode();
    var tokens = await exchange.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
    if (!tokens.TryGetProperty("refresh_token",out var refresh) || string.IsNullOrWhiteSpace(refresh.GetString()))
        throw new InvalidOperationException("No offline authorization received. Run again and grant access.");
    if (!tokens.TryGetProperty("scope",out var scope) || !(scope.GetString()??"").Split(' ').Contains(sendScope))
        throw new InvalidOperationException("Sending permission was not granted. Nothing was saved.");
    using var identityRequest = new HttpRequestMessage(HttpMethod.Get,"https://www.googleapis.com/oauth2/v3/userinfo");
    identityRequest.Headers.Authorization=new AuthenticationHeaderValue("Bearer",tokens.GetProperty("access_token").GetString());
    using var identityResponse=await http.SendAsync(identityRequest,timeout.Token);
    identityResponse.EnsureSuccessStatusCode();
    var identity=await identityResponse.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
    if (!identity.TryGetProperty("email_verified",out var verified) || verified.ValueKind!=JsonValueKind.True)
        throw new InvalidOperationException("The sender email is not verified. Nothing was saved.");
    var email=identity.GetProperty("email").GetString()!;
    var expected=config["Email"]?["GmailAddress"]?.GetValue<string>();
    if (!string.IsNullOrWhiteSpace(expected) && !expected.StartsWith("YOUR_",StringComparison.OrdinalIgnoreCase)
        && !string.Equals(expected,email,StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("The selected Google account does not match Email:GmailAddress. Choose the HackAcad sender account and retry. Nothing was saved.");
    // Re-read so unrelated edits made while authorizing are preserved.
    var latest=JsonNode.Parse(await File.ReadAllTextAsync(path))!;
    latest["Email"]??=new JsonObject();
    latest["Email"]!["GmailAddress"]=email;
    latest["Email"]!["GmailApi"]??=new JsonObject();
    latest["Email"]!["GmailApi"]!["RefreshToken"]=refresh.GetString();
    await File.WriteAllTextAsync(path,latest.ToJsonString(new JsonSerializerOptions { WriteIndented=true }));
    Console.WriteLine("Gmail sending authorization saved to ignored local settings. No email was sent. Restart HackAcad to use it.");
    Console.WriteLine("If the Cloud OAuth app is in Testing, Google expires this refresh token after 7 days; run setup again when needed.");
    return 0;
}
catch (Exception error)
{
    // Do not print provider responses, authorization codes, credentials or token values.
    Console.Error.WriteLine(error is InvalidOperationException ? error.Message : "Gmail setup failed ("+error.GetType().Name+"). Check Cloud setup and try again; no token values are printed.");
    return 1;
}
