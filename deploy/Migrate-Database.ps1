<#
.SYNOPSIS
    Backs up HMSLite, applies any pending EF Core migrations, and verifies the
    result. The release step Deploy-Production.ps1 says to run before it.

.DESCRIPTION
    Deploy-Production.ps1 deliberately does not touch the schema:

        "apply migrations - that is a release step of its own, run before this"

    This is that step. It did not exist until the first deployment made it
    matter: before there was production data, a broken migration cost a
    developer ten minutes; now it costs a clinic its records.

    The order is the whole point.

        1. check the server is reachable and the login works
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

.PARAMETER SqlInstance
    Instance name without the machine prefix. Must match the one the API
    connects to.

.PARAMETER Database
    Default HMSLite.

.PARAMETER BackupRoot
    Where the pre-migration backup is written. Default C:\SivayaanHMS\DBBackup.
    Put this on a different physical disk from the data files if you have one:
    a backup beside the database protects against a bad migration, not against
    a failed disk.

.PARAMETER DryRun
    Back up and report what would be applied, then stop. Use this first on any
    release you have not run before.

.PARAMETER SqlUser
    Omit to use Windows authentication, which is the default and needs no
    password anywhere. Supply it only if the server refuses that.

.EXAMPLE
    .\Migrate-Database.ps1 -SqlInstance SIVASQLEXPRESS -DryRun
.EXAMPLE
    .\Migrate-Database.ps1 -SqlInstance SIVASQLEXPRESS
#>

[CmdletBinding()]
param(
    [string] $SqlInstance = 'SQLEXPRESS',
    [string] $Database    = 'HMSLite',
    [string] $BackupRoot  = 'C:\SivayaanHMS\DBBackup',
    [switch] $DryRun,
    [string] $SqlUser,
    [string] $SqlPassword
)

$ErrorActionPreference = 'Stop'

function Say  ([string] $m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Warn ([string] $m) { Write-Host "  ! $m"  -ForegroundColor Yellow }
function Die  ([string] $m) { Write-Host "  x $m"  -ForegroundColor Red; exit 1 }
function Note ([string] $m) { Write-Host "    $m" }

$Server     = ".\$SqlInstance"
$DataProj   = Join-Path $PSScriptRoot '..\backend\src\SivayaanHMS.Data'
$Stamp      = Get-Date -Format 'yyyyMMdd-HHmmss'
$BackupFile = Join-Path $BackupRoot "$Database-pre-migration-$Stamp.bak"

# sqlcmd arguments shared by every call below.
#
# -C trusts the server certificate. A default Express instance presents a
# self-signed one, and recent sqlcmd versions reject it by default with an
# error that names neither certificates nor trust. Without -C nothing here
# ever reaches the database.
#
# -I sets QUOTED_IDENTIFIER ON. The Products table carries a filtered unique
# index, and SQL Server refuses *any* write to a table with one unless that
# option is on - not just index creation. sqlcmd defaults it off.
#
# -b makes sqlcmd exit non-zero on error instead of printing the error and
# reporting success, which would let this script march past a failed backup.
$SqlArgs = @('-S', $Server, '-C', '-I', '-b')
if ($SqlUser) { $SqlArgs += @('-U', $SqlUser, '-P', $SqlPassword) } else { $SqlArgs += '-E' }

# SET NOCOUNT ON, because "(1 rows affected)" arrives on stdout alongside the
# answer and a caller comparing the whole output to a value never matches.
function Invoke-Sql([string] $query, [string] $db = $Database) {
    $out = & sqlcmd @SqlArgs '-d' $db '-h' '-1' '-W' '-Q' "SET NOCOUNT ON; $query" 2>&1
    if ($LASTEXITCODE -ne 0) { Die "SQL failed: $out" }
    return @($out | Where-Object { $_ -and "$_".Trim() -and "$_" -notmatch 'rows affected' })
}

# One scalar, trimmed - for the checks that compare against a single value.
function Invoke-SqlScalar([string] $query, [string] $db = $Database) {
    # @() at the call site, because PowerShell unrolls a single-element array
    # on return: without it a one-row answer arrives as a plain string and
    # $rows[0] indexes its first *character*.
    $rows = @(Invoke-Sql $query $db)
    if ($rows.Count -eq 0) { return '' }
    return "$($rows[0])".Trim()
}

# ------------------------------------------------------------------ 1. reachable

Say "Checking $Server / $Database"

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    Die 'sqlcmd not found on PATH. Install the SQL Server command line tools.'
}

$who = Invoke-SqlScalar "SELECT DB_NAME() + ' as ' + SUSER_NAME();"
Note "Connected: $who"

# Refuse to touch a database that has never been created. Migrating one into
# existence here would hide a much more interesting problem - that the API is
# pointed at the wrong server.
$has = Invoke-SqlScalar "SELECT CASE WHEN OBJECT_ID('__EFMigrationsHistory') IS NULL THEN 'no' ELSE 'yes' END;"
if ($has -ne 'yes') {
    Die "$Database has no __EFMigrationsHistory table. This is a first install, not an upgrade - see docs/FIRST_DEPLOYMENT.md."
}

$applied = (Invoke-Sql 'SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;') |
           Where-Object { $_ -and $_.Trim() }
Note "Applied migrations: $($applied.Count)"

# ------------------------------------------------------------------- 2. back up

# Before listing what is pending, not after. If the backup cannot be taken,
# nothing else in this script should happen at all.
Say 'Backing up'

if (-not (Test-Path $BackupRoot)) { New-Item -ItemType Directory -Force $BackupRoot | Out-Null }

# COPY_ONLY so this does not disturb whatever backup chain a clinic may set up
# later. A pre-migration snapshot is not part of a schedule.
Invoke-Sql "BACKUP DATABASE [$Database] TO DISK = N'$BackupFile' WITH INIT, COPY_ONLY, CHECKSUM, STATS = 25;" 'master' | Out-Null

if (-not (Test-Path $BackupFile)) { Die "Backup reported success but $BackupFile does not exist." }

# RESTORE VERIFYONLY reads the file back and checks the checksums. A backup
# nobody has verified is a hope, not a rollback.
Invoke-Sql "RESTORE VERIFYONLY FROM DISK = N'$BackupFile' WITH CHECKSUM;" 'master' | Out-Null

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
# runs against exactly the database named here and not whatever the API
# happens to be configured with.
$conn = "Server=$Server;Database=$Database;TrustServerCertificate=True;"
$conn += if ($SqlUser) { "User ID=$SqlUser;Password=$SqlPassword;" } else { 'Trusted_Connection=True;' }
$env:SIVAYAANHMS_CONNECTION = $conn

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

# `database update` rather than piping a script through sqlcmd: it sets its own
# SET options, so the QUOTED_IDENTIFIER trap above cannot bite, and it writes
# __EFMigrationsHistory itself rather than trusting a generated file to.
& dotnet ef database update --project $DataProj --startup-project $DataProj --no-build
if ($LASTEXITCODE -ne 0) {
    Write-Host ''
    Die @"
Migration FAILED. The database may be part-way through.

  Restore the backup taken at the start of this run:

    sqlcmd -S "$Server" -C -I -b -E -d master -Q "ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [$Database] FROM DISK = N'$BackupFile' WITH REPLACE; ALTER DATABASE [$Database] SET MULTI_USER;"

  Stop the API first if it is running: Stop-Service SivayaanHMSApi
"@
}

# ------------------------------------------------------------------- 5. verify

Say 'Verifying'

$after = (Invoke-Sql 'SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;') |
         Where-Object { $_ -and $_.Trim() }
Note "Applied migrations: $($applied.Count) -> $($after.Count)"

$still = & dotnet ef migrations list --project $DataProj --startup-project $DataProj --no-build 2>&1 |
         Where-Object { $_ -match '\(Pending\)' }
if ($still) { Die 'Migrations still pending after the run. Investigate before starting the API.' }

# The one that goes missing quietly if a migration is ever applied through
# sqlcmd without -I. Cheap to check, and its absence is silent otherwise.
$ix = Invoke-SqlScalar "SELECT CASE WHEN EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_Products_TenantId_SearchKey') THEN 'present' ELSE 'MISSING' END;"
if ($ix -ne 'present') {
    Warn 'IX_Products_TenantId_SearchKey is missing - the duplicate-medicine guard is not in place. See docs/DATABASE_RELEASES.md.'
}

Write-Host ''
Say 'Done.'
Note "Backup: $BackupFile"
Note 'Now run Deploy-Production.ps1 to publish the API over the top.'
