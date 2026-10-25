using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SivayaanHMS.Data.Messaging;

public sealed class SivaCommOptions
{
    public const string SectionName = "SivaComm";

    /// <summary>e.g. https://comm.sivayaan.example — server to server, never the browser.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>This application's code in SivaComm, e.g. "hms-web".</summary>
    public string ClientId { get; set; } = "";

    public string KeyId { get; set; } = "";

    /// <summary>The signing secret SivaComm showed once at registration.
    /// appsettings.Production.json or SivaComm__Secret — never committed.</summary>
    public string Secret { get; set; } = "";

    public int TimeoutSeconds { get; set; } = 10;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(Secret);
}

/// <summary>One SivaComm answer: the HTTP status, its normalised status word
/// ("VERIFIED", "INVALID_OTP", … or "TEMPORARY_ERROR"), and the body.</summary>
public sealed record SivaCommResponse(int HttpStatus, string Status, JsonElement Body)
{
    public string? Str(string name) => Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    public int? Int(string name) => Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
    public bool? Bool(string name) => Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
}

/// <summary>
/// Signed calls to SivaComm. The reference implementation of the signature
/// in SivaComm's docs/API.md:
///
/// <code>
/// canonical = METHOD \n PATH_AND_QUERY \n TIMESTAMP \n NONCE \n hex(SHA256(body))
/// X-SivaComm-Signature = base64(HMAC-SHA256(secret, canonical))
/// </code>
///
/// Every failure that is not a definite answer from SivaComm — a timeout, a
/// refused connection, a 5xx, a body that is not the expected JSON — comes
/// back as TEMPORARY_ERROR. Nothing above this class can mistake an outage
/// for a verified code.
/// </summary>
public sealed class SivaCommClient(HttpClient http, IOptions<SivaCommOptions> options, ILogger<SivaCommClient> logger)
{
    private readonly SivaCommOptions _options = options.Value;
    private static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement;

    public async Task<SivaCommResponse> SendAsync(HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken ct)
    {
        var json = body is null ? [] : JsonSerializer.SerializeToUtf8Bytes(body, JsonSerializerOptions.Web);
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var nonce = Guid.NewGuid().ToString("N");

        using var request = new HttpRequestMessage(method, new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/"), path.TrimStart('/')));

        // Signed over the path actually sent, exactly as SivaComm will see it.
        var canonical = string.Join('\n',
            method.Method, request.RequestUri!.PathAndQuery, timestamp, nonce, Convert.ToHexStringLower(SHA256.HashData(json)));
        var signature = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.Secret), Encoding.UTF8.GetBytes(canonical)));

        if (body is not null)
        {
            request.Content = new ByteArrayContent(json);
            request.Content.Headers.ContentType = new("application/json");
        }
        request.Headers.Add("X-SivaComm-Client", _options.ClientId);
        request.Headers.Add("X-SivaComm-Key-Id", _options.KeyId);
        request.Headers.Add("X-SivaComm-Timestamp", timestamp);
        request.Headers.Add("X-SivaComm-Nonce", nonce);
        request.Headers.Add("X-SivaComm-Signature", signature);
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            using var response = await http.SendAsync(request, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            var status = (int)response.StatusCode;

            if (status == 401)
            {
                // Our key is wrong, revoked, or our clock is off. Loud, because
                // every reset fails until somebody fixes configuration.
                logger.LogError("SivaComm refused this application's signature (401). Check SivaComm:ClientId/KeyId/Secret and the server clock.");
                return new(status, "TEMPORARY_ERROR", Empty);
            }

            JsonElement parsed;
            try { parsed = JsonDocument.Parse(text).RootElement.Clone(); }
            catch (JsonException)
            {
                logger.LogError("SivaComm returned {Status} with a body that is not JSON.", status);
                return new(status, "TEMPORARY_ERROR", Empty);
            }

            var word = parsed.ValueKind == JsonValueKind.Object && parsed.TryGetProperty("status", out var s) ? s.GetString() : null;
            if (status >= 500 || word is null)
            {
                logger.LogWarning("SivaComm answered {Status} {Word} for {Path}.", status, word ?? "(none)", path);
                return new(status, "TEMPORARY_ERROR", parsed);
            }

            return new(status, word, parsed);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Could not reach SivaComm for {Path}: {Error}.", path, ex.GetType().Name);
            return new(0, "TEMPORARY_ERROR", Empty);
        }
    }

    public Task<SivaCommResponse> PostAsync(string path, object body, CancellationToken ct, string? idempotencyKey = null) =>
        SendAsync(HttpMethod.Post, path, body, idempotencyKey, ct);
}
