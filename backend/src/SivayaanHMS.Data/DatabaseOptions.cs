using Npgsql;

namespace SivayaanHMS.Data;

/// <summary>
/// Everything the application knows about where its PostgreSQL database is
/// and how to talk to it, bound from the <c>Database</c> section of
/// appsettings — one place, every knob, no connection string assembled by
/// hand anywhere else.
///
/// <para>
/// Each field is its own key rather than one long connection string because
/// the people who edit this file on a clinic's server are changing exactly
/// one thing at a time — a password, a host after a database move — and
/// should not have to find it inside a semicolon-separated line. Every key
/// can also be supplied as an environment variable in the usual ASP.NET
/// shape, <c>Database__Password</c> say, which is how the UAT harness points
/// a run at a throwaway database without a file.
/// </para>
///
/// <para>
/// <see cref="ConnectionString"/> is the escape hatch: when set, it is used
/// verbatim and every individual field is ignored. For a managed host whose
/// console hands you a complete string, or a URI-shaped one, that is easier
/// than picking it apart.
/// </para>
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Machine running PostgreSQL. <c>localhost</c> for the
    /// clinic-PC deployment; a hostname for a managed server.</summary>
    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 5432;

    /// <summary>The database name. Lower-case: PostgreSQL folds unquoted
    /// identifiers to lower case, and a mixed-case name has to be quoted in
    /// every psql command forever after.</summary>
    public string Name { get; set; } = "sivayaanhms";

    /// <summary>The application role — owner of the database, never a
    /// superuser. See docs/POSTGRESQL_SETUP.md for creating it.</summary>
    public string Username { get; set; } = "sivayaanhms";

    /// <summary>Empty in appsettings.json on purpose (that file is committed).
    /// Supplied per environment: appsettings.Local.json in development,
    /// appsettings.Production.json on a server, or <c>Database__Password</c>.</summary>
    public string Password { get; set; } = "";

    /// <summary>
    /// <c>Prefer</c> for a server on this machine; <c>Require</c> — or
    /// <c>VerifyFull</c> with the provider's CA — for anything reached over a
    /// network. Cloud providers insist on it, and traffic across a network
    /// without it is readable. Parsed by Npgsql: Disable, Allow, Prefer,
    /// Require, VerifyCA, VerifyFull.
    /// </summary>
    public string SslMode { get; set; } = "Prefer";

    /// <summary>Connection pooling. On, always, outside a diagnostic — every
    /// domain service opens a short-lived context per call, and without a
    /// pool each of those is a fresh TCP handshake and authentication.</summary>
    public bool Pooling { get; set; } = true;

    public int MinPoolSize { get; set; } = 0;

    public int MaxPoolSize { get; set; } = 50;

    /// <summary>Seconds a single statement may run before Npgsql gives up on
    /// it. 30 is Npgsql's own default; the year-end GST summary over a big
    /// clinic's sales is the one query that has ever approached it.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Seconds to wait for the server to accept a connection.
    /// Longer than the default 15 on purpose: after a reboot the API service
    /// can start before PostgreSQL is listening, and a process that has
    /// already died cannot notice the database arriving a moment later.</summary>
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    /// <summary>Shows up in <c>pg_stat_activity.application_name</c>, so a
    /// DBA looking at the server can tell this application's sessions from
    /// anything else using the same role.</summary>
    public string ApplicationName { get; set; } = "SivayaanHMS";

    /// <summary>
    /// Apply pending EF Core migrations when the API starts. Null — the
    /// default — means "yes in Development, no otherwise": a developer wants
    /// a fresh checkout to just work, while a production release applies
    /// migrations as its own step (deploy/Migrate-Database.ps1) and never as
    /// a race between service starts.
    /// </summary>
    public bool? MigrateOnStartup { get; set; }

    /// <summary>A complete Npgsql connection string. When set, wins over every
    /// individual field above.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The one connection string the application ever uses, built from the
    /// fields above — or <see cref="ConnectionString"/> verbatim when that is
    /// set. Goes through <see cref="NpgsqlConnectionStringBuilder"/> rather
    /// than string concatenation so a password containing a semicolon or a
    /// quote is escaped correctly instead of silently truncating the string.
    /// </summary>
    public string BuildConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionString))
            return ConnectionString.Trim();

        if (string.IsNullOrWhiteSpace(Host)) throw new InvalidOperationException("Database:Host is not configured.");
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidOperationException("Database:Name is not configured.");
        if (string.IsNullOrWhiteSpace(Username)) throw new InvalidOperationException("Database:Username is not configured.");

        if (!Enum.TryParse<SslMode>(SslMode, ignoreCase: true, out var sslMode))
            throw new InvalidOperationException(
                $"Database:SslMode '{SslMode}' is not one of Disable, Allow, Prefer, Require, VerifyCA, VerifyFull.");

        return new NpgsqlConnectionStringBuilder
        {
            Host = Host.Trim(),
            Port = Port,
            Database = Name.Trim(),
            Username = Username.Trim(),
            Password = Password,
            SslMode = sslMode,
            Pooling = Pooling,
            MinPoolSize = MinPoolSize,
            MaxPoolSize = MaxPoolSize,
            CommandTimeout = CommandTimeoutSeconds,
            Timeout = ConnectionTimeoutSeconds,
            ApplicationName = ApplicationName,
        }.ConnectionString;
    }
}
