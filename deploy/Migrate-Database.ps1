<#
.SYNOPSIS
    Backs up the PostgreSQL database, applies any pending EF Core migrations,
    and verifies the result. The release step Deploy-Production.ps1 says to
    run before it.

.DESCRIPTION
    Deploy-Production.ps1 deliberately does not touch the schema:

        "apply migrations - that is a release step of its own, run before this"

    This is that step. It did not exist until the first deployment made it
    matter: before there was production data, a broken migration cost a
    developer ten minutes; now it costs a clinic its records.

    The order is the whole point.

        1. read the Database section of appsettings.Production.json, and check
           the server is reachable and the role signs in
        2. BACK UP - always, even when nothing is pending
        3. list what is pending, and stop here if -DryRun
        4. apply
        5. verify, and print the exact restore command for the backup taken

    Safe to run repeatedly. With nothing pending it takes a backup, reports
    "already current" and changes nothing - which makes it a reasonable thing
    to run before every deployment without thinking about whether the release
    happens to contain a migration.

    It never drops, never resets and never runs `EnsureCreated`. The only
    thing it does to the schema is apply migrations that the repository
    contains and the database has not yet recorded.

    Where the database is comes from the application's own settings file,
    never from a parameter: a backup, a migration and the API itself must all
    mean the same database, and one place to change it is how that stays true.

.PARAMETER Root
    Where the published application lives - the folder holding
    appsettings.Production.json. Default C:\SivayaanHMS\api

.PARAMETER BackupRoot
    Where the pre-migration backup is written. Default C:\SivayaanHMS\DBBackup.
    Put this on a different physical disk from the data directory if you have
    one: a backup beside the database protects against a bad migration, not
    against a failed disk.

.PARAMETER DryRun
    Back up and report what would be applied, then stop. Use this first on any
    release you have not run before.

.PARAMETER PgBin
    The PostgreSQL bin folder holding psql.exe and pg_dump.exe. Found under
    C:\Program Files\PostgreSQL\<version>\bin when omitted; the installer does
    not put it on PATH.

.EXAMPLE
    .\Migrate-Database.ps1 -DryRun
.EXAMPLE
    .\Migrate-Database.ps1
#>

[CmdletBinding()]
param(
    [string] $Root       = 'C:\SivayaanHMS\api',
    [string] $BackupRoot = 'C:\SivayaanHMS\DBBackup',
    [switch] $DryRun,
    [string] $PgBin
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'PostgresSettings.ps1')

function Say  ([string] $m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Warn ([string] $m) { Write-Host "  ! $m"  -ForegroundColor Yellow }
function Die  ([string] $m) { Write-Host "  x $m"  -ForegroundColor Red; exit 1 }
function Note ([string] $m) { Write-Host "    $m" }

$DataProj = Join-Path $PSScriptRoot '..\backend\src\SivayaanHMS.Data'
$Stamp    = Get-Date -Format 'yyyyMMdd-HHmmss'

# ------------------------------------------------------------------ 1. reachable

Say "Reading the Database section of $Root\appsettings.Production.json"

$db = Read-PostgresSettings -AppRoot $Root
Note "Host $($db.Host):$($db.Port)  database $($db.Name)  role $($db.Username)"

$psql   = Find-PgTool -Name 'psql'    -PgBin $PgBin
$pgdump = Find-PgTool -Name 'pg_dump' -PgBin $PgBin
Note "Using $psql"

# The password goes to the PostgreSQL tools through the environment, which is
# what they read it from - never on a command line, where it would show up in
# the process list and in any log of this script's output.
$env:PGPASSWORD = $db.Password

# -X skips psqlrc, -q -t -A make the output a bare value, -v ON_ERROR_STOP=1
# makes a failed statement a failed command instead of a printed error and a
# zero exit code the script would march past.
$PsqlArgs = @('-X', '-q', '-t', '-A', '-v', 'ON_ERROR_STOP=1',
              '-h', $db.Host, '-p', $db.Port, '-U', $db.Username, '-d', $db.Name)

function Invoke-Sql([string] $query) {
    $out = & $psql @PsqlArgs -c $query 2>&1
    if ($LASTEXITCODE -ne 0) { Die "SQL failed: $out" }
    return @($out | Where-Object { $_ -and "$_".Trim() })
}

function Invoke-SqlScalar([string] $query) {
    # @() at the call site, because PowerShell unrolls a single-element array
    # on return: without it a one-row answer arrives as a plain string and
    # $rows[0] indexes its first *character*.
    $rows = @(Invoke-Sql $query)
    if ($rows.Count -eq 0) { return '' }
    return "$($rows[0])".Trim()
}

$who = Invoke-SqlScalar "SELECT current_database() || ' as ' || current_user || ' on PostgreSQL ' || current_setting('server_version');"
Note "Connected: $who"

# Refuse to touch a database that has never been migrated. Migrating one into
# existence here would hide a much more interesting problem - that the API is
# pointed at the wrong server.
$has = Invoke-SqlScalar "SELECT CASE WHEN to_regclass('""__EFMigrationsHistory""') IS NULL THEN 'no' ELSE 'yes' END;"
if ($has -ne 'yes') {
    Die "$($db.Name) has no __EFMigrationsHistory table. This is a first install, not an upgrade - see docs/FIRST_DEPLOYMENT.md."
}

$applied = @(Invoke-Sql 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";')
Note "Applied migrations: $($applied.Count)"

# ------------------------------------------------------------------- 2. back up

# Before listing what is pending, not after. If the backup cannot be taken,
# nothing else in this script should happen at all.
Say 'Backing up'

if (-not (Test-Path $BackupRoot)) { New-Item -ItemType Directory -Force $BackupRoot | Out-Null }

# Custom format (-Fc): compressed, and restorable table-by-table with
# pg_restore, which a plain SQL dump is not. Not --clean here; the restore
# command printed at the end adds --clean --if-exists, so the choice to
# overwrite is made by the person restoring, at the time they restore.
$BackupFile = Join-Path $BackupRoot "$($db.Name)-pre-migration-$Stamp.dump"

& $pgdump -h $db.Host -p $db.Port -U $db.Username -d $db.Name -Fc --no-owner --no-privileges -f $BackupFile 2>&1 |
    ForEach-Object { if ($_ -match 'error|fatal') { Die "pg_dump: $_" } }
if ($LASTEXITCODE -ne 0) { Die 'pg_dump failed.' }

if (-not (Test-Path $BackupFile)) { Die "pg_dump reported success but $BackupFile does not exist." }

# pg_restore --list reads the archive's table of contents back, which fails
# on a truncated or corrupt file. A backup nobody has verified is a hope, not
# a rollback.
$pgrestore = Find-PgTool -Name 'pg_restore' -PgBin $PgBin
$toc = & $pgrestore --list $BackupFile 2>&1
if ($LASTEXITCODE -ne 0) { Die "The backup does not read back: $toc" }

$sizeMb = [math]::Round((Get-Item $BackupFile).Length / 1MB, 1)
Note "$BackupFile  ($sizeMb MB, verified)"

# --------------------------------------------------------------- 3. what is due

Say 'Checking for pending migrations'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Die 'dotnet SDK not found. Needed to read the migration list; see the bundle alternative in docs/DATABASE_RELEASES.md.'
}

$efOk = & dotnet ef --version 2>&1
if ($LASTEXITCODE -ne 0) {
    Warn 'dotnet-ef tool not installed. Installing it globally.'
    & dotnet tool install --global dotnet-ef 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Die 'Could not install dotnet-ef. See docs/DATABASE_RELEASES.md for the bundle route.' }
}

# The design-time factory reads this rather than appsettings, so the migration
# runs against exactly the database read above and nothing else.
$env:SIVAYAANHMS_CONNECTION = $db.ConnectionString

# Always rebuild before reading the migration list. `dotnet ef` loads the
# compiled assembly, not the source, so a stale binary reports "No migrations
# were found" for a migration sitting in front of you.
Say 'Building'
& dotnet build $DataProj -v q --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { Die 'Build failed. Nothing was changed.' }

$list = & dotnet ef migrations list --project $DataProj --startup-project $DataProj --no-build 2>&1
$pending = $list | Where-Object { $_ -match '\(Pending\)' }

if (-not $pending) {
    Say 'Already current - no migrations pending.'
    Note "Backup kept at $BackupFile"
    exit 0
}

Write-Host ''
Warn "Pending migrations ($($pending.Count)):"
$pending | ForEach-Object { Write-Host "      $($_ -replace '\s*\(Pending\)','')" }
Write-Host ''

if ($DryRun) {
    Say 'Dry run - nothing applied.'
    Note "Backup kept at $BackupFile"
    exit 0
}

# ---------------------------------------------------------------- 4. apply them

Say 'Applying'

# `database update` rather than piping a script through psql: each migration
# runs in its own transaction, so a failure rolls that migration back rather
# than leaving the schema half-way through it, and it writes
# __EFMigrationsHistory itself rather than trusting a generated file to.
& dotnet ef database update --project $DataProj --startup-project $DataProj --no-build
if ($LASTEXITCODE -ne 0) {
    Write-Host ''
    Die @"
Migration FAILED. PostgreSQL rolls a failed migration back on its own, so the
schema is at whichever migration last succeeded - but check before trusting
that, and restore the backup taken at the start of this run if in doubt:

    Stop-Service SivayaanHMSApi
    `$env:PGPASSWORD = '<the password from appsettings.Production.json>'
    & "$pgrestore" -h $($db.Host) -p $($db.Port) -U $($db.Username) -d $($db.Name) --clean --if-exists --no-owner --no-privileges "$BackupFile"
    Start-Service SivayaanHMSApi
"@
}

# ------------------------------------------------------------------- 5. verify

Say 'Verifying'

$after = @(Invoke-Sql 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";')
Note "Applied migrations: $($applied.Count) -> $($after.Count)"

$still = & dotnet ef migrations list --project $DataProj --startup-project $DataProj --no-build 2>&1 |
         Where-Object { $_ -match '\(Pending\)' }
if ($still) { Die 'Migrations still pending after the run. Investigate before starting the API.' }

# The duplicate-medicine guard is a partial unique index. Cheap to check, and
# its absence is silent otherwise.
$ix = Invoke-SqlScalar "SELECT CASE WHEN EXISTS(SELECT 1 FROM pg_indexes WHERE indexname='IX_Products_TenantId_SearchKey') THEN 'present' ELSE 'MISSING' END;"
if ($ix -ne 'present') {
    Warn 'IX_Products_TenantId_SearchKey is missing - the duplicate-medicine guard is not in place. See docs/DATABASE_RELEASES.md.'
}

Write-Host ''
Say 'Done.'
Note "Backup: $BackupFile"
Note 'Now run Deploy-Production.ps1 to publish the API over the top.'
