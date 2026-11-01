// Exercise the real provider request using fictional credentials and a handler that cannot send SMS.
using System.Net;
using System.Text;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

using var cache = new MemoryCache(new MemoryCacheOptions());
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
    ["Sms:TwilioAccountSid"] = "AC" + new string('1', 32),
    ["Sms:TwilioVerifyServiceSid"] = "VA" + new string('2', 32),
    ["Sms:TwilioAuthToken"] = "fictional-test-token"
}).Build();
using var handler = new Provider();
using var client = new HttpClient(handler);
var service = new PhoneVerification(cache, config, new EnvironmentFixture(), new ClientFactory(client), TimeProvider.System, NullLogger<PhoneVerification>.Instance);
var sent = await service.SendAsync("fictional-user", "fictional-stamp", "+15005550006");
if (sent is not null || handler.Sends != 1) throw new Exception("The provider did not accept the controlled request.");
Console.WriteLine("PASS SMS requests use the bound recipient and service-level branding without premium overrides");
var checkedCode = await service.CheckAsync("fictional-user", "fictional-stamp", "123456");
if (checkedCode.Number != "+15005550006" || checkedCode.Error is not null) throw new Exception("Verification no longer uses the returned verification SID.");
Console.WriteLine("PASS verification still uses the provider-issued SID and correct code");
var replay = await service.CheckAsync("fictional-user", "fictional-stamp", "123456");
if (replay.Number is not null || handler.Checks != 1) throw new Exception("An approved verification was reusable.");
Console.WriteLine("PASS approved codes remain one-use; no real SMS or network calls were made");

sealed class Provider : HttpMessageHandler
{
    public int Sends { get; private set; }
    public int Checks { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Post || request.RequestUri?.Host != "verify.twilio.com" || request.Headers.Authorization?.Scheme != "Basic") throw new Exception("Unexpected provider request.");
        var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
        var sid = "VE" + new string('3', 32);
        if (request.RequestUri.AbsolutePath.EndsWith("/Verifications", StringComparison.Ordinal))
        {
            if (form["To"] != "+15005550006" || form["Channel"] != "sms" || form.ContainsKey("CustomFriendlyName")) throw new Exception("Invalid SMS recipient, channel or unsupported premium branding override.");
            Sends++;
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"status\":\"pending\",\"sid\":\"" + sid + "\"}", Encoding.UTF8, "application/json") };
        }
        if (!request.RequestUri.AbsolutePath.EndsWith("/VerificationCheck", StringComparison.Ordinal) || form["VerificationSid"] != sid || form["Code"] != "123456") throw new Exception("Unexpected verification check.");
        Checks++;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"status\":\"approved\"}", Encoding.UTF8, "application/json") };
    }
}
sealed class ClientFactory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
sealed class EnvironmentFixture : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "PhoneProviderCheck";
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public string WebRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "wwwroot");
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
