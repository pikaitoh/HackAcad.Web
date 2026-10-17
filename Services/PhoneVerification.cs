using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace HackAcad.Web.Services;

public record PhoneCheckResult(string? Number, string? Error);

// Contact-number verification only. This does not enable SMS login, recovery or two-factor authentication.
public class PhoneVerification(IMemoryCache cache, IConfiguration config, IWebHostEnvironment environment,
    IHttpClientFactory clients, TimeProvider clock, ILogger<PhoneVerification> logger)
{
    private static readonly object Gate = new();
    private sealed class Pending
    {
        public required string Number { get; init; }
        public required string Stamp { get; init; }
        public required DateTimeOffset Expires { get; init; }
        public string? Sid { get; set; }
        public string? Salt { get; set; }
        public byte[]? Hash { get; set; }
        public int Attempts { get; set; }
        public bool Busy { get; set; } = true;
    }
    private sealed class SendCounter
    {
        public DateTimeOffset Last { get; set; }
        public int Count { get; set; }
    }
    public bool IsTestMode => environment.IsDevelopment() && !string.IsNullOrWhiteSpace(config["Sms:DevelopmentPickupDirectory"]);
    private bool IsConfigured => Regex.IsMatch(config["Sms:TwilioAccountSid"] ?? "", "^AC[0-9a-fA-F]{32}$")
        && Regex.IsMatch(config["Sms:TwilioVerifyServiceSid"] ?? "", "^VA[0-9a-fA-F]{32}$")
        && !string.IsNullOrWhiteSpace(config["Sms:TwilioAuthToken"]);

    public string? PendingNumber(string userId, string stamp)
    {
        lock (Gate) return Current(userId, stamp)?.Number;
    }
    public void Cancel(string userId) { lock (Gate) cache.Remove("phone-pending:" + userId); }

    private Pending? Current(string userId, string stamp)
    {
        if (!cache.TryGetValue<Pending>("phone-pending:" + userId, out var pending)) return null;
        if (pending!.Expires <= clock.GetUtcNow() || pending.Stamp != stamp)
        { cache.Remove("phone-pending:" + userId); return null; }
        return pending;
    }

    public async Task<string?> SendAsync(string userId, string stamp, string number)
    {
        if (!Regex.IsMatch(number, "^\\+[1-9][0-9]{7,14}$")) return "Enter a phone number with its country code, such as +60123456789.";
        if (!IsTestMode && !IsConfigured) return "SMS verification is not available yet. Please contact an administrator.";
        var now = clock.GetUtcNow();
        var pending = new Pending { Number = number, Stamp = stamp, Expires = now.AddMinutes(10) };
        lock (Gate)
        {
            var userCounter = cache.GetOrCreate("phone-send-user:" + userId, entry =>
            { entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1); return new SendCounter(); })!;
            var numberCounter = cache.GetOrCreate("phone-send-number:" + number, entry =>
            { entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1); return new SendCounter(); })!;
            if (now - userCounter.Last < TimeSpan.FromSeconds(60)) return "Wait one minute before requesting another SMS.";
            if (userCounter.Count >= 5 || numberCounter.Count >= 5) return "Too many SMS requests. Please try again in an hour.";
            userCounter.Last = now; userCounter.Count++; numberCounter.Count++;
            cache.Set("phone-pending:" + userId, pending, TimeSpan.FromMinutes(10));
        }
        try
        {
            if (IsTestMode)
            {
                var directory = Path.GetFullPath(config["Sms:DevelopmentPickupDirectory"]!, environment.ContentRootPath);
                var webRoot = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
                if (directory.Equals(webRoot, StringComparison.OrdinalIgnoreCase)
                    || directory.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("SMS capture must be outside wwwroot.");
                var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
                pending.Salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                pending.Hash = SHA256.HashData(Encoding.UTF8.GetBytes(pending.Salt + code));
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(Path.Combine(directory, Guid.NewGuid() + ".json"),
                    JsonSerializer.Serialize(new { userId, number, code, expires = pending.Expires }));
            }
            else
            {
                var sent = await RequestAsync("Verifications", new() { ["To"] = number, ["Channel"] = "sms" });
                if (!sent.Success || sent.Status != "pending" || !Regex.IsMatch(sent.Sid ?? "", "^VE[0-9a-fA-F]{32}$"))
                    throw new InvalidOperationException("SMS provider did not accept verification.");
                pending.Sid = sent.Sid;
            }
            lock (Gate)
            {
                if (!ReferenceEquals(Current(userId, stamp), pending)) return "The request changed. Please request another code.";
                pending.Busy = false;
            }
            return null;
        }
        catch (Exception error)
        {
            lock (Gate)
                if (ReferenceEquals(Current(userId, stamp), pending)) cache.Remove("phone-pending:" + userId);
            logger.LogWarning("SMS verification could not be started ({ErrorType}).", error.GetType().Name);
            return "We couldn't send the SMS. Wait one minute and try again, or contact an administrator.";
        }
    }

    public async Task<PhoneCheckResult> CheckAsync(string userId, string stamp, string code)
    {
        Pending pending;
        lock (Gate)
        {
            var current = Current(userId, stamp);
            if (current is null) return new(null, "This code has expired or no request is pending. Request another SMS.");
            if (current.Busy) return new(null, "A request is already in progress. Please wait.");
            if (current.Attempts >= 5)
            { cache.Remove("phone-pending:" + userId); return new(null, "This code reached its attempt limit. Request another SMS."); }
            if (!Regex.IsMatch(code, "^[0-9]{6}$")) return new(null, "Enter the six-digit code from your SMS.");
            pending = current; pending.Busy = true; pending.Attempts++;
        }
        var approved = false;
        var expired = false;
        try
        {
            if (pending.Hash is not null)
                approved = CryptographicOperations.FixedTimeEquals(pending.Hash,
                    SHA256.HashData(Encoding.UTF8.GetBytes(pending.Salt + code)));
            else
            {
                if (!IsConfigured) return new(null, "SMS verification is unavailable. Please try again later.");
                var result = await RequestAsync("VerificationCheck", new() { ["VerificationSid"] = pending.Sid!, ["Code"] = code });
                approved = result.Success && result.Status == "approved";
                expired = result.HttpStatus is HttpStatusCode.NotFound || result.Code == 60202;
                if (!result.Success && !expired && result.HttpStatus != HttpStatusCode.BadRequest)
                    return new(null, "SMS verification is unavailable. Please try again later.");
            }
        }
        catch (Exception error)
        {
            logger.LogWarning("SMS verification check failed ({ErrorType}).", error.GetType().Name);
            return new(null, "SMS verification is unavailable. Please try again later.");
        }
        finally { lock (Gate) pending.Busy = false; }
        lock (Gate)
        {
            if (!ReferenceEquals(Current(userId, stamp), pending)) return new(null, "This request changed or expired. Request another SMS.");
            if (approved || expired || pending.Attempts >= 5) cache.Remove("phone-pending:" + userId);
            if (approved) return new(pending.Number, null);
            return new(null, expired || pending.Attempts >= 5
                ? "This code has expired or reached its attempt limit. Request another SMS."
                : "That code isn't correct. Check your SMS and try again.");
        }
    }

    private async Task<(bool Success, string? Status, string? Sid, int? Code, HttpStatusCode HttpStatus)> RequestAsync(
        string resource, Dictionary<string, string> values)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://verify.twilio.com/v2/Services/{config["Sms:TwilioVerifyServiceSid"]}/{resource}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(
            Encoding.UTF8.GetBytes(config["Sms:TwilioAccountSid"] + ":" + config["Sms:TwilioAuthToken"])));
        request.Content = new FormUrlEncodedContent(values);
        using var response = await clients.CreateClient("twilio-verify").SendAsync(request);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = json.RootElement;
        return (response.IsSuccessStatusCode,
            root.TryGetProperty("status", out var status) ? status.GetString() : null,
            root.TryGetProperty("sid", out var sid) ? sid.GetString() : null,
            root.TryGetProperty("code", out var error) && error.ValueKind == JsonValueKind.Number && error.TryGetInt32(out var value) ? value : null, response.StatusCode);
    }
}
