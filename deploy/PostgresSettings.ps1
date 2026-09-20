<#
    Shared by the database scripts: how to read the Database section out of the application's
    own settings file, and where the PostgreSQL command-line tools are.

    Dot-source it:   . (Join-Path $PSScriptRoot 'PostgresSettings.ps1')

    The database is read from appsettings.Production.json rather than passed in, so a backup, a
    migration, a restore and the application itself all agree on which database they mean. There
    is exactly one place to change it - the same "one place, every knob" rule the Database section
    itself follows (see backend/src/SivayaanHMS.Data/DatabaseOptions.cs).
#>

function Read-PostgresSettings {
    param([Parameter(Mandatory)] [string] $AppRoot)

    $file = Join-Path $AppRoot 'appsettings.Production.json'
    if (-not (Test-Path $file)) {
        throw "$file not found. Pass -Root <the folder holding the published API>."
    }

    $settings = Get-Content $file -Raw | ConvertFrom-Json
    $section = $settings.Database
    if (-not $section) { throw "$file has no Database section." }

    # A complete connection string, when given, wins over the individual keys - exactly as
    # DatabaseOptions.BuildConnectionString does in the application. Picked apart here only
    # because pg_dump and psql take the parts, not the string.
    if ($section.ConnectionString) {
        $parts = @{}
        foreach ($pair in ($section.ConnectionString -split ';')) {
            if ($pair -notmatch '=') { continue }
            $k, $v = $pair -split '=', 2
            $parts[$k.Trim().ToLowerInvariant()] = $v.Trim()
        }
        $pick = { param($names) foreach ($n in $names) { if ($parts.ContainsKey($n)) { return $parts[$n] } } return $null }
        $result = [pscustomobject]@{
            File             = $file
            Host             = & $pick @('host', 'server')
            Port             = & $pick @('port')
            Name             = & $pick @('database', 'db')
            Username         = & $pick @('username', 'user id', 'userid', 'user')
            Password         = & $pick @('password', 'pwd')
            ConnectionString = $section.ConnectionString
        }
    } else {
        $result = [pscustomobject]@{
            File             = $file
            Host             = $section.Host
            Port             = $section.Port
            Name             = $section.Name
            Username         = $section.Username
            Password         = $section.Password
            ConnectionString = $null
        }
        # The same keys the application hands Npgsql, for the one consumer here that wants a
        # string: the EF Core design-time factory behind `dotnet ef`.
        $ssl = if ($section.SslMode) { $section.SslMode } else { 'Prefer' }
        $result.ConnectionString =
            "Host=$($result.Host);Port=$($result.Port);Database=$($result.Name);Username=$($result.Username);" +
            "Password=$($result.Password);SSL Mode=$ssl;Application Name=SivayaanHMS-Migrate"
    }

    if (-not $result.Port) { $result.Port = '5432' }
    foreach ($required in 'Host', 'Name', 'Username', 'Password') {
        if (-not $result.$required) { throw "The Database section in $file has no $required." }
    }
    if ("$($result.Password)" -like '*REPLACE*') {
        throw "Database:Password in $file is still the placeholder."
    }
    return $result
}

function Find-PgTool {
    param([Parameter(Mandatory)] [string] $Name, [string] $PgBin = '')

    if ($PgBin) {
        $exe = Join-Path $PgBin "$Name.exe"
        if (Test-Path $exe) { return $exe }
        throw "$exe not found."
    }

    $onPath = Get-Command $Name -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    # The installer does not put the bin folder on PATH. Newest installed major version wins.
    $root = 'C:\Program Files\PostgreSQL'
    if (Test-Path $root) {
        $found = Get-ChildItem $root -Directory |
            Where-Object { $_.Name -match '^\d+$' } |
            Sort-Object { [int]$_.Name } -Descending |
            ForEach-Object { Join-Path $_.FullName "bin\$Name.exe" } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1
        if ($found) { return $found }
    }

    throw "$Name.exe not found on PATH or under $root. Install the PostgreSQL client tools, or pass -PgBin <folder>."
}

function Find-PostgresService {
    # The installer names the service after the major version - postgresql-x64-18. One instance
    # per machine is the normal case; the newest wins if there are more.
    $services = Get-Service -Name 'postgresql*' -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending
    if ($services) { return ($services | Select-Object -First 1) }
    return $null
}
