# Check whether the images this project needs can be pulled.
#
#   .\check-network.ps1
#
# Distinguishes four failure modes, because the fix is different for each:
#   1. images already cached      -> nothing to do
#   2. registry reachable         -> transient failure, retry
#   3. DNS fails                  -> network problem, a mirror will not help
#   4. blocked by firewall        -> a mirror is the fix

# Persian output arrives as UTF-8 and the Windows console re-encodes it to the
# OEM code page, which renders it as mojibake. The data is correct; only the
# display is wrong — which is worse, because it looks like corruption.
try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $OutputEncoding = [System.Text.Encoding]::UTF8
} catch {
    # Not fatal. Output is unreadable but the checks still work.
}


$ErrorActionPreference = 'Continue'

function Ok($m)   { Write-Host "  [ok]   $m" -ForegroundColor Green }
function Warn($m) { Write-Host "  [warn] $m" -ForegroundColor Yellow }
function Bad($m)  { Write-Host "  [fail] $m" -ForegroundColor Red }
function Head($m) { Write-Host "`n$m" -ForegroundColor Cyan }

$required = @('postgis/postgis:16-3.4', 'redis:7-alpine')

# --- 1. Already cached? -----------------------------------------------------

Head 'Checking local image cache'

$missing = @()

foreach ($image in $required) {
    $found = docker images -q $image 2>$null

    if ($found) { Ok "$image is cached" }
    else { Warn "$image is not cached"; $missing += $image }
}

if ($missing.Count -eq 0) {
    Write-Host ''
    Ok 'All required images are present. You can run: .\build.ps1 -Up'
    exit 0
}

# --- 2. Registry reachable? -------------------------------------------------

Head 'Testing registry connectivity'

try {
    $dnsOk = $true
    Resolve-DnsName registry-1.docker.io -ErrorAction Stop | Out-Null
    Ok 'DNS resolves registry-1.docker.io'
} catch {
    $dnsOk = $false
    Bad 'DNS does not resolve. A registry mirror will not fix this.'
}

# --- 3. Is a mirror already configured? -------------------------------------

Head 'Checking for a configured mirror'

$daemon = "$env:USERPROFILE\.docker\daemon.json"

if (Test-Path $daemon) {
    $content = Get-Content $daemon -Raw

    if ($content -match 'registry-mirrors') {
        Ok 'registry-mirrors is configured in daemon.json'
        Write-Host '        If pulls still fail, restart Docker Desktop.'
    } else {
        Warn 'daemon.json exists but has no registry-mirrors'
        Write-Host "        Add it to: $daemon"
    }
} else {
    Warn "No daemon.json at $daemon"
    Write-Host '        See docs\REGISTRY-MIRROR.md for the setup.'
}

# --- 4. Summary -------------------------------------------------------------

Head 'Summary'

if (-not $dnsOk) {
    Bad 'DNS is failing. Check with: Resolve-DnsName registry-1.docker.io'
    Write-Host ''
    Write-Host 'This is a network problem, not a Docker problem.'
    Write-Host 'Ask IT whether outbound DNS or docker.io is blocked.'
} else {
    Bad "$($missing.Count) image(s) cannot be downloaded."
    Write-Host ''
    Write-Host 'docker.io is blocked on this network. Options:'
    Write-Host ''
    Write-Host '  1. Configure a registry mirror'
    Write-Host '     .\docs\REGISTRY-MIRROR.md'
    Write-Host ''
    Write-Host '  2. Ask IT to whitelist docker.io, or to tell you the'
    Write-Host '     approved mirror hostname.'
    Write-Host ''
    Write-Host '  3. Check whether a proxy is required'
    Write-Host '     Docker Desktop > Settings > Resources > Proxies'
}

exit 1
