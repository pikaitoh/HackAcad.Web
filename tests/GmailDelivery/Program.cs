using System.Net;
using System.Text;
using System.Text.Json;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using MimeKit;

// All credentials, senders and recipients are fictional. This handler never opens a network connection.
var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
{
    ["Authentication:Google:ClientId"]="unit-client", ["Authentication:Google:ClientSecret"]="unit-secret",
    ["Email:GmailApi:RefreshToken"]="unit-refresh"
}).Build();
var checks=0;
void Check(bool ok,string message) { if(!ok) throw new Exception(message);checks++;Console.WriteLine("PASS "+message); }
MimeMessage Message(string recipient)
{
    var message=new MimeMessage();message.From.Add(new MailboxAddress("HackAcad","sender@gmail.com"));
    message.To.Add(MailboxAddress.Parse(recipient));message.Subject="Verify your HackAcad email";
    message.Body=new TextPart("plain"){Text="Open this link within 10 minutes: https://hackacad.example/Identity/Account/ConfirmEmail?code=unit-token"};
    return message;
}
foreach(var recipient in new[]{"learner@yahoo.com","learner@outlook.com","learner@school.edu.my"})
{
    var handler=new FakeGoogle();var gmail=new GmailEmail(new HttpClient(handler),config);
    Check(gmail.IsConfigured,"OAuth credentials and refresh token enable API delivery");
    await gmail.SendAsync(Message(recipient),CancellationToken.None);
    Check(handler.Requests==2 && handler.To==recipient && handler.From=="sender@gmail.com", "Gmail API sends MIME mail to "+recipient);
    Check(handler.Text!.Contains("within 10 minutes") && !handler.Text.Contains("unit-refresh"),"verification content excludes the sender refresh token");
}
foreach(var mode in new[]{"invalid-grant","missing-token","bad-json","forbidden","rate-limited","timeout"})
{
    var handler=new FakeGoogle{Mode=mode};var failed=false;
    try { await new GmailEmail(new HttpClient(handler),config).SendAsync(Message("learner@yahoo.com"),CancellationToken.None); }
    catch(Exception error) when(error is HttpRequestException or InvalidOperationException or JsonException or OperationCanceledException) { failed=true; }
    Check(failed && handler.Requests==(mode is "forbidden" or "rate-limited"?2:1),mode+" fails without an automatic duplicate send");
}
config["Email:GmailApi:RefreshToken"]="";
Check(!new GmailEmail(new HttpClient(new FakeGoogle()),config).IsConfigured,"missing sender authorization cannot enable real delivery");
Console.WriteLine($"All {checks} Gmail delivery assertions passed. No real email sent.");

sealed class FakeGoogle:HttpMessageHandler
{
    public string Mode="success";public int Requests;public string? To,From,Text;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        Requests++;
        if(request.RequestUri!.AbsoluteUri=="https://oauth2.googleapis.com/token")
        {
            var form=QueryHelpers.ParseQuery("?"+await request.Content!.ReadAsStringAsync(cancellationToken));
            if(request.Method!=HttpMethod.Post || form["grant_type"]!="refresh_token" || form["client_id"]!="unit-client" || form["client_secret"]!="unit-secret" || form["refresh_token"]!="unit-refresh") throw new Exception("Incorrect token refresh request");
            if(Mode=="timeout") throw new TaskCanceledException();
            return Mode switch
            {
                "invalid-grant"=>Response(HttpStatusCode.BadRequest,"{\"error\":\"invalid_grant\"}"),
                "missing-token"=>Response(HttpStatusCode.OK,"{}"),
                "bad-json"=>Response(HttpStatusCode.OK,"not-json"),
                _=>Response(HttpStatusCode.OK,"{\"access_token\":\"unit-access\",\"token_type\":\"Bearer\"}")
            };
        }
        if(request.RequestUri.AbsoluteUri!="https://gmail.googleapis.com/gmail/v1/users/me/messages/send" || request.Method!=HttpMethod.Post
            || request.Headers.Authorization?.Scheme!="Bearer" || request.Headers.Authorization.Parameter!="unit-access") throw new Exception("Incorrect Gmail send request");
        var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        var raw=json.RootElement.GetProperty("raw").GetString()!;
        if(raw.Contains('=') || raw.Contains('+') || raw.Contains('/')) throw new Exception("MIME must use unpadded base64url");
        using var stream=new MemoryStream(WebEncoders.Base64UrlDecode(raw));var message=MimeMessage.Load(stream);
        To=message.To.Mailboxes.Single().Address;From=message.From.Mailboxes.Single().Address;Text=message.TextBody;
        return Response(Mode=="forbidden"?HttpStatusCode.Forbidden:Mode=="rate-limited"?HttpStatusCode.TooManyRequests:HttpStatusCode.OK,"{}");
    }
    private static HttpResponseMessage Response(HttpStatusCode status,string json)=>new(status){Content=new StringContent(json,Encoding.UTF8,"application/json")};
}
