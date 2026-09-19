using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Seed.Data;
using Sacco.Seed.Seeders;
using Shouldly;

namespace Sacco.IntegrationTests.Identity;

/// <summary>The native mobile app's phone+PIN sign-in (ADR 0008, 2026-09-16 update): a new device needs
/// SMS OTP once, then the <c>mobile</c> client's ROPC grant works with just phone + PIN.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class MemberNativeLoginTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString, realAuth: true);
    public void Dispose() => _factory.Dispose();

    private const string Phone = "254700100001"; // M00001
    private const string Pin = MembersSeeder.DemoPin;

    private async Task<HttpResponseMessage> RequestOtpAsync(HttpClient client, string deviceId, string phone = Phone, string pin = Pin)
    {
        client.DefaultRequestHeaders.Remove("X-Tenant");
        client.DefaultRequestHeaders.Add("X-Tenant", DemoTenant.Slug);
        return await client.PostAsJsonAsync("/api/self/auth/otp/request", new { phone, pin, deviceId });
    }

    private static async Task<HttpResponseMessage> TokenAsync(HttpClient client, string deviceId, string? otp, string phone = Phone, string pin = Pin)
    {
        client.DefaultRequestHeaders.Remove("X-Tenant");
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["client_id"] = "mobile",
            ["username"] = phone, ["password"] = pin, ["tenant"] = DemoTenant.Slug,
            ["scope"] = "openid profile tenant sacco-api offline_access", ["device_id"] = deviceId,
        };
        if (otp is not null) form["otp"] = otp;
        return await client.PostAsync("/connect/token", new FormUrlEncodedContent(form));
    }

    [Fact]
    public async Task New_device_needs_otp_then_signs_in_and_becomes_trusted()
    {
        var client = _factory.CreateClient();
        var deviceId = $"device-{Guid.NewGuid():N}";

        var otpRequest = await RequestOtpAsync(client, deviceId);
        otpRequest.StatusCode.ShouldBe(HttpStatusCode.OK, await otpRequest.Content.ReadAsStringAsync());
        var otpBody = await otpRequest.Content.ReadFromJsonAsync<RequestOtpResponseDto>(HttpExtensions.JsonOptions);
        otpBody!.OtpRequired.ShouldBeTrue("a device that has never verified must go through OTP");
        otpBody.ExpiresInSeconds.ShouldBeGreaterThan(0);
        _factory.Sms.Sent.ShouldContain(m => m.PhoneNumber == Phone);

        // Without an otp field, /connect/token refuses the grant — PIN alone is not enough for a new device.
        var withoutOtp = await TokenAsync(client, deviceId, otp: null);
        withoutOtp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var wrongOtp = await TokenAsync(client, deviceId, otp: "000000");
        wrongOtp.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "wrong code is rejected, never silently accepted");

        var code = _factory.Sms.LastCodeFor(Phone);
        var signedIn = await TokenAsync(client, deviceId, otp: code);
        signedIn.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tokens = await signedIn.Content.ReadFromJsonAsync<TokenResponseDto>(HttpExtensions.JsonOptions);
        tokens!.AccessToken.ShouldNotBeNullOrWhiteSpace();
        tokens.RefreshToken.ShouldNotBeNullOrWhiteSpace("offline_access is requested so the app can renew silently, exactly like the browser flow already did");

        // The access token actually works against a self-service endpoint.
        var me = _factory.ClientWithToken(tokens.AccessToken);
        me.DefaultRequestHeaders.Add("X-Tenant", DemoTenant.Slug);
        (await me.GetAsync("/api/self/profile")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Same device, second sign-in: no OTP needed anymore.
        var secondOtpCheck = await RequestOtpAsync(client, deviceId);
        var secondBody = await secondOtpCheck.Content.ReadFromJsonAsync<RequestOtpResponseDto>(HttpExtensions.JsonOptions);
        secondBody!.OtpRequired.ShouldBeFalse("this device already completed OTP once");

        var secondSignIn = await TokenAsync(client, deviceId, otp: null);
        secondSignIn.StatusCode.ShouldBe(HttpStatusCode.OK, "a trusted device signs in with just phone + PIN");
    }

    [Fact]
    public async Task Wrong_pin_never_sends_an_otp()
    {
        var client = _factory.CreateClient();
        var deviceId = $"device-{Guid.NewGuid():N}";

        var response = await RequestOtpAsync(client, deviceId, pin: "9999");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        _factory.Sms.Sent.ShouldNotContain(m => m.PhoneNumber == Phone, "an SMS must never go out before the PIN itself is verified");
    }

    [Fact]
    public async Task A_different_device_for_the_same_member_still_needs_its_own_otp()
    {
        var client = _factory.CreateClient();
        var deviceA = $"device-{Guid.NewGuid():N}";
        var deviceB = $"device-{Guid.NewGuid():N}";

        await RequestOtpAsync(client, deviceA);
        var codeA = _factory.Sms.LastCodeFor(Phone);
        (await TokenAsync(client, deviceA, codeA)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var otpForB = await RequestOtpAsync(client, deviceB);
        var bodyForB = await otpForB.Content.ReadFromJsonAsync<RequestOtpResponseDto>(HttpExtensions.JsonOptions);
        bodyForB!.OtpRequired.ShouldBeTrue("trusting device A must not trust device B");

        // Device A's already-consumed code must not work for device B.
        (await TokenAsync(client, deviceB, codeA)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private sealed record RequestOtpResponseDto(bool OtpRequired, int ExpiresInSeconds);

    private sealed record TokenResponseDto(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken);
}
