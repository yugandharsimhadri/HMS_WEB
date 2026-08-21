using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;

namespace SivayaanHMS.Data;

/// <summary>
/// The clinic's own identity — printed on the prescription and the fee
/// receipt, never on the pharmacy bill.
/// </summary>
public class ClinicProfile
{
    public string Name { get; set; } = "Sivayaan HMS";
    public string AddressLine { get; set; } = "";
    public string AddressLine2 { get; set; } = "";
    public string Phone { get; set; } = "";

    /// <summary>
    /// Off by default. A consultation is usually not a taxable supply, and
    /// printing a GSTIN on a prescription that is not one would be a false
    /// claim — same reasoning as the pharmacy's own switch, applied separately.
    /// </summary>
    public bool GstRegistered { get; set; }
    public string Gstin { get; set; } = "";

    /// <summary>
    /// Printed at the foot of the prescription and the fee receipt. Empty
    /// means "use the shared Reports footer" — see <see cref="DocumentTheme.Footer"/>
    /// — so a clinic that never visits this field keeps printing exactly what
    /// it always did until it types its own.
    /// </summary>
    public string FooterText { get; set; } = "";

    // When the doctor actually sits. Indian clinics run two sittings with the
    // afternoon off, and the desk thinks in those terms — "who is left this
    // evening" is a real question and "who is left today" is not.
    public TimeSpan MorningFrom { get; set; } = new(10, 0, 0);
    public TimeSpan MorningTo { get; set; } = new(13, 0, 0);
    public TimeSpan EveningFrom { get; set; } = new(16, 0, 0);
    public TimeSpan EveningTo { get; set; } = new(20, 0, 0);

    /// <summary>
    /// The hours a session covers, or null for the whole day. The end is
    /// exclusive, so a morning ending at 13:00 does not also claim the one
    /// o'clock patient.
    /// </summary>
    public (TimeSpan From, TimeSpan To)? Window(ClinicSession session) => session switch
    {
        ClinicSession.Morning => (MorningFrom, MorningTo),
        ClinicSession.Evening => (EveningFrom, EveningTo),
        _ => null
    };

    /// <summary>Whether a visit at this time belongs to the chosen session.</summary>
    public bool IsIn(ClinicSession session, DateTime when)
    {
        if (Window(session) is not { } window) return true;

        var time = when.TimeOfDay;
        return time >= window.From && time < window.To;
    }

    /// <summary>"10:00 to 13:00", for the line under the OPD heading.</summary>
    public string Describe(ClinicSession session) =>
        Window(session) is { } window
            ? $"{window.From:hh\\:mm} to {window.To:hh\\:mm}"
            : "the whole day";
}

/// <summary>
/// The pharmacy's own identity — printed on the medicine bill. Can differ
/// from the clinic's: a pharmacy sometimes trades under its own name even
/// when it sits inside the same building.
/// </summary>
public class PharmacyProfile
{
    public string Name { get; set; } = "Sivayaan HMS";
    public string AddressLine { get; set; } = "";
    public string AddressLine2 { get; set; } = "";
    public string Phone { get; set; } = "";

    /// <summary>
    /// Whether the pharmacy is registered for GST. Off means no tax is charged
    /// and bills print as a plain invoice — issuing a "tax invoice" without
    /// being registered is not something to leave switched on by default.
    /// </summary>
    public bool GstRegistered { get; set; }
    public string Gstin { get; set; } = "";
    public string DrugLicenceNo { get; set; } = "";
    public string PharmacistName { get; set; } = "";

    /// <summary>
    /// Printed at the foot of the medicine bill. Empty means "use the shared
    /// Reports footer" — see <see cref="DocumentTheme.Footer"/> — so a
    /// pharmacy that never visits this field keeps printing exactly what it
    /// always did until it types its own.
    /// </summary>
    public string FooterText { get; set; } = "";
}

/// <summary>
/// How every printed document is branded. One theme today — a single logo and
/// a single footer message used everywhere — with room to grow into
/// per-document themes later without another migration: this is already its
/// own settings group rather than folded into Clinic or Pharmacy.
/// </summary>
public class DocumentTheme
{
    public string Footer { get; set; } = "Get well soon. Medicines once sold are not returnable.";

    /// <summary>
    /// Base64, not a file path and not a database column of its own. It rides
    /// inside the same Settings row as everything else, so it travels with
    /// every backup automatically.
    /// </summary>
    public string? LogoBase64 { get; set; }
    public string? LogoContentType { get; set; }

    public bool HasLogo => !string.IsNullOrWhiteSpace(LogoBase64);

    /// <summary>
    /// The typeface every printed document uses. Null/blank means the
    /// compiled-in default rather than a literal font name baked in here, so
    /// a clinic that never visits this field keeps printing exactly what it
    /// always did.
    /// </summary>
    public string? PrintFontFamily { get; set; }

    /// <summary>
    /// Added to every font size on every printed document. Zero by default,
    /// so nothing changes size until a clinic asks it to. Kept as one number
    /// applied everywhere rather than a size per document: the documents'
    /// own internal proportions are a design decision, not something to
    /// expose as separate knobs.
    /// </summary>
    public double PrintFontSizeDelta { get; set; }

    /// <summary>
    /// The typeface the clinic's (or the pharmacy's) own name prints in, at
    /// the top of every document — separate from <see cref="PrintFontFamily"/>
    /// so the name can stand out in its own face without changing the body
    /// text everywhere else. Null/blank inherits <see cref="PrintFontFamily"/>.
    /// </summary>
    public string? TitleFontFamily { get; set; }

    /// <summary>
    /// Added to the clinic/pharmacy name's own size, on top of whatever
    /// <see cref="PrintFontSizeDelta"/> already adds to it along with
    /// everything else on the page — the two stack. Zero by default.
    /// </summary>
    public double TitleFontSizeDelta { get; set; }
}

/// <summary>How the application itself behaves, as opposed to who it is speaking for.</summary>
public class GeneralSettings
{
    /// <summary>
    /// How the OPD queue is drawn. Tiles suit a short list read at a glance; rows
    /// fit more people on screen. Which is better depends on how busy the clinic
    /// is, so it is the user's choice rather than ours.
    /// </summary>
    public QueueLayout QueueLayout { get; set; } = QueueLayout.Tiles;

    /// <summary>
    /// Light or dark. A counter under fluorescent light and a desk in a dim back
    /// room want different things, and it is the same person switching between
    /// them, so it is a setting rather than something we decide.
    /// </summary>
    public AppThemeKind Theme { get; set; } = AppThemeKind.Light;

    /// <summary>
    /// Off by default. Most clinics have no in-house lab, so the Diagnostics
    /// nav item, and everything it opens, stays out of the way until this is
    /// switched on under Settings → Features — the same "optional module"
    /// shape later features can reuse.
    /// </summary>
    public bool DiagnosticsEnabled { get; set; }

    /// <summary>
    /// On by default — unlike every other module toggle on this page. Every
    /// clinic already running this application is already using the OPD
    /// queue; a change that silently switched it off would break their
    /// clinic the moment they opened it. A dentist- or pediatrician-only
    /// clinic switches this off once, deliberately, during setup.
    /// </summary>
    public bool OpdEnabled { get; set; } = true;

    /// <summary>On by default, for the same reason as <see cref="OpdEnabled"/> —
    /// every existing install is already using the pharmacy counter.</summary>
    public bool PharmacyEnabled { get; set; } = true;

    /// <summary>
    /// Off by default. Advance booking, cancellation/reschedule, the daily
    /// check-in screen, and on-screen reminders — a clinic that only ever
    /// takes walk-ins has no use for any of it until this is switched on.
    /// </summary>
    public bool AppointmentsEnabled { get; set; }

    /// <summary>Off by default. Vaccine master, vaccination and growth
    /// history, and pediatric procedure billing.</summary>
    public bool PediatricsEnabled { get; set; }

    /// <summary>Off by default. Dental cases, sittings, replacements,
    /// packages and per-case payment collection.</summary>
    public bool DentistEnabled { get; set; }

    /// <summary>Off by default. The in-house lab module — analyte-level test
    /// master, report/panel configuration, packages, and result entry with a
    /// printed report. Distinct from <see cref="DiagnosticsEnabled"/>, which
    /// stays a flat named-test billing module for ad-hoc or sent-out work.</summary>
    public bool PathologyLabEnabled { get; set; }

    /// <summary>
    /// Off by default. The seeded Admin account exists either way, so
    /// turning it on is immediately usable without a separate setup step.
    /// On the SaaS edition this sits alongside, not instead of, the
    /// platform-level tenant login — see SAAS_MIGRATION.md's authorisation
    /// note.
    /// </summary>
    public bool RequireLogin { get; set; }

    /// <summary>
    /// Whether at least one clinical module — OPD, Pharmacy, or any of the
    /// newer ones — is switched on. All seven off at once would leave an
    /// empty nav bar and an unusable application, so
    /// <see cref="SettingsService.SaveGeneralAsync"/> refuses to save that
    /// state; this is what it checks.
    /// </summary>
    public bool AnyModuleEnabled =>
        OpdEnabled || PharmacyEnabled || DiagnosticsEnabled || AppointmentsEnabled
        || PediatricsEnabled || DentistEnabled || PathologyLabEnabled;
}

public enum QueueLayout
{
    Tiles = 0,
    Rows = 1
}

/// <summary>
/// Which sitting the OPD screen is showing. Full day is the safe default: it
/// hides nobody, which matters because a visit booked outside both windows —
/// two in the afternoon, say — belongs to neither session.
/// </summary>
public enum ClinicSession
{
    FullDay = 0,
    Morning = 1,
    Evening = 2
}

/// <summary>
/// Everything a tenant remembers between sessions: who the clinic and the
/// pharmacy are, how documents are branded, and how the software itself
/// behaves — every module toggle from the desktop's Settings → Features
/// carries over unchanged, so a clinic that registers for the web edition
/// gets the same configurability. Backed by one key/value table per tenant
/// (AppDbContext's global filter already scopes every read/write here to
/// the caller's clinic), so every field here is a row, not a column.
/// </summary>
public class SettingsService(IDbContextFactory<AppDbContext> factory)
{
    // ── Clinic ─────────────────────────────────────────────────────────────
    private const string KeyClinicName = "clinic.name";
    private const string KeyClinicAddress = "clinic.address";
    private const string KeyClinicAddress2 = "clinic.address2";
    private const string KeyClinicPhone = "clinic.phone";
    private const string KeyClinicGstRegistered = "clinic.gstregistered";
    private const string KeyClinicGstin = "clinic.gstin";
    private const string KeyClinicMorningFrom = "clinic.morningfrom";
    private const string KeyClinicMorningTo = "clinic.morningto";
    private const string KeyClinicEveningFrom = "clinic.eveningfrom";
    private const string KeyClinicEveningTo = "clinic.eveningto";
    private const string KeyClinicFooter = "clinic.footer";

    // ── Pharmacy ───────────────────────────────────────────────────────────
    private const string KeyPharmacyName = "pharmacy.name";
    private const string KeyPharmacyAddress = "pharmacy.address";
    private const string KeyPharmacyAddress2 = "pharmacy.address2";
    private const string KeyPharmacyPhone = "pharmacy.phone";
    private const string KeyPharmacyGstRegistered = "pharmacy.gstregistered";
    private const string KeyPharmacyGstin = "pharmacy.gstin";
    private const string KeyPharmacyLicence = "pharmacy.druglicence";
    private const string KeyPharmacyPharmacist = "pharmacy.pharmacist";
    private const string KeyPharmacyFooter = "pharmacy.footer";

    // ── Document branding ─────────────────────────────────────────────────
    private const string KeyDocsFooter = "docs.footer";
    private const string KeyDocsLogoBase64 = "docs.logo.base64";
    private const string KeyDocsLogoContentType = "docs.logo.contenttype";
    private const string KeyDocsPrintFontFamily = "docs.print.fontfamily";
    private const string KeyDocsPrintFontSizeDelta = "docs.print.fontsizedelta";
    private const string KeyDocsTitleFontFamily = "docs.title.fontfamily";
    private const string KeyDocsTitleFontSizeDelta = "docs.title.fontsizedelta";

    // ── General ────────────────────────────────────────────────────────────
    private const string KeyQueueLayout = "opd.queuelayout";
    private const string KeyTheme = "ui.theme";
    private const string KeyDiagnosticsEnabled = "features.diagnostics.enabled";
    private const string KeyOpdEnabled = "features.opd.enabled";
    private const string KeyPharmacyEnabled = "features.pharmacy.enabled";
    private const string KeyAppointmentsEnabled = "features.appointments.enabled";
    private const string KeyPediatricsEnabled = "features.pediatrics.enabled";
    private const string KeyDentistEnabled = "features.dentist.enabled";
    private const string KeyPathologyLabEnabled = "features.pathologylab.enabled";
    private const string KeyRequireLogin = "auth.requirelogin";

    public async Task<ClinicProfile> GetClinicAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var map = await db.Settings.ToDictionaryAsync(s => s.Key, s => s.Value);

        var fallback = new ClinicProfile();
        return new ClinicProfile
        {
            Name = Get(map, KeyClinicName, fallback.Name),
            AddressLine = Get(map, KeyClinicAddress, fallback.AddressLine),
            AddressLine2 = Get(map, KeyClinicAddress2, fallback.AddressLine2),
            Phone = Get(map, KeyClinicPhone, fallback.Phone),
            GstRegistered = Bool(map, KeyClinicGstRegistered),
            Gstin = Get(map, KeyClinicGstin, fallback.Gstin),
            FooterText = Get(map, KeyClinicFooter, fallback.FooterText),
            MorningFrom = Time(map, KeyClinicMorningFrom, fallback.MorningFrom),
            MorningTo = Time(map, KeyClinicMorningTo, fallback.MorningTo),
            EveningFrom = Time(map, KeyClinicEveningFrom, fallback.EveningFrom),
            EveningTo = Time(map, KeyClinicEveningTo, fallback.EveningTo)
        };
    }

    public async Task SaveClinicAsync(ClinicProfile profile)
    {
        await using var db = await factory.CreateDbContextAsync();

        await SetAsync(db, KeyClinicName, profile.Name);
        await SetAsync(db, KeyClinicAddress, profile.AddressLine);
        await SetAsync(db, KeyClinicAddress2, profile.AddressLine2);
        await SetAsync(db, KeyClinicPhone, profile.Phone);
        await SetAsync(db, KeyClinicGstRegistered, profile.GstRegistered.ToString());
        await SetAsync(db, KeyClinicGstin, profile.Gstin);
        await SetAsync(db, KeyClinicFooter, profile.FooterText);

        // "hh\:mm" rather than the default, which would write 10:00:00 and read
        // back the same — correct, but nobody hand-editing the table wants to
        // count the colons.
        await SetAsync(db, KeyClinicMorningFrom, profile.MorningFrom.ToString(@"hh\:mm"));
        await SetAsync(db, KeyClinicMorningTo, profile.MorningTo.ToString(@"hh\:mm"));
        await SetAsync(db, KeyClinicEveningFrom, profile.EveningFrom.ToString(@"hh\:mm"));
        await SetAsync(db, KeyClinicEveningTo, profile.EveningTo.ToString(@"hh\:mm"));

        await db.SaveChangesAsync();
    }

    public async Task<PharmacyProfile> GetPharmacyAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var map = await db.Settings.ToDictionaryAsync(s => s.Key, s => s.Value);

        var fallback = new PharmacyProfile();
        return new PharmacyProfile
        {
            Name = Get(map, KeyPharmacyName, fallback.Name),
            AddressLine = Get(map, KeyPharmacyAddress, fallback.AddressLine),
            AddressLine2 = Get(map, KeyPharmacyAddress2, fallback.AddressLine2),
            Phone = Get(map, KeyPharmacyPhone, fallback.Phone),
            GstRegistered = Bool(map, KeyPharmacyGstRegistered),
            Gstin = Get(map, KeyPharmacyGstin, fallback.Gstin),
            DrugLicenceNo = Get(map, KeyPharmacyLicence, fallback.DrugLicenceNo),
            PharmacistName = Get(map, KeyPharmacyPharmacist, fallback.PharmacistName),
            FooterText = Get(map, KeyPharmacyFooter, fallback.FooterText)
        };
    }

    public async Task SavePharmacyAsync(PharmacyProfile profile)
    {
        await using var db = await factory.CreateDbContextAsync();

        await SetAsync(db, KeyPharmacyName, profile.Name);
        await SetAsync(db, KeyPharmacyAddress, profile.AddressLine);
        await SetAsync(db, KeyPharmacyAddress2, profile.AddressLine2);
        await SetAsync(db, KeyPharmacyPhone, profile.Phone);
        await SetAsync(db, KeyPharmacyGstRegistered, profile.GstRegistered.ToString());
        await SetAsync(db, KeyPharmacyGstin, profile.Gstin);
        await SetAsync(db, KeyPharmacyLicence, profile.DrugLicenceNo);
        await SetAsync(db, KeyPharmacyPharmacist, profile.PharmacistName);
        await SetAsync(db, KeyPharmacyFooter, profile.FooterText);

        await db.SaveChangesAsync();
    }

    public async Task<DocumentTheme> GetDocumentThemeAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var map = await db.Settings.ToDictionaryAsync(s => s.Key, s => s.Value);

        var fallback = new DocumentTheme();
        return new DocumentTheme
        {
            Footer = Get(map, KeyDocsFooter, fallback.Footer),
            LogoBase64 = map.TryGetValue(KeyDocsLogoBase64, out var b64) && !string.IsNullOrWhiteSpace(b64) ? b64 : null,
            LogoContentType = map.TryGetValue(KeyDocsLogoContentType, out var ct) && !string.IsNullOrWhiteSpace(ct) ? ct : null,
            PrintFontFamily = map.TryGetValue(KeyDocsPrintFontFamily, out var font) && !string.IsNullOrWhiteSpace(font) ? font : null,
            PrintFontSizeDelta = Double(map, KeyDocsPrintFontSizeDelta, fallback.PrintFontSizeDelta),
            TitleFontFamily = map.TryGetValue(KeyDocsTitleFontFamily, out var titleFont) && !string.IsNullOrWhiteSpace(titleFont) ? titleFont : null,
            TitleFontSizeDelta = Double(map, KeyDocsTitleFontSizeDelta, fallback.TitleFontSizeDelta)
        };
    }

    public async Task SaveDocumentThemeAsync(DocumentTheme theme)
    {
        await using var db = await factory.CreateDbContextAsync();

        await SetAsync(db, KeyDocsFooter, theme.Footer);
        await SetAsync(db, KeyDocsLogoBase64, theme.LogoBase64 ?? "");
        await SetAsync(db, KeyDocsLogoContentType, theme.LogoContentType ?? "");
        await SetAsync(db, KeyDocsPrintFontFamily, theme.PrintFontFamily ?? "");
        await SetAsync(db, KeyDocsPrintFontSizeDelta, theme.PrintFontSizeDelta.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await SetAsync(db, KeyDocsTitleFontFamily, theme.TitleFontFamily ?? "");
        await SetAsync(db, KeyDocsTitleFontSizeDelta, theme.TitleFontSizeDelta.ToString(System.Globalization.CultureInfo.InvariantCulture));

        await db.SaveChangesAsync();
    }

    public async Task<GeneralSettings> GetGeneralAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var map = await db.Settings.ToDictionaryAsync(s => s.Key, s => s.Value);

        var fallback = new GeneralSettings();
        return new GeneralSettings
        {
            QueueLayout = Enum.TryParse<QueueLayout>(Get(map, KeyQueueLayout, ""), out var layout)
                ? layout : fallback.QueueLayout,
            Theme = Enum.TryParse<AppThemeKind>(Get(map, KeyTheme, ""), out var theme)
                ? theme : fallback.Theme,
            DiagnosticsEnabled = Bool(map, KeyDiagnosticsEnabled),
            OpdEnabled = BoolDefault(map, KeyOpdEnabled, true),
            PharmacyEnabled = BoolDefault(map, KeyPharmacyEnabled, true),
            AppointmentsEnabled = Bool(map, KeyAppointmentsEnabled),
            PediatricsEnabled = Bool(map, KeyPediatricsEnabled),
            DentistEnabled = Bool(map, KeyDentistEnabled),
            PathologyLabEnabled = Bool(map, KeyPathologyLabEnabled),
            RequireLogin = Bool(map, KeyRequireLogin)
        };
    }

    /// <summary>
    /// Refuses to write a state where every module — OPD, Pharmacy and every
    /// newer one — is off at once: that would empty the nav bar and leave
    /// nothing to turn a module back on from. See
    /// <see cref="GeneralSettings.AnyModuleEnabled"/>.
    /// </summary>
    public async Task SaveGeneralAsync(GeneralSettings settings)
    {
        if (!settings.AnyModuleEnabled)
            throw new InvalidOperationException(
                "At least one module — OPD, Pharmacy, or one of the others — has to stay switched on.");

        await using var db = await factory.CreateDbContextAsync();

        await SetAsync(db, KeyQueueLayout, settings.QueueLayout.ToString());
        await SetAsync(db, KeyTheme, settings.Theme.ToString());
        await SetAsync(db, KeyDiagnosticsEnabled, settings.DiagnosticsEnabled.ToString());
        await SetAsync(db, KeyOpdEnabled, settings.OpdEnabled.ToString());
        await SetAsync(db, KeyPharmacyEnabled, settings.PharmacyEnabled.ToString());
        await SetAsync(db, KeyAppointmentsEnabled, settings.AppointmentsEnabled.ToString());
        await SetAsync(db, KeyPediatricsEnabled, settings.PediatricsEnabled.ToString());
        await SetAsync(db, KeyDentistEnabled, settings.DentistEnabled.ToString());
        await SetAsync(db, KeyPathologyLabEnabled, settings.PathologyLabEnabled.ToString());
        await SetAsync(db, KeyRequireLogin, settings.RequireLogin.ToString());

        await db.SaveChangesAsync();
    }

    private static string Get(Dictionary<string, string> map, string key, string fallback)
        => map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static bool Bool(Dictionary<string, string> map, string key)
        => bool.TryParse(Get(map, key, ""), out var value) && value;

    /// <summary>Like <see cref="Bool"/>, but for a toggle that defaults to
    /// <c>true</c> rather than <c>false</c> when the row does not exist yet —
    /// see <see cref="GeneralSettings.OpdEnabled"/> and
    /// <see cref="GeneralSettings.PharmacyEnabled"/>.</summary>
    private static bool BoolDefault(Dictionary<string, string> map, string key, bool fallback)
        => bool.TryParse(Get(map, key, ""), out var value) ? value : fallback;

    /// <summary>
    /// A stored time, or the default when the row is missing or unreadable. An
    /// unparseable session window would otherwise hide the whole queue.
    /// </summary>
    private static TimeSpan Time(Dictionary<string, string> map, string key, TimeSpan fallback)
        => TimeSpan.TryParse(Get(map, key, ""), out var parsed) ? parsed : fallback;

    /// <summary>A stored number, or the default when the row is missing or
    /// unreadable — same reasoning as <see cref="Time"/>.</summary>
    private static double Double(Dictionary<string, string> map, string key, double fallback)
        => double.TryParse(Get(map, key, ""), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static async Task SetAsync(AppDbContext db, string key, string? value)
    {
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key);
        if (row is null)
            db.Settings.Add(new Setting { Key = key, Value = value ?? "" });
        else
            row.Value = value ?? "";
    }
}
