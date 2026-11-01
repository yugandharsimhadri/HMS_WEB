using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SivayaanHMS.Data.Messaging;

public sealed class SivaComOptions
{
    public const string SectionName = "SivaCom";

    /// <summary>
    /// The switch. True: reset codes and welcome messages go through SivaCom.
    /// False (default): the normal flow, entirely inside this application.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>e.g. https://comm.sivayaan.example — server to server, never the browser.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>This application's code in SivaCom, e.g. "hms-web".</summary>
    public string ClientId { get; set; } = "";

    public string KeyId { get; set; } = "";

    /// <summary>The signing secret SivaCom showed once at registration.
    /// appsettings.Production.json or SivaCom__Secret — never committed.</summary>
    public string Secret { get; set; } = "";

    public int TimeoutSeconds { get; set; } = 10;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(Secret);
}

/// <summary>One SivaCom answer: the HTTP status, its normalised status word
/// ("VERIFIED", "INVALID_OTP", … or "TEMPORARY_ERROR"), and the body.</summary>
public sealed record SivaComResponse(int HttpStatus, string Status, JsonElement Body)
{
    public string? Str(string name) => Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    public int? Int(string name) => Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
    public bool? Bool(string name) => Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
}

/// <summary>
/// Signed calls to SivaCom. The reference implementation of the signature
/// in SivaCom's docs/API.md:
///
/// <code>
/// canonical = METHOD \n PATH_AND_QUERY \n TIMESTAMP \n NONCE \n hex(SHA256(body))
/// X-SivaCom-Signature = base64(HMAC-SHA256(secret, canonical))
/// </code>
///
/// Every failure that is not a definite answer from SivaCom — a timeout, a
/// refused connection, a 5xx, a body that is not the expected JSON — comes
/// back as TEMPORARY_ERROR. Nothing above this class can mistake an outage
/// for a verified code.
/// </summary>
public sealed class SivaComClient(HttpClient http, IOptions<SivaComOptions> options, ILogger<SivaComClient> logger)
{
    private readonly SivaComOptions _options = options.Value;
    private static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement;

    public async Task<SivaComResponse> SendAsync(HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken ct)
    {
        var json = body is null ? [] : JsonSerializer.SerializeToUtf8Bytes(body, JsonSerializerOptions.Web);
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var nonce = Guid.NewGuid().ToString("N");

        using var request = new HttpRequestMessage(method, new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/"), path.TrimStart('/')));

        // Signed over the path actually sent, exactly as SivaCom will see it.
        var canonical = string.Join('\n',
            method.Method, request.RequestUri!.PathAndQuery, timestamp, nonce, Convert.ToHexStringLower(SHA256.HashData(json)));
        var signature = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.Secret), Encoding.UTF8.GetBytes(canonical)));

        if (body is not null)
        {
            request.Content = new ByteArrayContent(json);
            request.Content.Headers.ContentType = new("application/json");
        }
        request.Headers.Add("X-SivaCom-Client", _options.ClientId);
        request.Headers.Add("X-SivaCom-Key-Id", _options.KeyId);
        request.Headers.Add("X-SivaCom-Timestamp", timestamp);
        request.Headers.Add("X-SivaCom-Nonce", nonce);
        request.Headers.Add("X-SivaCom-Signature", signature);
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        // Every call is one line in the messages log: where it went, what came
        // back, how long it took, and SivaCom's correlation id — the same id
        // is in SivaCom's own messages log, so the two read side by side.
        var url = request.RequestUri!.GetLeftPart(UriPartial.Path);
        var started = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var response = await http.SendAsync(request, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            var status = (int)response.StatusCode;
            var ms = started.ElapsedMilliseconds;

            if (status == 401)
            {
                // Our key is wrong, revoked, or our clock is off. Loud, because
                // every reset fails until somebody fixes configuration.
                logger.LogError(
                    "SivaCom {Method:l} {Url:l} -> 401 in {Ms} ms: SivaCom refused this application's signature (client {ClientId:l}, key {KeyId:l}). " +
                    "Compare SivaCom:ClientId/KeyId/Secret here with the hms-web entry in SivaCom's Applications, check both servers' clocks, " +
                    "and that both are builds from after the SivaCom rename. SivaCom's messages log says which.",
                    method.Method, url, ms, _options.ClientId, _options.KeyId);
                return new(status, "TEMPORARY_ERROR", Empty);
            }

            JsonElement parsed;
            try { parsed = JsonDocument.Parse(text).RootElement.Clone(); }
            catch (JsonException)
            {
                logger.LogError("SivaCom {Method:l} {Url:l} -> {Status} in {Ms} ms with a body that is not JSON: is SivaCom:BaseUrl really SivaCom?",
                    method.Method, url, status, ms);
                return new(status, "TEMPORARY_ERROR", Empty);
            }

            var word = Text(parsed, "status");
            var message = Text(parsed, "message");
            var correlation = Text(parsed, "correlationId") ?? "-";

            if (status >= 500 || word is null)
            {
                logger.LogWarning("SivaCom {Method:l} {Url:l} -> {Status} {Word:l} in {Ms} ms{Message:l} [correlation {Correlation:l}]",
                    method.Method, url, status, word ?? "(none)", ms, message is null ? "" : " - " + message, correlation);
                return new(status, "TEMPORARY_ERROR", parsed);
            }

            logger.Log(status < 300 ? LogLevel.Information : LogLevel.Warning,
                "SivaCom {Method:l} {Url:l} -> {Status} {Word:l} in {Ms} ms{Ids:l}{Message:l} [correlation {Correlation:l}]",
                method.Method, url, status, word, ms,
                string.Concat(Text(parsed, "challengeId") is { } c ? " challenge " + c : "", Text(parsed, "messageId") is { } m ? " message " + m : ""),
                message is null || status < 300 ? "" : " - " + message, correlation);

            return new(status, word, parsed);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            // The reason ("No connection could be made…", a DNS failure) says
            // whether SivaCom is down, the URL is wrong or a firewall is in
            // the way. It names hosts, never the user.
            logger.LogError("Could not reach SivaCom: {Method:l} {Url:l} failed after {Ms} ms: {Error:l}. Is the SivaComApi service running, and is SivaCom:BaseUrl right?",
                method.Method, url, started.ElapsedMilliseconds,
                ex is HttpRequestException ? ex.Message : $"no answer within SivaCom:TimeoutSeconds ({_options.TimeoutSeconds} s)");
            return new(0, "TEMPORARY_ERROR", Empty);
        }
    }

    private static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public Task<SivaComResponse> PostAsync(string path, object body, CancellationToken ct, string? idempotencyKey = null) =>
        SendAsync(HttpMethod.Post, path, body, idempotencyKey, ct);
}
