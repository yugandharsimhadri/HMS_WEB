using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SivayaanHMS.Data.Messaging;

namespace SivayaanHMS.Tests;

/// <summary>Answers each request from a script, and keeps what it was sent.</summary>
public sealed class StubHandler : HttpMessageHandler
{
    public Queue<Func<HttpRequestMessage, HttpResponseMessage>> Script { get; } = new();
    public List<(HttpRequestMessage Request, string Body)> Seen { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Seen.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
        return Script.Dequeue()(request);
    }

    public static HttpResponseMessage Json(int status, object body) => new((HttpStatusCode)status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };
}

/// <summary>
/// The HTTP edge to SivaComm. What matters most here is the failure side:
/// every way the call can go wrong must come back as TEMPORARY_ERROR or a
/// definite refusal — never as VERIFIED or CONSUMED.
/// </summary>
public sealed class SivaCommGatewayTests
{
    private const string Secret = "test-signing-secret";
    private readonly StubHandler _http = new();

    private SivaCommOtpGateway Gateway()
    {
        var options = Options.Create(new SivaCommOptions
        {
            BaseUrl = "https://comm.test", ClientId = "hms-web", KeyId = "k1", Secret = Secret, TimeoutSeconds = 5,
        });
        var client = new SivaCommClient(new HttpClient(_http), options, NullLogger<SivaCommClient>.Instance);
        return new SivaCommOtpGateway(client, options, NullLogger<SivaCommOtpGateway>.Instance);
    }

    private static OtpSendRequest Send() => new("hms-reset-1", "u-user-1", "9876543210", Guid.NewGuid(), Guid.NewGuid(), "Clinic");

    [Fact]
    public async Task Requests_are_signed_the_way_sivacomm_checks_them()
    {
        _http.Script.Enqueue(_ => StubHandler.Json(202, new { success = true, status = "QUEUED", challengeId = "c-1" }));

        var r = await Gateway().SendAsync(Send(), default);
        Assert.Equal(OtpStatus.Queued, r.Status);
        Assert.Equal("c-1", r.ChallengeId);

        var (req, body) = Assert.Single(_http.Seen);
        string H(string name) => req.Headers.GetValues(name).Single();
        var canonical = string.Join('\n', "POST", "/api/v1/otp/challenges", H("X-SivaComm-Timestamp"), H("X-SivaComm-Nonce"),
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body))));
        var expected = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(canonical)));

        Assert.Equal(expected, H("X-SivaComm-Signature"));
        Assert.Equal("hms-web", H("X-SivaComm-Client"));
        // No password field of any kind ever travels to SivaComm.
        var fields = JsonDocument.Parse(body).RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["resetReference", "userReference", "phone", "purpose"], fields);
    }

    [Fact]
    public async Task Verified_grant_is_held_encrypted_and_consumed_with_the_reset_reference_as_idempotency_key()
    {
        _http.Script.Enqueue(_ => StubHandler.Json(200, new { success = true, status = "VERIFIED", resetReference = "hms-reset-1", verificationGrant = "GRANT-xyz" }));
        _http.Script.Enqueue(_ => StubHandler.Json(200, new { success = true, status = "CONSUMED", resetReference = "hms-reset-1", userReference = "u-user-1" }));
        var gateway = Gateway();

        var v = await gateway.VerifyAsync("c-1", "hms-reset-1", Guid.Empty, Guid.Empty, "123456", default);
        Assert.Equal(OtpStatus.Verified, v.Status);
        Assert.DoesNotContain("GRANT-xyz", v.GrantHandle);

        Assert.Equal(OtpStatus.Consumed, await gateway.ConsumeAsync(v.GrantHandle!, "hms-reset-1", "u-user-1", default));

        var (consume, body) = _http.Seen[1];
        Assert.Equal("hms-reset-1", consume.Headers.GetValues("Idempotency-Key").Single());
        Assert.Contains("GRANT-xyz", body);
    }

    [Fact]
    public async Task Invalid_otp_carries_the_remaining_attempts()
    {
        _http.Script.Enqueue(_ => StubHandler.Json(422, new { success = false, status = "INVALID_OTP", remainingAttempts = 2 }));

        var v = await Gateway().VerifyAsync("c-1", "hms-reset-1", Guid.Empty, Guid.Empty, "000000", default);

        Assert.Equal(OtpStatus.InvalidOtp, v.Status);
        Assert.Equal(2, v.RemainingAttempts);
        Assert.Null(v.GrantHandle);
    }

    [Fact]
    public async Task Verified_for_a_different_reset_is_not_trusted()
    {
        _http.Script.Enqueue(_ => StubHandler.Json(200, new { success = true, status = "VERIFIED", resetReference = "someone-else", verificationGrant = "G" }));

        var v = await Gateway().VerifyAsync("c-1", "hms-reset-1", Guid.Empty, Guid.Empty, "123456", default);

        Assert.Equal(OtpStatus.TemporaryError, v.Status);
        Assert.Null(v.GrantHandle);
    }

    [Fact]
    public async Task Consumed_for_a_different_user_is_not_trusted()
    {
        _http.Script.Enqueue(_ => StubHandler.Json(200, new { status = "VERIFIED", resetReference = "hms-reset-1", verificationGrant = "G" }));
        _http.Script.Enqueue(_ => StubHandler.Json(200, new { status = "CONSUMED", resetReference = "hms-reset-1", userReference = "u-other" }));
        var gateway = Gateway();
        var v = await gateway.VerifyAsync("c-1", "hms-reset-1", Guid.Empty, Guid.Empty, "123456", default);

        Assert.NotEqual(OtpStatus.Consumed, await gateway.ConsumeAsync(v.GrantHandle!, "hms-reset-1", "u-user-1", default));
    }

    public static TheoryData<Func<HttpRequestMessage, HttpResponseMessage>> Outages => new()
    {
        _ => StubHandler.Json(503, new { success = false, status = "TEMPORARY_ERROR" }),
        _ => StubHandler.Json(500, new { status = "VERIFIED", verificationGrant = "G", resetReference = "hms-reset-1" }),
        _ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>proxy error</html>") },
        _ => StubHandler.Json(401, new { success = false, status = "UNAUTHORIZED" }),
        _ => StubHandler.Json(200, new { success = true }),
        _ => throw new HttpRequestException("connection refused"),
        _ => throw new TaskCanceledException("timeout"),
    };

    [Theory]
    [MemberData(nameof(Outages))]
    public async Task Every_kind_of_outage_is_a_temporary_error_never_a_verification(Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        _http.Script.Enqueue(answer);

        var v = await Gateway().VerifyAsync("c-1", "hms-reset-1", Guid.Empty, Guid.Empty, "123456", default);

        Assert.Equal(OtpStatus.TemporaryError, v.Status);
        Assert.Null(v.GrantHandle);
    }

    [Fact]
    public async Task A_tampered_grant_handle_is_refused_without_calling_sivacomm()
    {
        Assert.Equal(OtpStatus.Expired, await Gateway().ConsumeAsync("bm90LWEtaGFuZGxlLWF0LWFsbC1ub3QtYXQtYWxs", "hms-reset-1", "u-user-1", default));
        Assert.Empty(_http.Seen);
    }
}
