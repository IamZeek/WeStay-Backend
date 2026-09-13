<#
.SYNOPSIS
    Full end-to-end test of the WeStay backend against PostgreSQL, through the API gateway.

.DESCRIPTION
    Exercises every service the gateway routes to, and specifically probes what a
    SQL Server -> PostgreSQL migration puts at risk:

      * DateTime round-trip      (Npgsql timestamp shim)
      * decimal precision        (numeric(18,2))
      * enum round-trip          (stored as integers)
      * identity sequences       (setval after HasData seeding)
      * seed data                (amenities, booking statuses, fee config)
      * empty-string vs null     (nullable-reference implicit [Required])

    Phases: Auth -> Listings -> Search -> Bookings -> Fees -> Reviews -> Admin -> Security.

    Pure HTTP. No .NET SDK or project needed. Run it from anywhere.

    NOTE: this file is deliberately ASCII-only. Windows PowerShell 5.1 reads .ps1 as
    Windows-1252 unless the file has a UTF-8 BOM, so non-ASCII characters (em dashes,
    smart quotes) corrupt string literals and break parsing.

    Requires: ApiGateway 7087, AuthService 7019, ListingService 7002,
              BookingService 7292, ReviewService 7084.

.EXAMPLE
    .\test-postgres-e2e.ps1
.EXAMPLE
    .\test-postgres-e2e.ps1 -SkipAdmin
#>

[CmdletBinding()]
param(
    [string] $Gateway    = 'https://localhost:7087',
    [string] $AdminEmail = 'admin@westay.local',
    [string] $AdminPass  = 'Admin123!',
    [switch] $SkipAdmin
)

# ---------------------------------------------------------------- TLS
if ($PSVersionTable.PSVersion.Major -ge 6) {
    $script:SkipCert = @{ SkipCertificateCheck = $true }
} else {
    $script:SkipCert = @{}
    Add-Type @"
using System.Net;
using System.Security.Cryptography.X509Certificates;
public class WeStayCertPolicy : ICertificatePolicy {
    public bool CheckValidationResult(ServicePoint sp, X509Certificate c, WebRequest r, int p) { return true; }
}
"@ -ErrorAction SilentlyContinue
    [System.Net.ServicePointManager]::CertificatePolicy = New-Object WeStayCertPolicy
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
}

$script:Pass = 0; $script:Fail = 0; $script:Skip = 0
$script:Failures = @()

function Assert-That {
    param([string]$What, [bool]$Condition, [string]$Detail = '')
    if ($Condition) { Write-Host "  PASS  $What" -ForegroundColor Green; $script:Pass++ }
    else {
        Write-Host "  FAIL  $What" -ForegroundColor Red
        if ($Detail) { Write-Host "        $Detail" -ForegroundColor DarkYellow }
        $script:Fail++
        $script:Failures += "$What$(if($Detail){" :: $Detail"})"
    }
}

function Skip-That {
    param([string]$What, [string]$Why)
    Write-Host "  SKIP  $What" -ForegroundColor DarkGray
    if ($Why) { Write-Host "        $Why" -ForegroundColor DarkGray }
    $script:Skip++
}

function Write-Head { param($t) Write-Host "`n--- $t ---" -ForegroundColor Cyan }

function Api {
    param([string]$Method, [string]$Path, $Body, [string]$Token)
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    # UseBasicParsing stops PS 5.1 prompting "Script Execution Risk" on every call.
    $params = @{
        Uri = "$Gateway$Path"; Method = $Method; Headers = $headers
        ContentType = 'application/json'; ErrorAction = 'Stop'
        UseBasicParsing = $true
    } + $script:SkipCert
    if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 8 -Compress) }
    try {
        $resp = Invoke-WebRequest @params
        $parsed = $null
        if ($resp.Content) { try { $parsed = $resp.Content | ConvertFrom-Json } catch { $parsed = $resp.Content } }
        return @{ Status = [int]$resp.StatusCode; Body = $parsed }
    } catch {
        $status = 0; $content = $_.Exception.Message
        if ($_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
            try {
                if ($PSVersionTable.PSVersion.Major -ge 6) { $content = $_.ErrorDetails.Message }
                else {
                    $sr = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                    $content = $sr.ReadToEnd()
                }
            } catch { }
        }
        $parsed = $null
        if ($content) { try { $parsed = $content | ConvertFrom-Json } catch { $parsed = $content } }
        return @{ Status = $status; Body = $parsed }
    }
}

function Get-Field {
    param($Obj, [string[]]$Names)
    if ($null -eq $Obj) { return $null }
    foreach ($n in $Names) {
        foreach ($c in @($n, $n.Substring(0,1).ToLower()+$n.Substring(1), $n.Substring(0,1).ToUpper()+$n.Substring(1))) {
            if ($Obj.PSObject.Properties.Name -contains $c) { return $Obj.$c }
        }
    }
    return $null
}

function Short { param($o) if ($null -eq $o) { '' } else { ($o | ConvertTo-Json -Depth 4 -Compress) } }

Write-Host "`n===== WeStay full end-to-end test (PostgreSQL) =====" -ForegroundColor Cyan
Write-Host "Gateway: $Gateway`n" -ForegroundColor Gray

$stamp      = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$emailHost  = "e2e-host-$stamp@westay.local"
$emailGuest = "e2e-guest-$stamp@westay.local"
$pw         = 'Test123!'
$dob        = '1990-06-15'
$lid1 = $null; $lid2 = $null; $bid = $null; $bcode = $null

# ================================================================ 1. AUTH
Write-Head '1. Auth'

$r = Api POST '/api/auth/register' @{
    firstName='E2E'; lastName='Host'; email=$emailHost
    password=$pw; phoneNumber='+923001234567'; dateOfBirth=$dob }
Assert-That 'register host (with dateOfBirth)' ($r.Status -eq 200) "status $($r.Status): $(Short $r.Body)"
$tokHost = Get-Field $r.Body @('Token')
Assert-That 'register returned token' ($null -ne $tokHost)

$r = Api POST '/api/auth/become-host' $null $tokHost
Assert-That 'become-host' ($r.Status -eq 200) "status $($r.Status)"
$t = Get-Field $r.Body @('Token'); if ($t) { $tokHost = $t }
Assert-That 'role is Host' ((Get-Field $r.Body @('Role')) -eq 'Host') "role=$(Get-Field $r.Body @('Role'))"

$r = Api GET '/api/auth/profile' $null $tokHost
Assert-That 'GET profile' ($r.Status -eq 200) "status $($r.Status)"
$gotDob = Get-Field $r.Body @('DateOfBirth')
Assert-That 'dateOfBirth round-trips (Npgsql timestamp shim)' `
    ($null -ne $gotDob -and ([datetime]$gotDob).ToString('yyyy-MM-dd') -eq $dob) "sent $dob, got '$gotDob'"

$r = Api POST '/api/auth/login' @{ email=$emailHost; password=$pw }
Assert-That 'login' ($r.Status -eq 200) "status $($r.Status)"

$r = Api POST '/api/auth/register' @{
    firstName='E2E'; lastName='Guest'; email=$emailGuest
    password=$pw; phoneNumber='+923009876543'; dateOfBirth=$null }
Assert-That 'register guest (null dateOfBirth)' ($r.Status -eq 200) "status $($r.Status): $(Short $r.Body)"
$tokGuest = Get-Field $r.Body @('Token')

$tokAdmin = $null
if (-not $SkipAdmin) {
    $r = Api POST '/api/auth/login' @{ email=$AdminEmail; password=$AdminPass }
    if ($r.Status -eq 200) {
        $tokAdmin = Get-Field $r.Body @('Token')
        Assert-That 'admin login (seeded)' $true
    } else {
        Skip-That 'admin login' "status $($r.Status). AdminSeed may differ; pass -AdminEmail / -AdminPass"
    }
}

# ================================================================ 2. LISTINGS
Write-Head '2. Listings'

$price = 8000.55
$mk = @{
    title='E2E Villa'; description='Created by test-postgres-e2e.ps1. Safe to delete.'
    type=4; category=0; guests=4; bedrooms=2; beds=3; bathrooms=2
    pricePerNight=$price
    address='Street 1'; city='Islamabad'
    state=''; country='Pakistan'; zipCode=''
    amenityIds=@(); imageUrls=@() }

$r = Api POST '/api/listings' $mk $tokHost
Assert-That 'create listing (empty-string optionals)' ($r.Status -in 200,201) "status $($r.Status): $(Short $r.Body)"
$lid1 = Get-Field $r.Body @('Id')

$r = Api GET "/api/listings/$lid1" $null $tokHost
Assert-That 'GET listing by id' ($r.Status -eq 200) "status $($r.Status)"
Assert-That 'decimal precision preserved' ([decimal](Get-Field $r.Body @('PricePerNight')) -eq [decimal]$price) "got $(Get-Field $r.Body @('PricePerNight'))"
Assert-That 'enum Type round-trips (Villa=4)' ((Get-Field $r.Body @('Type')) -eq 4) "got $(Get-Field $r.Body @('Type'))"
Assert-That 'enum Category round-trips (ShortTerm=0)' ((Get-Field $r.Body @('Category')) -eq 0)
Assert-That 'new listing is Active (0)' ((Get-Field $r.Body @('Status')) -eq 0)

$mk2 = $mk.Clone(); $mk2.title = 'E2E Villa Two'
$r = Api POST '/api/listings' $mk2 $tokHost
$lid2 = Get-Field $r.Body @('Id')
Assert-That 'identity sequence increments' ($null -ne $lid2 -and [int]$lid2 -gt [int]$lid1) "id1=$lid1 id2=$lid2"

$r = Api GET '/api/listings' $null $tokHost
Assert-That 'host lists own listings' ($r.Status -eq 200) "status $($r.Status)"
Assert-That 'host sees exactly 2' (@($r.Body).Count -eq 2) "saw $(@($r.Body).Count)"

$r = Api GET '/api/listings' $null $tokGuest
Assert-That 'other user sees 0 (owner-scoped)' (@($r.Body).Count -eq 0) "saw $(@($r.Body).Count)"

$upd = @{ title='E2E Villa (edited)'; description='Edited by the e2e test.'
    category=0; guests=4; bedrooms=2; beds=3; bathrooms=2; pricePerNight=9999.99
    address='Street 2'; city='Rawalpindi'; state='Punjab'; country='Pakistan'; zipCode='46000'
    amenityIds=@(); imageUrls=@() }
$r = Api PUT "/api/listings/$lid1" $upd $tokHost
Assert-That 'update listing' ($r.Status -eq 200) "status $($r.Status): $(Short $r.Body)"
Assert-That 'updated price precision' ([decimal](Get-Field $r.Body @('PricePerNight')) -eq [decimal]9999.99)

$nul = $upd.Clone(); $nul.state = $null; $nul.zipCode = $null
$r = Api PUT "/api/listings/$lid1" $nul $tokHost
Assert-That 'null optionals rejected (implicit Required)' ($r.Status -eq 400) "status $($r.Status), expected 400"

$r = Api DELETE "/api/listings/$lid1" $null $tokHost
Assert-That 'DELETE (soft)' ($r.Status -eq 200) "status $($r.Status)"

# GetListingByIdAsync is Active-only, so a soft-deleted listing 404s rather than
# coming back with Status=1. Check the owner's own list instead, which is not filtered.
$r = Api GET '/api/listings' $null $tokHost
$deleted = @($r.Body) | Where-Object { (Get-Field $_ @('Id')) -eq $lid1 }
Assert-That 'listing is Inactive (1) after soft delete' `
    ($null -ne $deleted -and (Get-Field $deleted @('Status')) -eq 1) `
    "status=$(Get-Field $deleted @('Status'))"
$r = Api PATCH "/api/listings/$lid1/status" @{ status=0 } $tokHost
Assert-That 'PATCH reactivate' ($r.Status -eq 200) "status $($r.Status)"
$r = Api GET "/api/listings/$lid1" $null $tokHost
Assert-That 'status Active (0) again' ((Get-Field $r.Body @('Status')) -eq 0)

# ================================================================ 3. SEARCH
Write-Head '3. Search (public)'

$r = Api GET '/api/search?city=Islamabad&page=1&pageSize=10'
Assert-That 'search by city' ($r.Status -eq 200) "status $($r.Status): $(Short $r.Body)"

$r = Api GET '/api/search/featured'
Assert-That 'featured listings' ($r.Status -eq 200) "status $($r.Status)"

$r = Api GET '/api/search/stats'
Assert-That 'listing stats' ($r.Status -eq 200) "status $($r.Status)"

$r = Api GET '/api/search/popular-locations?count=5'
Assert-That 'popular locations' ($r.Status -eq 200) "status $($r.Status)"

$r = Api GET "/api/search/similar/$lid2"
Assert-That 'similar listings' ($r.Status -eq 200) "status $($r.Status)"

# ================================================================ 4. BOOKINGS
Write-Head '4. Bookings (cross-service)'

$checkIn  = (Get-Date).AddDays(30).ToString('yyyy-MM-dd')
$checkOut = (Get-Date).AddDays(33).ToString('yyyy-MM-dd')

$r = Api POST '/api/bookings/availability' @{
    listingId=[int]$lid2; checkInDate=$checkIn; checkOutDate=$checkOut }
Assert-That 'availability check (anonymous)' ($r.Status -eq 200) "status $($r.Status): $(Short $r.Body)"

$r = Api GET "/api/bookings/availability-calendar/$lid2`?startDate=$checkIn&endDate=$checkOut"
Assert-That 'availability calendar' ($r.Status -eq 200) "status $($r.Status)"

# Creating a booking makes BookingService call ListingService for /price, /capacity
# and /owner using the internal service key, so this covers cross-service auth too.
$r = Api POST '/api/bookings' @{
    listingId=[int]$lid2; checkInDate=$checkIn; checkOutDate=$checkOut; numberOfGuests=2
    specialRequests='E2E test booking.'
    guests=@(@{ firstName='E2E'; lastName='Guest'; email=$emailGuest
                phoneNumber='+923009876543'; dateOfBirth=$null })
} $tokGuest
Assert-That 'create booking (cross-service price/capacity/owner)' ($r.Status -in 200,201) "status $($r.Status): $(Short $r.Body)"

# The id may be on the response root or nested under a wrapper, depending on the DTO.
$bookingObj = $r.Body
if ($null -eq (Get-Field $bookingObj @('Id'))) {
    foreach ($wrap in @('Booking', 'Data', 'Result')) {
        $inner = Get-Field $r.Body @($wrap)
        if ($inner -and (Get-Field $inner @('Id'))) { $bookingObj = $inner; break }
    }
}
$bid   = Get-Field $bookingObj @('Id')
$bcode = Get-Field $bookingObj @('BookingCode')
if (-not $bid) {
    Write-Host "        create-booking response shape: $(Short $r.Body)" -ForegroundColor DarkYellow
}

if ($bid) {
    Assert-That 'booking has a code' ($null -ne $bcode) "code=$bcode"

    $r = Api GET "/api/bookings/$bid" $null $tokGuest
    Assert-That 'GET booking by id' ($r.Status -eq 200) "status $($r.Status)"
    $gotIn = Get-Field $r.Body @('CheckInDate')
    Assert-That 'booking dates round-trip' `
        ($null -ne $gotIn -and ([datetime]$gotIn).ToString('yyyy-MM-dd') -eq $checkIn) `
        "sent $checkIn, got $gotIn"

    if ($bcode) {
        $r = Api GET "/api/bookings/code/$bcode" $null $tokGuest
        Assert-That 'GET booking by code' ($r.Status -eq 200) "status $($r.Status)"
    }

    $r = Api POST "/api/bookings/$bid/confirm" $null $tokHost
    Assert-That 'host confirms booking (ownership via /owner)' ($r.Status -eq 200) "status $($r.Status): $(Short $r.Body)"

    $r = Api GET "/api/bookings/$bid" $null $tokGuest
    $st = Get-Field $r.Body @('Status')
    Assert-That 'booking status is Confirmed' ("$st" -match 'Confirm|2') "got '$st'"
} else {
    Skip-That 'booking detail and confirm assertions' 'booking was not created'
}

# ================================================================ 5. FEES
Write-Head '5. Platform fees (seed data)'

$r = Api GET '/api/fees'
Assert-That 'GET /api/fees (PlatformFeeConfig seed)' ($r.Status -eq 200) "status $($r.Status): $(Short $r.Body)"

# ================================================================ 6. REVIEWS
Write-Head '6. Reviews'

$r = Api GET "/api/reviews/listing/$lid2"
Assert-That 'listing reviews (anonymous)' ($r.Status -eq 200) "status $($r.Status)"

$r = Api GET "/api/reviews/listing/$lid2/summary"
Assert-That 'listing review summary' ($r.Status -eq 200) "status $($r.Status)"

Skip-That 'create review' 'requires a Completed booking, covered by WeStay.Tests ReviewTests'

# ================================================================ 7. ADMIN
Write-Head '7. Admin'

if ($tokAdmin) {
    $r = Api GET '/api/listings/admin/all?page=1&pageSize=5' $null $tokAdmin
    Assert-That 'admin: all listings' ($r.Status -eq 200) "status $($r.Status): $(Short $r.Body)"

    $r = Api GET '/api/bookings/admin/all?page=1&pageSize=5' $null $tokAdmin
    Assert-That 'admin: all bookings' ($r.Status -eq 200) "status $($r.Status)"
} else {
    Skip-That 'admin endpoints' 'no admin token'
}

# ================================================================ 8. SECURITY
Write-Head '8. Gateway security'

$r = Api GET '/api/listings'
Assert-That 'listings without token rejected' ($r.Status -in 401,403) "status $($r.Status), expected 401/403"

$r = Api POST '/api/listings' $mk
Assert-That 'create without token rejected' ($r.Status -in 401,403) "status $($r.Status), expected 401/403"

$r = Api GET '/api/auth/users/1/contact' $null $tokHost
Assert-That 'admin-gated /api/auth/users/* blocked for host' ($r.Status -in 401,403) "status $($r.Status), expected 401/403"

$r = Api GET '/api/notifications' $null $tokHost
Assert-That 'internal /api/notifications not routed at gateway' ($r.Status -in 404,401,403) "status $($r.Status)"

# ================================================================ summary
Write-Host "`n===== Summary =====" -ForegroundColor Cyan
Write-Host "  Passed:  $script:Pass" -ForegroundColor Green
Write-Host "  Failed:  $script:Fail" -ForegroundColor $(if ($script:Fail) { 'Red' } else { 'Green' })
Write-Host "  Skipped: $script:Skip" -ForegroundColor DarkGray

if ($script:Fail -gt 0) {
    Write-Host "`n  Failures:" -ForegroundColor Red
    $script:Failures | ForEach-Object { Write-Host "    - $_" -ForegroundColor Red }
    Write-Host ''
    Write-Host '  Reading the failures:' -ForegroundColor Yellow
    Write-Host '    dates            : Npgsql timestamp shim (AppContext.SetSwitch in Program.cs)' -ForegroundColor Yellow
    Write-Host '    401 / 403        : shared Jwt Key differing between services' -ForegroundColor Yellow
    Write-Host '    booking creation : ServiceAuth InternalApiKey mismatch (Booking vs Listing)' -ForegroundColor Yellow
    Write-Host '    connection error : that service is not running' -ForegroundColor Yellow
}

Write-Host ''
Write-Host '  Test data created (delete when convenient):' -ForegroundColor Gray
Write-Host "    users:    $emailHost , $emailGuest" -ForegroundColor Gray
Write-Host "    listings: $lid1 , $lid2" -ForegroundColor Gray
Write-Host "    booking:  $bid $bcode" -ForegroundColor Gray
Write-Host ''

exit $(if ($script:Fail -gt 0) { 1 } else { 0 })
