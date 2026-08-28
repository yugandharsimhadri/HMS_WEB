using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SivayaanHMS.Automation;

/// <summary>
/// The fixed cast every workflow expects to find on screen. Held here rather than inline in the
/// scenarios so a test and its failure message can name the same patient — the same reason
/// TransTrack.Automation's DemoData exists.
/// </summary>
public static class DemoData
{
    public const string ClinicName = "Automation Test Clinic";
    public const string ClinicCode = "uattest";

    /// <summary>Usernames are "<c>local-part@clinic-code</c>" in this product — this is the local part.</summary>
    public const string AdminLocalPart = "admin";

    /// <summary>The full username the admin actually signs in with: <c>admin@uattest</c>.</summary>
    public const string AdminUsername = AdminLocalPart + "@" + ClinicCode;

    /// <summary>
    /// Chosen at registration, exactly as a real clinic owner does on the sign-up form — see
    /// <see cref="DemoDataSeeder.RegisterClinicAsync"/>. Registration does not force a password
    /// change afterwards (that is reserved for a temporary password support issues), so this is
    /// what every workflow signs in with.
    /// </summary>
    public const string AdminPassword = "UatTest@12345";

    public const string PatientOneName = "Aarav Rao";
    public const string PatientOnePhone = "9820000001";
    public const string PatientTwoName = "Meera Iyer";
    public const string PatientTwoPhone = "9820000002";

    /// <summary>Seeded automatically for every new clinic by TenantProvisioner — see StarterCatalogue in TransTrack, or here, DemoData.cs.</summary>
    public const string StarterMedicineName = "Paracetamol 500mg";

    /// <summary>Also seeded automatically, by TenantProvisioner.SeedStarterDataAsync — one starter doctor so a visit can be booked without registering staff first.</summary>
    public const string StarterDoctorName = "Dr. A. Kumar";
}

/// <summary>
/// Builds the dataset a run is tested against, through the product's own HTTP API — the same
/// calls the client makes, in the same order a clinic actually signs up and gets to work.
///
/// Seeding through the API rather than by writing rows directly is deliberate, for the reasons
/// TransTrack.Automation's DemoDataSeeder gives for doing the same thing: it exercises (and so
/// cannot silently break) the registration path itself, and it never has to know the schema — a
/// migration that adds a column changes nothing here.
///
/// Simpler than that seeder in one genuine way: <c>POST /api/tenants/register</c> needs no
/// bearer token and forces no password change afterwards, so there is no EnterpriseAdmin
/// recovery-token step and no "sign in with a temporary password, then change it" round trip to
/// walk through first. A clinic here is usable the moment registration returns.
/// </summary>
public sealed class DemoDataSeeder(string apiBaseUrl, Action<string>? log = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _api = apiBaseUrl.TrimEnd('/');

    /// <summary>
    /// Registers the clinic, signs in as its admin, turns on the Pediatrics and Diagnostics
    /// modules (both off by default — see <c>GeneralSettings</c> — so a workflow that needs
    /// either has to ask for it explicitly, exactly as a real clinic would from
    /// Settings &gt; Features), and adds two patients with one booked visit between them so the
    /// OPD queue, the register and a search all have something real to find.
    ///
    /// Idempotent by inspection: if the admin can already sign in — the case when
    /// <see cref="AutomationOptions.ManageServers"/> is false and this is pointed at a database a
    /// previous run already seeded — the seed stops after that check.
    /// </summary>
    public async Task<string> SeedAsync(CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        var existingToken = await TrySignInAsync(http, cancellationToken);
        if (existingToken is not null)
        {
            log?.Invoke("Demo data already present — skipping the seed.");
            return existingToken;
        }

        log?.Invoke($"Registering '{DemoData.ClinicName}' through {_api}");
        await RegisterClinicAsync(http, cancellationToken);

        var token = await TrySignInAsync(http, cancellationToken)
            ?? throw new InvalidOperationException("Registered the clinic but could not then sign in as its admin.");

        await EnableModulesAsync(http, token, cancellationToken);

        var doctorId = await FirstDoctorIdAsync(http, token, cancellationToken);

        var patientOneId = await CreatePatientAsync(http, token, DemoData.PatientOneName, DemoData.PatientOnePhone, cancellationToken);
        await CreatePatientAsync(http, token, DemoData.PatientTwoName, DemoData.PatientTwoPhone, cancellationToken);

        await BookVisitAsync(http, token, patientOneId, doctorId, cancellationToken);

        log?.Invoke("Seed complete.");
        return token;
    }

    /// <summary>The signup form, filled in exactly as a clinic owner fills it in.</summary>
    public async Task RegisterClinicAsync(HttpClient http, CancellationToken cancellationToken)
    {
        var response = await http.PostAsJsonAsync($"{_api}/api/tenants/register", new
        {
            clinicName = DemoData.ClinicName,
            clinicCode = DemoData.ClinicCode,
            username = DemoData.AdminLocalPart,
            password = DemoData.AdminPassword,
        }, Json, cancellationToken);

        // 409 Conflict means a previous run's database is still around and already has this
        // clinic code — treated as success, since the sign-in check right after this call is
        // what actually decides whether the seed can proceed.
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Clinic registration failed ({(int)response.StatusCode}): {body}");
        }
    }

    private async Task<string?> TrySignInAsync(HttpClient http, CancellationToken cancellationToken)
    {
        var response = await http.PostAsJsonAsync($"{_api}/api/auth/login",
            new { username = DemoData.AdminUsername, password = DemoData.AdminPassword }, Json, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(Json, cancellationToken);
        return body?.Token;
    }

    /// <summary>
    /// Pediatrics and Diagnostics start switched off for every new clinic, so a workflow that
    /// needs either has to turn it on the same way Settings &gt; Features does: read the whole
    /// settings object, flip the two flags, save the whole object back. A partial update is not
    /// offered by the API — see <c>SettingsController.SaveGeneral</c> — because the desktop's
    /// screen never offered one either.
    /// </summary>
    private async Task EnableModulesAsync(HttpClient http, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_api}/api/settings/general");
        request.Headers.Authorization = new("Bearer", token);
        var getResponse = await http.SendAsync(request, cancellationToken);
        getResponse.EnsureSuccessStatusCode();

        // JsonNode rather than laundering the response through a typed record or a hand-built
        // dictionary: this only ever needs to flip two booleans and post the rest back exactly as
        // received, and a node mutates a property in place without having to know or reconstruct
        // the type of every other field on GeneralSettings — including the enum-backed ones
        // (QueueLayout, Theme), which would need their own coercion logic to round-trip safely
        // through anything less direct than this.
        var settings = JsonNode.Parse(await getResponse.Content.ReadAsStringAsync(cancellationToken))
            ?? throw new InvalidOperationException("GET /api/settings/general returned no body.");

        settings["diagnosticsEnabled"] = true;
        settings["pediatricsEnabled"] = true;

        await PostRawAsync(http, token, "/api/settings/general", settings, cancellationToken);
    }

    private async Task<Guid> FirstDoctorIdAsync(HttpClient http, string token, CancellationToken cancellationToken)
    {
        var doctors = await GetAsync<List<Ref>>(http, token, "/api/doctors", cancellationToken);
        if (doctors is not { Count: > 0 })
            throw new InvalidOperationException("Registration did not create the starter doctor the workflows book against.");
        return doctors[0].Id;
    }

    private Task<Guid> CreatePatientAsync(HttpClient http, string token, string name, string phone, CancellationToken cancellationToken)
        => PostForIdAsync(http, token, "/api/patients", new { name, phone, age = 8, gender = "Male" }, cancellationToken);

    private Task BookVisitAsync(HttpClient http, string token, Guid patientId, Guid doctorId, CancellationToken cancellationToken)
        => PostAsync(http, token, "/api/visits", new
        {
            patientId,
            doctorId,
            scheduledOn = DateTime.Today.AddHours(10),
            complaint = "Fever and cough",
            fee = 300,
        }, cancellationToken);

    private async Task<Guid> PostForIdAsync(HttpClient http, string token, string path, object body, CancellationToken cancellationToken)
    {
        var text = await PostRawAsync(http, token, path, body, cancellationToken);
        using var document = JsonDocument.Parse(text);

        if (!document.RootElement.TryGetProperty("id", out var idProperty))
            throw new InvalidOperationException($"POST {path} did not return an object with an 'id'. Body: {text}");

        return idProperty.GetGuid();
    }

    private async Task PostAsync(HttpClient http, string token, string path, object body, CancellationToken cancellationToken)
        => await PostRawAsync(http, token, path, body, cancellationToken);

    private async Task<string> PostRawAsync(HttpClient http, string token, string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _api + path) { Content = JsonContent.Create(body, options: Json) };
        request.Headers.Authorization = new("Bearer", token);

        var response = await http.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"POST {path} failed ({(int)response.StatusCode}). Body: {text}");

        return text;
    }

    private async Task<T?> GetAsync<T>(HttpClient http, string token, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _api + path);
        request.Headers.Authorization = new("Bearer", token);

        var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
    }

    private sealed record LoginResponse(string Token, string Username, string Role, bool MustChangePassword, string ClinicName);
    private sealed record Ref(Guid Id);
}
