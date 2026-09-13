<#
.SYNOPSIS
    Completes the SQL Server -> PostgreSQL migration for WeStay.

.DESCRIPTION
    Runs every step from POSTGRES-MIGRATION.md in order, stopping at the first failure
    so you never end up half-migrated:

      1. Verify prerequisites and reach Postgres
      2. Create the six databases (safe to re-run)
      3. Delete the old SQL Server migrations
      4. Restore + build
      5. Set the six connection strings in User Secrets
      6. Regenerate migrations and create the schemas
      7. Verify seed data landed

    Works with Postgres running natively on Windows OR in Docker — it auto-detects.
    Docker is NOT required: steps 2 and 7 are conveniences, and step 6 connects over
    TCP through Npgsql regardless.

    The code changes (Npgsql packages, UseNpgsql, timestamp shim) are already committed —
    this script only does the parts that need to run on your machine.

.EXAMPLE
    .\migrate-to-postgres.ps1

.EXAMPLE
    # Postgres in Docker under a different container name
    .\migrate-to-postgres.ps1 -PgMode docker -PgContainer my-postgres

.EXAMPLE
    # Databases already created by hand
    .\migrate-to-postgres.ps1 -SkipDbCreate
#>

[CmdletBinding()]
param(
    [ValidateSet('auto', 'native', 'docker')]
    [string] $PgMode      = 'auto',
    [string] $PgContainer = 'westay-postgres',
    [string] $PgUser      = 'westay',
    # 127.0.0.1 rather than 'localhost' on purpose: localhost resolves to ::1 (IPv6) first on
    # Windows, and a Postgres listening only on IPv4 makes that connection hang instead of
    # failing fast. This applies to the generated connection strings too.
    [string] $PgHost      = '127.0.0.1',
    [int]    $PgPort      = 5432,
    [securestring] $DbPassword,
    [switch] $SkipDbCreate,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

$Services = @(
    @{ Project = 'WeStay.AuthService';         Key = 'ConnectionStrings:DefaultConnection';      Db = 'westay_auth' }
    @{ Project = 'WeStay.ListingService';      Key = 'ConnectionStrings:ListingDbConnection';    Db = 'westay_listing' }
    @{ Project = 'WeStay.BookingService';      Key = 'ConnectionStrings:BookingConnection';      Db = 'westay_booking' }
    @{ Project = 'WeStay.MessagingService';    Key = 'ConnectionStrings:MessagingConnection';    Db = 'westay_messaging' }
    @{ Project = 'WeStay.NotificationService'; Key = 'ConnectionStrings:NotificationConnection'; Db = 'westay_notification' }
    @{ Project = 'WeStay.ReviewService';       Key = 'ConnectionStrings:ReviewConnection';       Db = 'westay_review' }
)

function Write-Step { param($n, $t) Write-Host "`n=== [$n/7] $t ===" -ForegroundColor Cyan }
function Write-Ok   { param($m)     Write-Host "  OK  $m" -ForegroundColor Green }
function Write-Warn { param($m)     Write-Host "  !   $m" -ForegroundColor Yellow }

function Invoke-Checked {
    # NOTE: the parameter is deliberately NOT called $Args — that's a PowerShell automatic
    # variable, and declaring it shadows the real arguments so the splat expands to nothing.
    param([string]$Exe, [string[]]$CliArgs, [string]$What)
    & $Exe @CliArgs
    if ($LASTEXITCODE -ne 0) {
        throw "$What failed (exit $LASTEXITCODE). Nothing after this point has run."
    }
}

function Test-Port {
    <#
    Fast TCP reachability check. Test-NetConnection is used instead of this in a lot of
    scripts, but it tries IPv6 first and can sit there for ~20s when nothing answers on ::1.
    #>
    param([string]$TargetHost, [int]$Port, [int]$TimeoutMs = 3000)
    $client = $null
    try {
        $client = [System.Net.Sockets.TcpClient]::new()
        $iar = $client.BeginConnect($TargetHost, $Port, $null, $null)
        if (-not $iar.AsyncWaitHandle.WaitOne($TimeoutMs)) { return $false }
        $client.EndConnect($iar)
        return $true
    } catch {
        return $false
    } finally {
        if ($client) { $client.Close() }
    }
}

$RepoRoot = $PSScriptRoot
Set-Location $RepoRoot
if (-not (Test-Path (Join-Path $RepoRoot 'WeStay.sln'))) {
    throw "WeStay.sln not found in $RepoRoot. Put this script in the repo root."
}

# ---------------------------------------------------------------- 1. prerequisites
Write-Step 1 'Checking prerequisites'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "'dotnet' is not on PATH. Install the .NET 8 SDK and re-run."
}
Write-Ok 'dotnet found'

& dotnet ef --version *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Warn 'dotnet-ef not found — installing it globally'
    Invoke-Checked 'dotnet' @('tool', 'install', '--global', 'dotnet-ef') 'dotnet tool install dotnet-ef'
} else {
    Write-Ok 'dotnet-ef found'
}

# --- Is Postgres actually reachable? This is the part that matters. ---
if (-not (Test-Port -TargetHost $PgHost -Port $PgPort)) {
    throw @"
Nothing is listening on ${PgHost}:${PgPort}.

Start Postgres first, then re-run. Either:
  - Native Windows service:  Get-Service postgresql*  |  Start-Service <name>
  - Docker Desktop:          start Docker Desktop, then
                             docker start $PgContainer

If Postgres IS running, it may be bound only to IPv6 or to a different port. Check with:
  Get-NetTCPConnection -LocalPort $PgPort -State Listen
"@
}
Write-Ok "Postgres reachable on ${PgHost}:${PgPort}"

# --- How do we run psql? Native beats Docker; neither is fatal. ---
$psqlMode = 'none'
$script:PsqlExe = 'psql'

if ($PgMode -in 'auto', 'native') {
    $found = Get-Command psql -ErrorAction SilentlyContinue
    if ($found) {
        $psqlMode = 'native'
        $script:PsqlExe = $found.Source
        Write-Ok 'psql found on PATH'
    }
    else {
        # A native Windows install almost always lands here, just not on PATH.
        $candidate = Get-ChildItem 'C:\Program Files\PostgreSQL\*\bin\psql.exe',
                                   'C:\Program Files (x86)\PostgreSQL\*\bin\psql.exe' `
                                   -ErrorAction SilentlyContinue |
                     Sort-Object FullName -Descending |
                     Select-Object -First 1
        if ($candidate) {
            $psqlMode = 'native'
            $script:PsqlExe = $candidate.FullName
            Write-Ok "psql found at $($candidate.FullName)"
        }
    }
}

if ($psqlMode -eq 'none' -and $PgMode -in 'auto', 'docker') {
    # docker.exe writes to stderr when the daemon is down, which $ErrorActionPreference='Stop'
    # turns into a terminating error. Probe it defensively — a missing daemon is not fatal here.
    if (Get-Command docker -ErrorAction SilentlyContinue) {
        $names = ''
        try {
            $old = $ErrorActionPreference
            $ErrorActionPreference = 'SilentlyContinue'
            $names = (& docker ps --filter "name=$PgContainer" --format '{{.Names}}' 2>$null) -join ''
        } catch {
            $names = ''
        } finally {
            $ErrorActionPreference = $old
        }
        if ($names -match [regex]::Escape($PgContainer)) {
            $psqlMode = 'docker'
            Write-Ok "Postgres container '$PgContainer' is running"
        } else {
            Write-Warn 'Docker not usable (daemon down or container not running) — ignoring'
        }
    }
}

if ($psqlMode -eq 'none') {
    Write-Warn 'No psql available (not on PATH, and no running Docker container found).'
    Write-Warn 'Database creation and seed verification will be skipped.'
    Write-Warn 'Postgres IS reachable, so migrations will still work — but the six'
    Write-Warn 'databases must already exist or step 6 will fail.'
}

if (-not $DbPassword) {
    $DbPassword = Read-Host -AsSecureString "Postgres password for user '$PgUser'"
}
$PlainPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($DbPassword))
if ([string]::IsNullOrWhiteSpace($PlainPassword)) { throw 'Password cannot be empty.' }

function Invoke-Psql {
    <#
    Runs one SQL statement. Returns combined stdout+stderr; sets $script:PsqlExit.

    psql reports things like "database already exists" on stderr, and PowerShell escalates
    native-command stderr into a terminating error when $ErrorActionPreference is 'Stop'.
    That would kill the script before the caller can decide whether the message is benign,
    so the preference is relaxed for the duration of the call and the exit code is used as
    the real signal instead.
    #>
    param([string]$Database, [string]$Sql, [switch]$Quiet)

    $extra = if ($Quiet) { @('-t', '-A') } else { @() }
    $out = ''
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'

    try {
        switch ($psqlMode) {
            'native' {
                $prevPw = $env:PGPASSWORD
                $env:PGPASSWORD = $PlainPassword
                try {
                    $out = & $script:PsqlExe -h $PgHost -p $PgPort -U $PgUser -d $Database @extra -c $Sql 2>&1
                } finally {
                    $env:PGPASSWORD = $prevPw
                }
            }
            'docker' {
                $out = & docker exec -e "PGPASSWORD=$PlainPassword" $PgContainer `
                           psql -U $PgUser -d $Database @extra -c $Sql 2>&1
            }
            default {
                $script:PsqlExit = -1
                return 'psql unavailable'
            }
        }
        $script:PsqlExit = $LASTEXITCODE
    } catch {
        $script:PsqlExit = -1
        $out = $_.Exception.Message
    } finally {
        $ErrorActionPreference = $prevEap
    }

    return (($out | ForEach-Object { $_.ToString() }) -join "`n")
}

# ---------------------------------------------------------------- 2. databases
Write-Step 2 'Creating databases'

if ($SkipDbCreate) {
    Write-Warn 'Skipped (-SkipDbCreate)'
} elseif ($psqlMode -eq 'none') {
    Write-Warn 'Skipped (no psql). Create these manually if they do not exist:'
    $Services | ForEach-Object { Write-Host "    CREATE DATABASE $($_.Db);" }
} else {
    foreach ($svc in $Services) {
        $db = $svc.Db
        # CREATE DATABASE has no IF NOT EXISTS in Postgres, so tolerate "already exists".
        $out = Invoke-Psql -Database 'postgres' -Sql "CREATE DATABASE $db;"
        if     ($script:PsqlExit -eq 0)     { Write-Ok "created $db" }
        elseif ($out -match 'already exists') { Write-Ok "$db already exists" }
        else   { throw "Could not create ${db}: $out" }
    }
}

# ---------------------------------------------------------------- 3. old migrations
Write-Step 3 'Removing SQL Server migrations'

$MigrationDirs = $Services |
    ForEach-Object { Join-Path $RepoRoot "$($_.Project)\Migrations" } |
    Where-Object   { Test-Path $_ }

if (-not $MigrationDirs) {
    Write-Ok 'Already removed'
} else {
    Write-Host '  About to permanently delete:' -ForegroundColor Yellow
    $MigrationDirs | ForEach-Object { Write-Host "    $_" }
    Write-Host '  These contain SQL Server column types and cannot be reused.' -ForegroundColor Yellow

    if (-not $Force) {
        # Case-insensitive and whitespace-tolerant: typing the word is the confirmation,
        # matching its exact case adds nothing.
        $answer = Read-Host '  Type DELETE to continue'
        if ($answer.Trim() -ne 'DELETE') { throw 'Cancelled. Nothing was changed.' }
    }

    $MigrationDirs | Remove-Item -Recurse -Force
    Write-Ok "Removed $($MigrationDirs.Count) Migrations folder(s)"
}

# ---------------------------------------------------------------- 4. build
Write-Step 4 'Restoring and building'

Invoke-Checked 'dotnet' @('restore', 'WeStay.sln') 'dotnet restore'
Write-Ok 'restore'
Invoke-Checked 'dotnet' @('build', 'WeStay.sln', '--nologo', '-v', 'minimal') 'dotnet build'
Write-Ok 'build'

# ---------------------------------------------------------------- 5. connection strings
Write-Step 5 'Setting connection strings'

foreach ($svc in $Services) {
    $conn = "Host=$PgHost;Port=$PgPort;Database=$($svc.Db);Username=$PgUser;Password=$PlainPassword"
    Invoke-Checked 'dotnet' @(
        'user-secrets', 'set', $svc.Key, $conn, '--project', $svc.Project
    ) "user-secrets set for $($svc.Project)"
    Write-Ok "$($svc.Project) -> $($svc.Db)"
}

# ---------------------------------------------------------------- 6. migrations
Write-Step 6 'Generating migrations and schemas'

foreach ($svc in $Services) {
    Write-Host "  $($svc.Project)..." -ForegroundColor DarkGray
    Invoke-Checked 'dotnet' @(
        'ef', 'migrations', 'add', 'InitialPostgres', '--project', $svc.Project
    ) "migrations add for $($svc.Project)"
    Invoke-Checked 'dotnet' @(
        'ef', 'database', 'update', '--project', $svc.Project
    ) "database update for $($svc.Project)"
    Write-Ok $svc.Project
}

# ---------------------------------------------------------------- 7. verify
Write-Step 7 'Verifying seed data'

if ($psqlMode -eq 'none') {
    Write-Warn 'Skipped (no psql). Step 6 succeeding is itself good evidence.'
} else {
    # Table counts first — pg_tables needs no quoted identifiers, so it can't be tripped
    # up by PowerShell's handling of embedded double quotes.
    foreach ($svc in $Services) {
        $n = Invoke-Psql -Database $svc.Db -Quiet `
                 -Sql "SELECT COUNT(*) FROM pg_tables WHERE schemaname = 'public';"
        if ($script:PsqlExit -ne 0) {
            Write-Warn "$($svc.Db): could not read ($n)"
        } elseif ([int]$n.Trim() -le 1) {
            # 1 would be just __EFMigrationsHistory — schema never actually applied.
            Write-Warn "$($svc.Db): only $($n.Trim()) table(s) — schema looks EMPTY"
        } else {
            Write-Ok "$($svc.Db): $($n.Trim()) tables"
        }
    }

    # Seed rows. The \" escaping is what survives PowerShell -> native psql; plain
    # double quotes get stripped and Postgres then lowercases the identifier.
    $checks = @(
        @{ Db = 'westay_listing';      Table = 'Amenities' }
        @{ Db = 'westay_booking';      Table = 'BookingStatuses' }
        @{ Db = 'westay_messaging';    Table = 'ConversationTypes' }
        @{ Db = 'westay_notification'; Table = 'NotificationTypes' }
    )
    foreach ($c in $checks) {
        $count = Invoke-Psql -Database $c.Db -Quiet `
                     -Sql "SELECT COUNT(*) FROM \`"$($c.Table)\`";"
        if ($script:PsqlExit -eq 0) { Write-Ok "$($c.Table): $($count.Trim()) row(s)" }
        else                        { Write-Warn "$($c.Table): could not read ($count)" }
    }
}

# ---------------------------------------------------------------- done
Write-Host "`n=== Migration complete ===" -ForegroundColor Green
Write-Host @"

WeStay is now running on PostgreSQL.

Next:
  1. Start AuthService (7019), ListingService (7002) and ApiGateway (7087)
  2. Re-run the browser smoke test at http://localhost:3000

Watch registration first — DateOfBirth is exactly the field the Npgsql timestamp
shim protects. If it fails with "Cannot write DateTime with Kind=Unspecified",
the AppContext.SetSwitch line in AuthService/Program.cs isn't taking effect.

Note psql needs double-quoted table names:
  SELECT * FROM "Listings";     works
  SELECT * FROM Listings;       relation does not exist
"@ -ForegroundColor Gray
