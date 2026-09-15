using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sacco.Modules.Notifications.Channels;
using Shouldly;

namespace Sacco.UnitTests.Notifications;

/// <summary>
/// Every gateway behind <see cref="ISmsSender"/> is driven through a fake HTTP handler: request shape, auth, and
/// success/failure mapping. This is what makes the provider swap in <c>NotificationsModule.RegisterLiveSmsSender</c> a
/// configuration change — every sender honours the same contract regardless of the wire format underneath.
/// </summary>
public sealed class SmsProvidersTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];
        public Func<HttpRequestMessage, string, (HttpStatusCode, string)> Respond { get; set; } = (_, _) => (HttpStatusCode.OK, "{}");
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request, body));
            var (status, json) = Respond(request, body);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static HttpClient ClientOf(FakeHandler handler) => new(handler, disposeHandler: false);

    [Fact]
    public async Task Twilio_sends_basic_auth_and_form_body_and_reads_the_message_sid()
    {
        var handler = new FakeHandler { Respond = (_, _) => (HttpStatusCode.Created, """{"sid":"SM123","status":"queued"}""") };
        var settings = Options.Create(new NotificationChannelSettings { Sms = new SmsSettings { Twilio = new TwilioSettings { AccountSid = "AC1", AuthToken = "tok", From = "+15550001111" } } });
        var sender = new TwilioSmsSender(ClientOf(handler), settings);

        var result = await sender.SendAsync("254722000001", "Hello", CancellationToken.None);
        result.ShouldBe("SM123");

        var call = handler.Calls.Single();
        call.Request.RequestUri!.AbsolutePath.ShouldBe("/2010-04-01/Accounts/AC1/Messages.json");
        call.Request.Headers.Authorization!.Scheme.ShouldBe("Basic");
        Encoding.UTF8.GetString(Convert.FromBase64String(call.Request.Headers.Authorization.Parameter!)).ShouldBe("AC1:tok");
        call.Body.ShouldContain("To=%2B254722000001");
        call.Body.ShouldContain("From=%2B15550001111");
    }

    [Fact]
    public async Task Twilio_throws_with_the_gateway_error_on_rejection()
    {
        var handler = new FakeHandler { Respond = (_, _) => (HttpStatusCode.BadRequest, """{"code":21211,"message":"The 'To' number is not a valid phone number."}""") };
        var settings = Options.Create(new NotificationChannelSettings { Sms = new SmsSettings { Twilio = new TwilioSettings { AccountSid = "AC1", AuthToken = "tok", From = "+15550001111" } } });
        var sender = new TwilioSmsSender(ClientOf(handler), settings);

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => sender.SendAsync("0722", "Hi", CancellationToken.None));
        ex.Message.ShouldContain("not a valid phone number");
    }

    [Fact]
    public async Task WhatsApp_sends_bearer_auth_and_text_payload_and_reads_the_message_id()
    {
        var handler = new FakeHandler { Respond = (_, _) => (HttpStatusCode.OK, """{"messaging_product":"whatsapp","contacts":[{"input":"254722000001","wa_id":"254722000001"}],"messages":[{"id":"wamid.ABC"}]}""") };
        var settings = Options.Create(new NotificationChannelSettings { Sms = new SmsSettings { WhatsApp = new WhatsAppSettings { PhoneNumberId = "111222333", AccessToken = "eaTok" } } });
        var sender = new WhatsAppCloudApiSmsSender(ClientOf(handler), settings);

        var result = await sender.SendAsync("+254722000001", "Your loan was approved.", CancellationToken.None);
        result.ShouldBe("wamid.ABC");

        var call = handler.Calls.Single();
        call.Request.RequestUri!.AbsolutePath.ShouldBe("/v20.0/111222333/messages");
        call.Request.Headers.Authorization.ShouldBe(new AuthenticationHeaderValue("Bearer", "eaTok"));
        var body = JsonDocument.Parse(call.Body).RootElement;
        body.GetProperty("to").GetString().ShouldBe("254722000001", "no leading + on the wire");
        body.GetProperty("type").GetString().ShouldBe("text");
        body.GetProperty("text").GetProperty("body").GetString().ShouldBe("Your loan was approved.");
    }

    [Fact]
    public async Task Safaricom_fetches_a_bearer_token_then_sends_and_reads_the_message_id()
    {
        var handler = new FakeHandler
        {
            Respond = (req, _) => req.RequestUri!.AbsolutePath.EndsWith("/oauth/v1/generate")
                ? (HttpStatusCode.OK, """{"access_token":"tok-1","expires_in":"3599"}""")
                : (HttpStatusCode.OK, """{"messageId":"SF-001","status":"Sent"}"""),
        };
        var settings = Options.Create(new NotificationChannelSettings { Sms = new SmsSettings { Safaricom = new SafaricomSmsSettings { BaseUrl = "https://sms.safaricom.co.ke/", ClientId = "id", ClientSecret = "secret", SenderId = "SACCO" } } });
        var sender = new SafaricomSmsSender(ClientOf(handler), settings);

        var result = await sender.SendAsync("254722000001", "Balance update", CancellationToken.None);
        result.ShouldBe("SF-001");
        handler.Calls.Single(c => c.Request.RequestUri!.AbsolutePath.EndsWith("/sms/v1/send")).Request.Headers.Authorization!.Parameter.ShouldBe("tok-1");
    }

    [Fact]
    public async Task Airtel_sms_fetches_a_bearer_token_sends_the_country_header_and_reads_the_message_id()
    {
        var handler = new FakeHandler
        {
            Respond = (req, _) => req.RequestUri!.AbsolutePath.EndsWith("/auth/oauth2/token")
                ? (HttpStatusCode.OK, """{"access_token":"tok-2","token_type":"bearer","expires_in":3600}""")
                : (HttpStatusCode.OK, """{"data":{"messageId":"AT-001"},"status":{"success":true,"message":"OK"}}"""),
        };
        var settings = Options.Create(new NotificationChannelSettings { Sms = new SmsSettings { Airtel = new AirtelSmsSettings { BaseUrl = "https://openapiuat.airtel.africa/", ClientId = "id", ClientSecret = "secret", Country = "KE", SenderId = "SACCO" } } });
        var sender = new AirtelSmsSender(ClientOf(handler), settings);

        var result = await sender.SendAsync("254722000001", "Withdrawal processed", CancellationToken.None);
        result.ShouldBe("AT-001");
        var send = handler.Calls.Single(c => c.Request.RequestUri!.AbsolutePath.EndsWith("/standard/v1/messaging"));
        send.Request.Headers.GetValues("X-Country").Single().ShouldBe("KE");
        send.Request.Headers.Authorization!.Parameter.ShouldBe("tok-2");
    }

    [Fact]
    public async Task Mock_sender_posts_to_the_local_gateway_and_reads_the_message_id()
    {
        var handler = new FakeHandler { Respond = (_, _) => (HttpStatusCode.OK, """{"messageId":"MOCK-1","status":"Delivered"}""") };
        var settings = Options.Create(new NotificationChannelSettings { Sms = new SmsSettings { Mock = new MockSmsSettings { BaseUrl = "http://localhost:5108/" } } });
        var sender = new MockSmsSender(ClientOf(handler), settings);

        var result = await sender.SendAsync("254722000001", "Test", CancellationToken.None);
        result.ShouldBe("MOCK-1");
        handler.Calls.Single().Request.RequestUri!.AbsolutePath.ShouldBe("/sms/send");
    }

    [Fact]
    public async Task Mock_sender_throws_on_a_simulated_gateway_failure()
    {
        var handler = new FakeHandler { Respond = (_, _) => (HttpStatusCode.UnprocessableEntity, """{"messageId":null,"status":"Failed","error":"Simulated gateway failure"}""") };
        var settings = Options.Create(new NotificationChannelSettings { Sms = new SmsSettings { Mock = new MockSmsSettings { BaseUrl = "http://localhost:5108/" } } });
        var sender = new MockSmsSender(ClientOf(handler), settings);

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => sender.SendAsync("254700000000", "Test", CancellationToken.None));
        ex.Message.ShouldContain("Simulated gateway failure");
    }
}
