<#
.SYNOPSIS
    Publishes the Sivayaan HMS API and runs it as a Windows service on port 6051.

.DESCRIPTION
    Safe to run repeatedly. On the first run it creates the service; on later
    runs it stops the service, republishes over the top, and starts it again.

    What it deliberately does NOT do:
      * create the database, login or user - see docs/FIRST_DEPLOYMENT.md
      * apply migrations - that is a release step of its own, run before this
      * touch appsettings.Production.json if one already exists

    Run from an elevated PowerShell. Creating a service requires it.

.PARAMETER Root
    Where the published application lives. Default C:\SivayaanHMS\api

.PARAMETER SkipPublish
    Reconfigure and restart the service without rebuilding.

.PARAMETER PostgresService
    The Windows service name of the PostgreSQL server on this machine, e.g.
    postgresql-x64-18. Found automatically when omitted. The API service is
    made dependent on it so it never loses the startup race after a reboot.
    Pass an empty string when the database is on another machine and there
    is no local service to wait for.

.EXAMPLE
    .\Deploy-Production.ps1
.EXAMPLE
    .\Deploy-Production.ps1 -PostgresService postgresql-x64-18
.EXAMPLE
    .\Deploy-Production.ps1 -Root D:\Apps\HMS -SkipPublish
#>

[CmdletBinding()]
param(
    [string] $Root = 'C:\SivayaanHMS\api',
    [string] $PostgresService = '(auto)',
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'PostgresSettings.ps1')

$ServiceName = 'SivayaanHMSApi'
$Port        = 6051
$Project     = Join-Path $PSScriptRoot '..\backend\src\SivayaanHMS.Api'
$Settings    = Join-Path $Root 'appsettings.Production.json'

# The template lives in the source tree; a copied release folder (see
# docs/RELEASE_POSTGRESQL.md) carries it beside this deploy folder instead.
$Template    = @(
    (Join-Path $Project 'appsettings.Production.json.template'),
    (Join-Path $PSScriptRoot '..\appsettings.Production.json.template')
) | Where-Object { Test-Path $_ } | Select-Object -First 1

function Say([string] $m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Warn([string] $m) { Write-Host "  ! $m" -ForegroundColor Yellow }
function Die([string] $m) { Write-Host "  x $m" -ForegroundColor Red; exit 1 }

# ---------------------------------------------------------------- prerequisites

Say 'Checking prerequisites'

$admin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
         ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { Die 'Run this from an elevated PowerShell - creating a service needs it.' }

# The PostgreSQL service, when the database is on this machine. Named
# explicitly, found automatically, or deliberately none at all (a database on
# another host) - three different answers, each checked rather than assumed.
$PgService = $null
if ($PostgresService -eq '(auto)') {
    $PgService = Find-PostgresService
    if (-not $PgService) {
        Die 'No PostgreSQL service found on this machine. Install PostgreSQL (docs/POSTGRESQL_SETUP.md), or pass -PostgresService '''' if the database is on another host.'
    }
} elseif ($PostgresService) {
    $PgService = Get-Service -Name $PostgresService -ErrorAction SilentlyContinue
    if (-not $PgService) {
        $found = Get-Service -Name 'postgresql*' -ErrorAction SilentlyContinue
        if ($found) {
            Warn 'PostgreSQL services present on this machine:'
            $found | ForEach-Object { Write-Host "      $($_.Name)  ($($_.Status))" }
        }
        Die "'$PostgresService' is not a service on this machine."
    }
}
if ($PgService) {
    if ($PgService.Status -ne 'Running') {
        Warn "$($PgService.Name) is $($PgService.Status). Starting it."
        Start-Service $PgService.Name
    }
    Write-Host "    PostgreSQL: $($PgService.Name) $((Get-Service $PgService.Name).Status)"
} else {
    Write-Host '    PostgreSQL: on another host (no local service dependency)'
}

if (-not $SkipPublish) {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) { Die 'dotnet SDK not found on PATH. Needed to publish; use -SkipPublish on a machine without it.' }
}

# The published build is framework-dependent, so the ASP.NET Core runtime has
# to exist on this machine. Without it the service installs happily and then
# fails to start with a message about a missing framework, which reads like an
# application fault rather than a missing prerequisite.
$runtimes = & dotnet --list-runtimes 2>$null
$aspnet = $runtimes | Where-Object { $_ -match '^Microsoft\.AspNetCore\.App 10\.' }
if (-not $aspnet) {
    Die 'ASP.NET Core 10 runtime not found. Install the ASP.NET Core Hosting Bundle (or the runtime) before deploying - dotnet --list-runtimes should show Microsoft.AspNetCore.App 10.x.'
}
Write-Host "    Runtime   : $(($aspnet | Select-Object -First 1))"

# ------------------------------------------------------------------ stop first

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') {
    # The running exe locks itself; publishing over it fails halfway and
    # leaves a folder that is neither the old build nor the new one.
    Say "Stopping $ServiceName"
    Stop-Service $ServiceName -Force
    (Get-Service $ServiceName).WaitForStatus('Stopped', '00:00:30')
}

# --------------------------------------------------------------------- publish

if (-not $SkipPublish) {
    Say "Publishing to $Root"
    New-Item -ItemType Directory -Force -Path $Root | Out-Null

    # Keep the filled-in settings across a republish. dotnet publish does not
    # delete it, but a wiped folder or a bad run would, and it holds the JWT
    # key that every live session is signed with.
    $backup = $null
    if (Test-Path $Settings) {
        $backup = Join-Path $env:TEMP "appsettings.Production.$(Get-Date -Format yyyyMMddHHmmss).json"
        Copy-Item $Settings $backup
        Write-Host "    Existing settings backed up to $backup"
    }

    # -r win-x64 rather than a portable publish. QuestPDF's Skia binaries ship
    # for every runtime identifier, so a portable build carries 109 MB of
    # Linux and macOS native libraries to a Windows clinic machine: 187 MB
    # against 55 MB for the same application. --self-contained false keeps it
    # framework-dependent, so the .NET runtime check above still applies.
    & dotnet publish $Project -c Release -r win-x64 --self-contained false -o $Root --nologo
    if ($LASTEXITCODE -ne 0) { Die 'dotnet publish failed.' }

    # RID-specific publishing flattens native libraries to the root instead of
    # runtimes/<rid>/native. If this one goes missing the API still starts and
    # every screen works - printing is the only thing that fails, and only when
    # somebody first tries it.
    if (-not (Test-Path (Join-Path $Root 'QuestPdfSkia.dll'))) {
        Die 'QuestPdfSkia.dll is missing from the published output. PDFs would fail at the first print.'
    }
    if (-not (Test-Path (Join-Path $Root 'LatoFont'))) {
        Warn 'LatoFont is missing from the published output; PDFs may fall back to a substitute typeface.'
    }

    if ($backup -and -not (Test-Path $Settings)) {
        Copy-Item $backup $Settings
        Warn 'Publish removed appsettings.Production.json; restored from backup.'
    }
}

# ------------------------------------------------------- secrets sanity checks

Say 'Checking what landed in the published folder'

# appsettings.Local.json is the developer's file and holds the database password and
# the platform-support credential. It is excluded from publish in the csproj,
# but a stale copy from an older deployment would still be read - and because
# Program.cs loads it last, it would override production's database settings.
$leaked = Join-Path $Root 'appsettings.Local.json'
if (Test-Path $leaked) {
    Die "appsettings.Local.json is in $Root. It carries development secrets AND would override the production database settings. Delete it and redeploy."
}

if (-not (Test-Path $Settings)) {
    if (-not $Template) { Die "No appsettings.Production.json in $Root and no template found to copy. Create it from appsettings.Production.json.template (see docs/RELEASE_POSTGRESQL.md)." }
    Copy-Item $Template $Settings
    Warn "No appsettings.Production.json existed; copied the template to $Settings"
    Die  'Fill in the REPLACE-ME values in that file, then run this again.'
}

$conf = Get-Content $Settings -Raw | ConvertFrom-Json

# Read-PostgresSettings refuses a missing section, a missing key or a
# REPLACE-ME password, which is the whole check. The host is compared with the
# service decision above: a local service dependency guarding a database that
# is actually elsewhere protects nothing, and a remote host with a local
# dependency waits on a service the app never talks to.
$db = Read-PostgresSettings -AppRoot $Root
$dbIsLocal = $db.Host -in @('localhost', '127.0.0.1', '::1', $env:COMPUTERNAME)
if ($dbIsLocal -and -not $PgService) { Warn "Database:Host is '$($db.Host)' but no local PostgreSQL service is being depended on. The API may lose the startup race after a reboot." }
if (-not $dbIsLocal -and $PgService) { Warn "Database:Host is '$($db.Host)' - not this machine - yet the service will depend on local $($PgService.Name). Pass -PostgresService '' if that is not wanted." }
Write-Host "    Database: $($db.Name) on $($db.Host):$($db.Port) as $($db.Username)"

if ($conf.Jwt.Key -like '*REPLACE*') { Die 'The JWT key in appsettings.Production.json is still a placeholder. The API refuses to start outside Development with it.' }
if ($conf.Jwt.Key.Length -lt 32)     { Die 'The JWT key is shorter than 32 characters.' }
if ($conf.PlatformAdmin -and $conf.PlatformAdmin.Password -like '*REPLACE*') {
    Warn 'PlatformAdmin password is still a placeholder - EnterpriseAdmin will not be able to sign in. Delete the block if that is intended.'
}
foreach ($o in $conf.Cors.AllowedOrigins) {
    if ($o.EndsWith('/')) { Warn "CORS origin '$o' has a trailing slash and will never match. Origins are scheme+host+port, nothing else." }
    if ($o -cne $o.ToLower()) { Warn "CORS origin '$o' has capitals. Origin matching is case-sensitive even though DNS is not." }
}
Write-Host "    Port    : $($conf.Kestrel.Endpoints.Http.Url)"
Write-Host "    Origins : $($conf.Cors.AllowedOrigins -join ', ')"

# --------------------------------------------------------------------- service

$exe = Join-Path $Root 'SivayaanHMS.Api.exe'
if (-not (Test-Path $exe)) { Die "$exe not found. Publish did not produce an executable." }

if (-not $existing) {
    Say "Creating service $ServiceName"
    & sc.exe create $ServiceName binPath= "`"$exe`"" start= auto DisplayName= "Sivayaan HMS API" | Out-Null
    if ($LASTEXITCODE -ne 0) { Die 'sc.exe create failed.' }
} else {
    Say "Service $ServiceName already exists; updating it"
    & sc.exe config $ServiceName binPath= "`"$exe`"" start= auto | Out-Null
}

# Without this the API starts first after a reboot, cannot reach a database
# that is still coming up, and stays down until somebody notices.
if ($PgService) {
    Say "Setting the dependency on $($PgService.Name)"
    & sc.exe config $ServiceName depend= $PgService.Name | Out-Null
    if ($LASTEXITCODE -ne 0) { Warn 'Could not set the service dependency; set it by hand or the API may lose the startup race after a reboot.' }
} else {
    # A remote database has no local service to wait for; clear any
    # dependency a previous deployment on this machine may have set.
    & sc.exe config $ServiceName depend= / | Out-Null
}

# Environment for the service. This is what gates the placeholder-key check
# and keeps the startup migration off - it must not say Development.
Say 'Setting ASPNETCORE_ENVIRONMENT=Production for the service'
$regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
Set-ItemProperty -Path $regPath -Name Environment -Value @('ASPNETCORE_ENVIRONMENT=Production') -Type MultiString

& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null

# ----------------------------------------------------------------------- start

Say "Starting $ServiceName"
Start-Service $ServiceName
(Get-Service $ServiceName).WaitForStatus('Running', '00:00:60')

Say "Health check on port $Port"
$ok = $false
foreach ($attempt in 1..10) {
    Start-Sleep -Seconds 2
    try {
        $r = Invoke-WebRequest "http://localhost:$Port/api/settings/general" -UseBasicParsing -TimeoutSec 5
        $code = $r.StatusCode
    } catch {
        if ($_.Exception.Response) { $code = [int] $_.Exception.Response.StatusCode } else { $code = 0 }
    }
    # 401 is the healthy answer: the API is listening and refusing an
    # unauthenticated call. 200 would mean anonymous access to clinic settings.
    if ($code -eq 401) { $ok = $true; break }
    if ($code -eq 200) { Warn 'Got 200 unauthenticated where 401 was expected - check the authorization policy.'; $ok = $true; break }
    Write-Host "    attempt $attempt : $code"
}

if (-not $ok) {
    Warn "No answer on port $Port. Recent service errors:"
    Get-EventLog -LogName Application -Newest 200 -ErrorAction SilentlyContinue |
        Where-Object { $_.Message -match 'SivayaanHMS' } |
        Select-Object -First 3 TimeGenerated, Message | Format-List
    Die 'The service is running but the API is not answering. See above.'
}

Write-Host ''
Say 'API is up'
Write-Host "    http://localhost:$Port  (401 unauthenticated, as expected)"
Write-Host ''
Write-Host 'Next: the tunnel. See docs/DEPLOY_CLOUDFLARE.md section 2.' -ForegroundColor Green
Write-Host 'Nothing here schedules a database backup. Migrate-Database.ps1 takes one before each release; a nightly one is still nobody''s job - see docs/POSTGRESQL_SETUP.md.' -ForegroundColor Yellow
