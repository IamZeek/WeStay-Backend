<#
.SYNOPSIS
    Starts every WeStay service and runs the end-to-end integration suite.

.DESCRIPTION
    WeStay.Tests drives the real HTTP surface through the gateway, so all six services
    plus the gateway must be listening first. This launches each in its own window
    (so you can read its logs), waits for every port to come up, then runs the suite.

    Leave the service windows open afterwards — they're your dev environment.

.EXAMPLE
    .\run-e2e.ps1                  # start everything, then test
.EXAMPLE
    .\run-e2e.ps1 -TestOnly        # services already running, just test
.EXAMPLE
    .\run-e2e.ps1 -NoTest          # start services, don't test
.EXAMPLE
    .\run-e2e.ps1 -Filter ListingCrudTests
#>

[CmdletBinding()]
param(
    [switch] $TestOnly,
    [switch] $NoTest,
    [string] $Filter,
    [int]    $StartupTimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
$RepoRoot = $PSScriptRoot
Set-Location $RepoRoot

# Ports come from WeStay.Tests/appsettings.json + CLAUDE.md. ReviewService isn't in the
# test config's BaseUrls but the suite reaches it through the gateway.
$Services = @(
    @{ Name = 'WeStay.AuthService';         Port = 7019 }
    @{ Name = 'WeStay.ListingService';      Port = 7002 }
    @{ Name = 'WeStay.BookingService';      Port = 7292 }
    @{ Name = 'WeStay.MessagingService';    Port = 7179 }
    @{ Name = 'WeStay.NotificationService'; Port = 7284 }
    @{ Name = 'WeStay.ReviewService';       Port = 7084 }
    @{ Name = 'WeStay.ApiGateway';          Port = 7087 }   # last: it proxies the others
)

function Write-Head { param($t) Write-Host "`n=== $t ===" -ForegroundColor Cyan }
function Write-Ok   { param($m) Write-Host "  OK  $m" -ForegroundColor Green }
function Write-Warn { param($m) Write-Host "  !   $m" -ForegroundColor Yellow }

function Test-Port {
    param([string]$TargetHost = '127.0.0.1', [int]$Port, [int]$TimeoutMs = 1000)
    $c = $null
    try {
        $c = [System.Net.Sockets.TcpClient]::new()
        $iar = $c.BeginConnect($TargetHost, $Port, $null, $null)
        if (-not $iar.AsyncWaitHandle.WaitOne($TimeoutMs)) { return $false }
        $c.EndConnect($iar); return $true
    } catch { return $false } finally { if ($c) { $c.Close() } }
}

if (-not $TestOnly) {
    Write-Head 'Starting services'

    foreach ($svc in $Services) {
        if (Test-Port -Port $svc.Port) {
            Write-Ok "$($svc.Name) already listening on $($svc.Port)"
            continue
        }

        # Own window per service so its logs stay readable and it can be stopped individually.
        $cmd = @"
`$host.UI.RawUI.WindowTitle = '$($svc.Name) :$($svc.Port)'
Set-Location '$RepoRoot'
`$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project $($svc.Name) --no-launch-profile --urls 'https://localhost:$($svc.Port)'
Write-Host ''
Write-Host '$($svc.Name) exited. Press any key to close.' -ForegroundColor Red
`$null = `$host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
"@
        Start-Process powershell -ArgumentList '-NoExit', '-Command', $cmd | Out-Null
        Write-Host "  ... launching $($svc.Name)" -ForegroundColor DarkGray
        Start-Sleep -Milliseconds 700   # stagger, so six builds don't thrash the disk at once
    }

    Write-Head 'Waiting for ports'

    $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
    $pending  = [System.Collections.ArrayList]@($Services)

    while ($pending.Count -gt 0 -and (Get-Date) -lt $deadline) {
        foreach ($svc in @($pending)) {
            if (Test-Port -Port $svc.Port) {
                Write-Ok "$($svc.Name) up on $($svc.Port)"
                $pending.Remove($svc)
            }
        }
        if ($pending.Count -gt 0) { Start-Sleep -Seconds 2 }
    }

    if ($pending.Count -gt 0) {
        Write-Warn 'These never came up:'
        $pending | ForEach-Object { Write-Host "      $($_.Name) (:$($_.Port))" -ForegroundColor Yellow }
        Write-Host @"

  Check that service's window for the error. The usual causes:
    - Missing user secret: each service needs Jwt:Key, and the data services need
      their connection string. A missing one throws at startup by design.
    - ServiceAuth:InternalApiKey not set. WeStay.Tests expects
      'westay-dev-internal-key-change-in-prod' across all six services.
    - Port already taken by an older run.
"@ -ForegroundColor Yellow
        throw 'Not all services started.'
    }
}

if ($NoTest) {
    Write-Head 'Services running'
    Write-Host '  Skipping tests (-NoTest).' -ForegroundColor Gray
    return
}

Write-Head 'Running end-to-end suite'
Write-Host '  WeStay.Tests runs serially — the booking-job tests trigger global sweeps.' -ForegroundColor DarkGray

$testArgs = @('test', 'WeStay.Tests', '--nologo', '-v', 'normal')
if ($Filter) { $testArgs += @('--filter', $Filter) }

& dotnet @testArgs
$testExit = $LASTEXITCODE

Write-Head 'Result'
if ($testExit -eq 0) {
    Write-Ok 'All tests passed against PostgreSQL.'
} else {
    Write-Warn "Tests failed (exit $testExit)."
    Write-Host @"

  If failures cluster around dates, the Npgsql timestamp shim is the first thing
  to check — AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)
  at the top of each Program.cs.

  If they cluster around 401/403, it's the shared Jwt:Key or ServiceAuth key
  differing between services rather than anything database-related.
"@ -ForegroundColor Yellow
}

Write-Host "`n  Service windows are still open — close them when you're done.`n" -ForegroundColor Gray
exit $testExit
