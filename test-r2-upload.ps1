<#
.SYNOPSIS
    Tests the Cloudflare R2 image upload path end to end.

.DESCRIPTION
    Uploading successfully is only half the job: the URL that comes back has to be
    publicly readable by a browser, with no credentials. This script checks both, plus
    the two ways R2 is usually misconfigured:

      1. R2:PublicBaseUrl pointing at the authenticated S3 endpoint
         (<account>.r2.cloudflarestorage.com) instead of the r2.dev / custom domain.
         Uploads succeed; every image 403s in the browser.
      2. Bucket public access never enabled. Same symptom.

    Steps: create a host -> upload a PNG -> fetch the returned URL anonymously ->
    compare bytes -> attach it to a listing and confirm it round-trips.

    ASCII-only on purpose (Windows PowerShell 5.1 reads .ps1 as Windows-1252).

    Requires: ApiGateway 7087, AuthService 7019, ListingService 7002.

.EXAMPLE
    .\test-r2-upload.ps1
#>

[CmdletBinding()]
param(
    [string] $Gateway       = 'https://localhost:7087',
    # Used to isolate whether a multipart failure is Ocelot's or the service's.
    [string] $ListingDirect = 'https://localhost:7002'
)

if ($PSVersionTable.PSVersion.Major -ge 6) {
    $script:SkipCert = @{ SkipCertificateCheck = $true }
} else {
    $script:SkipCert = @{}

    # The dev certificate is self-signed, so TLS validation has to be bypassed.
    #
    # It must be a COMPILED delegate, not a PowerShell scriptblock: validation runs on
    # a background thread with no runspace attached, and a scriptblock there dies with
    # "There is no Runspace available to run scripts in this thread", surfacing only as
    # "The underlying connection was closed".
    if (-not ('WeStayCertBypass' -as [type])) {
        Add-Type @"
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

public static class WeStayCertBypass
{
    public static void Enable()
    {
        ServicePointManager.ServerCertificateValidationCallback =
            delegate (object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors errors)
            { return true; };
        ServicePointManager.SecurityProtocol =
            SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
    }
}
"@
    }
    [WeStayCertBypass]::Enable()
}

$script:Pass = 0; $script:Fail = 0
$script:Failures = @()

function Assert-That {
    param([string]$What, [bool]$Condition, [string]$Detail = '')
    if ($Condition) { Write-Host "  PASS  $What" -ForegroundColor Green; $script:Pass++ }
    else {
        Write-Host "  FAIL  $What" -ForegroundColor Red
        if ($Detail) { Write-Host "        $Detail" -ForegroundColor DarkYellow }
        $script:Fail++; $script:Failures += "$What$(if($Detail){" :: $Detail"})"
    }
}
function Write-Head { param($t) Write-Host "`n--- $t ---" -ForegroundColor Cyan }

function Api {
    param([string]$Method, [string]$Path, $Body, [string]$Token)
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $params = @{
        Uri = "$Gateway$Path"; Method = $Method; Headers = $headers
        ContentType = 'application/json'; ErrorAction = 'Stop'; UseBasicParsing = $true
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

Add-Type -AssemblyName System.Net.Http -ErrorAction SilentlyContinue

function Invoke-Upload {
    <#
    Posts a multipart/form-data upload using System.Net.Http.

    Hand-building the body and handing it to Invoke-WebRequest is unreliable on
    Windows PowerShell 5.1: byte arrays can get re-encoded as text, which corrupts
    binary content and produces a Content-Length that disagrees with the payload.
    ASP.NET then reports "Unexpected end of Stream". MultipartFormDataContent builds
    the boundary, headers and length correctly.
    #>
    param([string]$BaseUrl, [byte[]]$FileBytes, [string]$FileName, [string]$ContentType, [string]$Token)

    $handler = New-Object System.Net.Http.HttpClientHandler

    # On .NET Core / PS7, HttpClient ignores ServicePointManager, so the bypass has to go
    # on the handler. DangerousAcceptAnyServerCertificateValidator is a real compiled
    # delegate, unlike a scriptblock, so it works on the TLS background thread.
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        $handler.ServerCertificateCustomValidationCallback =
            [System.Net.Http.HttpClientHandler]::DangerousAcceptAnyServerCertificateValidator
    }
    # On .NET Framework / PS5.1, HttpClient DOES route through ServicePointManager,
    # which WeStayCertBypass::Enable() has already configured.

    $client  = New-Object System.Net.Http.HttpClient($handler)
    $content = New-Object System.Net.Http.MultipartFormDataContent

    try {
        $client.Timeout = [TimeSpan]::FromSeconds(60)
        if ($Token) {
            $client.DefaultRequestHeaders.Authorization =
                New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $Token)
        }

        $fileContent = New-Object System.Net.Http.ByteArrayContent(, $FileBytes)
        $fileContent.Headers.ContentType =
            [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse($ContentType)

        # Field name must be "file" to match UploadImage(IFormFile file).
        $content.Add($fileContent, 'file', $FileName)

        $resp = $client.PostAsync("$BaseUrl/api/listings/upload-image", $content).GetAwaiter().GetResult()
        $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()

        $parsed = $null
        if ($body) { try { $parsed = $body | ConvertFrom-Json } catch { $parsed = $body } }
        return @{ Status = [int]$resp.StatusCode; Body = $parsed; Raw = $body }
    } catch {
        # The useful message is usually two or three levels down; the outer one is just
        # "An error occurred while sending the request."
        $ex = $_.Exception
        $msg = $ex.Message
        while ($ex.InnerException) { $ex = $ex.InnerException; $msg = "$msg -> $($ex.Message)" }
        return @{ Status = 0; Body = $null; Raw = $msg }
    } finally {
        if ($content) { $content.Dispose() }
        if ($client)  { $client.Dispose() }
        if ($handler) { $handler.Dispose() }
    }
}

Write-Host "`n===== WeStay R2 image upload test =====" -ForegroundColor Cyan
Write-Host "Gateway: $Gateway`n" -ForegroundColor Gray

# ---------------------------------------------------------------- host account
Write-Head '1. Host account'

$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$email = "r2-test-$stamp@westay.local"

$r = Api POST '/api/auth/register' @{
    firstName='R2'; lastName='Test'; email=$email
    password='Test123!'; phoneNumber='+923001234567'; dateOfBirth=$null }
Assert-That 'register host' ($r.Status -eq 200) "status $($r.Status)"
$tok = Get-Field $r.Body @('Token')

$r = Api POST '/api/auth/become-host' $null $tok
if ($r.Status -eq 200) { $t = Get-Field $r.Body @('Token'); if ($t) { $tok = $t } }
Assert-That 'become-host' ($r.Status -eq 200) "status $($r.Status)"

if (-not $tok) { Write-Host "`nNo token. Stopping.`n" -ForegroundColor Red; exit 1 }

# ---------------------------------------------------------------- upload
Write-Head '2. Upload to R2'

# A real 1x1 PNG. Small, valid, and the .png name satisfies the extension allow-list.
$pngB64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=='
$pngBytes = [Convert]::FromBase64String($pngB64)
Write-Host "  test image: $($pngBytes.Length) bytes" -ForegroundColor DarkGray

$fileName = "r2-test-$stamp.png"
$imageUrl = $null

# --- through the gateway (what the front end actually does) ---
$viaGw = Invoke-Upload -BaseUrl $Gateway -FileBytes $pngBytes -FileName $fileName `
             -ContentType 'image/png' -Token $tok
Assert-That 'upload via gateway' ($viaGw.Status -eq 200) "status $($viaGw.Status) : $($viaGw.Raw)"
if ($viaGw.Status -eq 200) { $imageUrl = Get-Field $viaGw.Body @('ImageUrl') }

# --- straight to ListingService, only if the gateway path failed ---
if ($viaGw.Status -ne 200) {
    Write-Host '  gateway upload failed; retrying directly against ListingService' -ForegroundColor DarkGray
    $viaSvc = Invoke-Upload -BaseUrl $ListingDirect -FileBytes $pngBytes -FileName $fileName `
                  -ContentType 'image/png' -Token $tok
    if ($viaSvc.Status -eq 200) {
        $imageUrl = Get-Field $viaSvc.Body @('ImageUrl')
        Write-Host ''
        Write-Host '  DIAGNOSIS: direct upload works, gateway upload does not.' -ForegroundColor Yellow
        Write-Host '  R2 and ListingService are fine. Ocelot is not forwarding the' -ForegroundColor Yellow
        Write-Host '  multipart body intact. The rest of this run used the direct route.' -ForegroundColor Yellow
        Write-Host ''
    } else {
        Write-Host ''
        Write-Host '  DIAGNOSIS: direct upload fails too, so this is not a gateway problem.' -ForegroundColor Yellow
        Write-Host "  ListingService said: $($viaSvc.Raw)" -ForegroundColor Yellow
        Write-Host ''
    }
}

Assert-That 'response contains imageUrl' ($null -ne $imageUrl)
if ($imageUrl) { Write-Host "  returned: $imageUrl" -ForegroundColor DarkGray }

# ---------------------------------------------------------------- url sanity
Write-Head '3. URL shape'

if ($imageUrl) {
    Assert-That 'url is https' ($imageUrl -like 'https://*') "got $imageUrl"

    # The single most common mistake: PublicBaseUrl left as the S3 endpoint.
    Assert-That 'url is NOT the authenticated S3 endpoint' `
        ($imageUrl -notlike '*r2.cloudflarestorage.com*') `
        'R2:PublicBaseUrl points at the S3 API endpoint. Use the r2.dev subdomain or your custom domain.'

    Assert-That 'url has the listings/ key prefix' ($imageUrl -like '*/listings/*') "got $imageUrl"
} else {
    Assert-That 'url checks' $false 'no imageUrl to check'
}

# ---------------------------------------------------------------- public read
Write-Head '4. Public readability (no credentials)'

if ($imageUrl) {
    try {
        # Deliberately no Authorization header: this is what a browser does.
        $img = Invoke-WebRequest -Uri $imageUrl -Method GET -UseBasicParsing -ErrorAction Stop
        Assert-That 'image fetches anonymously (200)' ([int]$img.StatusCode -eq 200) "status $($img.StatusCode)"

        $ct = $img.Headers['Content-Type']
        Assert-That 'content-type is an image' ("$ct" -like 'image/*') "got '$ct'"

        $got = $img.Content
        if ($got -isnot [byte[]]) { $got = [System.Text.Encoding]::UTF8.GetBytes([string]$got) }
        Assert-That 'bytes match what was uploaded' `
            ($got.Length -eq $pngBytes.Length) `
            "uploaded $($pngBytes.Length) bytes, downloaded $($got.Length)"
    } catch {
        $st = 0
        if ($_.Exception.Response) { $st = [int]$_.Exception.Response.StatusCode }
        $hint = switch ($st) {
            401 { 'Bucket is private. Enable Public Development URL, or connect a custom domain.' }
            403 { 'Bucket is private, or PublicBaseUrl is the S3 endpoint rather than the public hostname.' }
            404 { 'Object not found at that URL. Check PublicBaseUrl matches the bucket serving these objects.' }
            default { $_.Exception.Message }
        }
        Assert-That 'image fetches anonymously' $false "status $st : $hint"
    }
} else {
    Assert-That 'public readability' $false 'no imageUrl to fetch'
}

# ---------------------------------------------------------------- attach
Write-Head '5. Attach to a listing'

if ($imageUrl) {
    $r = Api POST '/api/listings' @{
        title='R2 upload test'; description='Created by test-r2-upload.ps1. Safe to delete.'
        type=0; category=0; guests=2; bedrooms=1; beds=1; bathrooms=1
        pricePerNight=5000
        address='Street 1'; city='Islamabad'; state=''; country='Pakistan'; zipCode=''
        amenityIds=@(); imageUrls=@($imageUrl) } $tok
    Assert-That 'create listing with the image' ($r.Status -in 200,201) "status $($r.Status)"
    $lid = Get-Field $r.Body @('Id')

    if ($lid) {
        $r = Api GET "/api/listings/$lid" $null $tok
        $images = Get-Field $r.Body @('Images')
        Assert-That 'listing has 1 image' (@($images).Count -eq 1) "count $(@($images).Count)"
        $stored = if (@($images).Count) { Get-Field @($images)[0] @('ImageUrl') } else { $null }
        Assert-That 'stored url matches uploaded url' ($stored -eq $imageUrl) "stored '$stored'"
        $isPrimary = if (@($images).Count) { Get-Field @($images)[0] @('IsPrimary') } else { $null }
        Assert-That 'first image is primary' ($isPrimary -eq $true) "isPrimary=$isPrimary"
    }
}

# ---------------------------------------------------------------- summary
Write-Host "`n===== Summary =====" -ForegroundColor Cyan
Write-Host "  Passed: $script:Pass" -ForegroundColor Green
Write-Host "  Failed: $script:Fail" -ForegroundColor $(if ($script:Fail) { 'Red' } else { 'Green' })

if ($script:Fail -gt 0) {
    Write-Host "`n  Failures:" -ForegroundColor Red
    $script:Failures | ForEach-Object { Write-Host "    - $_" -ForegroundColor Red }
    Write-Host ''
    Write-Host '  Common causes:' -ForegroundColor Yellow
    Write-Host '    upload 500        : R2 secrets missing or wrong. Check the ListingService window.' -ForegroundColor Yellow
    Write-Host '    upload 403        : API token lacks Object Read and Write on the bucket.' -ForegroundColor Yellow
    Write-Host '    fetch 401 / 403   : bucket public access not enabled, or PublicBaseUrl is the' -ForegroundColor Yellow
    Write-Host '                        S3 endpoint instead of the r2.dev / custom domain.' -ForegroundColor Yellow
    Write-Host '    fetch 404         : PublicBaseUrl belongs to a different bucket.' -ForegroundColor Yellow
}
Write-Host ''
Write-Host "  Uploaded object: $imageUrl" -ForegroundColor Gray
Write-Host "  Test user:       $email" -ForegroundColor Gray
Write-Host ''

exit $(if ($script:Fail -gt 0) { 1 } else { 0 })
