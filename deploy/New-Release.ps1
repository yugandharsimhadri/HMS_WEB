<#
.SYNOPSIS
    Builds everything a server needs into one folder that can be copied:
    the published API, the frontend, the schema bundle, the deploy scripts
    and the settings template. docs/RELEASE_POSTGRESQL.md is the guide that
    consumes it.

.DESCRIPTION
    Run on a development machine with the .NET 10 SDK, dotnet-ef and Node.
    Output goes to backend\artifacts\release (git-ignored), and a zip beside
    it named with the date and the short commit hash.

.PARAMETER Out
    Where to assemble the folder. Default backend\artifacts\release.

.PARAMETER NoZip
    Leave the folder unzipped.

.EXAMPLE
    .\deploy\New-Release.ps1
#>

[CmdletBinding()]
param(
    [string] $Out,
    [switch] $NoZip
)

$ErrorActionPreference = 'Stop'

function Say([string] $m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Die([string] $m) { Write-Host "  x $m" -ForegroundColor Red; exit 1 }

$Repo     = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ApiProj  = Join-Path $Repo 'backend\src\SivayaanHMS.Api'
$DataProj = Join-Path $Repo 'backend\src\SivayaanHMS.Data'
$Frontend = Join-Path $Repo 'frontend'
if (-not $Out) { $Out = Join-Path $Repo 'backend\artifacts\release' }

# MSBuild node reuse has hung publishes on a busy machine before; one-shot
# nodes cost a little startup and cannot be left in a bad state by whatever
# else touched the tree recently.
$env:MSBUILDDISABLENODEREUSE = '1'

if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force $Out | Out-Null

# ---------------------------------------------------------------------- API

Say 'Publishing the API (Release, win-x64, framework-dependent)'
& dotnet publish $ApiProj -c Release -r win-x64 --self-contained false -o (Join-Path $Out 'api') --nologo -v quiet
if ($LASTEXITCODE -ne 0) { Die 'dotnet publish failed.' }

# The one guarantee that matters most: the developer's secrets file must not
# be in what gets copied to a server.
if (Test-Path (Join-Path $Out 'api\appsettings.Local.json')) {
    Die 'appsettings.Local.json landed in the publish output. SivayaanHMS.Api.csproj''s CopyToPublishDirectory="Never" rule has regressed.'
}
if (-not (Test-Path (Join-Path $Out 'api\QuestPdfSkia.dll'))) { Die 'QuestPdfSkia.dll missing from the publish - PDFs would fail.' }

# ------------------------------------------------------------------- schema

Say 'Building the migration bundle (self-contained migrate.exe)'
& dotnet ef migrations bundle --self-contained -r win-x64 --project $DataProj --startup-project $DataProj -o (Join-Path $Out 'migrate.exe') --force
if ($LASTEXITCODE -ne 0) { Die 'dotnet ef migrations bundle failed. Is dotnet-ef installed? dotnet tool install --global dotnet-ef' }

# ----------------------------------------------------------------- frontend

Say 'Building the frontend'
Push-Location $Frontend
try {
    if (-not (Test-Path 'node_modules')) { & npm ci; if ($LASTEXITCODE -ne 0) { Die 'npm ci failed.' } }
    & npm run build
    if ($LASTEXITCODE -ne 0) { Die 'npm run build failed.' }
} finally { Pop-Location }
Copy-Item (Join-Path $Frontend 'dist') (Join-Path $Out 'frontend') -Recurse

# ------------------------------------------------------ scripts and the rest

Say 'Copying deploy scripts, SQL, template and the guide'
New-Item -ItemType Directory -Force (Join-Path $Out 'deploy') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'Deploy-Production.ps1'), (Join-Path $PSScriptRoot 'Migrate-Database.ps1'), (Join-Path $PSScriptRoot 'PostgresSettings.ps1') (Join-Path $Out 'deploy')
Copy-Item (Join-Path $PSScriptRoot 'sql') (Join-Path $Out 'sql') -Recurse
Copy-Item (Join-Path $PSScriptRoot 'cloudflared') (Join-Path $Out 'cloudflared') -Recurse
Copy-Item (Join-Path $ApiProj 'appsettings.Production.json.template') $Out
Copy-Item (Join-Path $Repo 'docs\RELEASE_POSTGRESQL.md') $Out

$commit = (& git -C $Repo rev-parse --short HEAD 2>$null)
$stamp  = "$(Get-Date -Format yyyyMMdd)-$commit"
Set-Content (Join-Path $Out 'VERSION.txt') "SivayaanHMS release $stamp`nBuilt $(Get-Date -Format 'yyyy-MM-dd HH:mm') on $env:COMPUTERNAME`n"

# ---------------------------------------------------------------------- zip

if (-not $NoZip) {
    $zip = Join-Path (Split-Path $Out) "SivayaanHMS-release-$stamp.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Say "Zipping to $zip"
    Compress-Archive -Path (Join-Path $Out '*') -DestinationPath $zip
    Write-Host "    $([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB"
}

Write-Host ''
Say "Release folder: $Out"
Write-Host 'Next: docs/RELEASE_POSTGRESQL.md, on the server.' -ForegroundColor Green
