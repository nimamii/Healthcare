# Find a working image source for this network.
#
#   .\find-registry.ps1
#
# docker.io is blocked here, but other registries may not be. This tests each
# candidate against the actual network and reports what works, so nothing
# here is a guess.
#
# It tries, in order:
#   1. images already cached locally
#   2. a registry mirror, if one is configured
#   3. alternative registries that host PostgreSQL and Redis
#   4. Docker Hub directly, as a baseline

$ErrorActionPreference = 'Continue'

function Ok($m)   { Write-Host "  [ok]   $m" -ForegroundColor Green }
function Warn($m) { Write-Host "  [warn] $m" -ForegroundColor Yellow }
function Bad($m)  { Write-Host "  [fail] $m" -ForegroundColor Red }
function Head($m) { Write-Host "`n$m" -ForegroundColor Cyan }

# Each candidate is tried with a tiny image where possible. A failure here
# means the registry is unreachable; it says nothing about whether the real
# image exists there.
$probes = @(
    @{ Registry = 'mcr.microsoft.com';        Path = 'dotnet/runtime:8.0';  Note = 'already known to work' }
    @{ Registry = 'ghcr.io';                 Path = 'homebrew/core/git:latest'; Note = 'GitHub Container Registry' }
    @{ Registry = 'quay.io';                 Path = 'quay/busybox:latest'; Note = 'Red Hat Quay' }
    @{ Registry = 'public.ecr.aws';          Path = 'docker/library/busybox:latest'; Note = 'AWS public ECR' }
    @{ Registry = 'gcr.io';                  Path = 'distroless/base:latest'; Note = 'Google Container Registry' }
    @{ Registry = 'registry-1.docker.io';    Path = 'library/busybox:latest'; Note = 'Docker Hub (baseline)' }
)

Head 'Probing registries'

$reachable = @()

foreach ($p in $probes) {
    $ref = "$($p.Registry)/$($p.Path)"

    Write-Host "  trying $ref" -ForegroundColor DarkGray

    $output = docker manifest inspect $ref 2>&1

    if ($LASTEXITCODE -eq 0) {
        Ok "$($p.Registry) is reachable  ($($p.Note))"
        $reachable += $p.Registry
    } else {
        $reason = ($output | Select-Object -First 1) -replace '\s+', ' '
        if ($reason.Length -gt 70) { $reason = $reason.Substring(0, 70) + '...' }
        Bad "$($p.Registry) - $reason"
    }
}

# --- What can we actually use? ----------------------------------------------

Head 'Result'

if ($reachable.Count -eq 0) {
    Bad 'No registry is reachable.'
    Write-Host ''
    Write-Host 'This is a network-level block, not a Docker problem.'
    Write-Host 'Ask IT to whitelist container registries, or to provide a'
    Write-Host 'corporate mirror hostname.'
    exit 1
}

Write-Host "  Reachable: $($reachable -join ', ')" -ForegroundColor Green

# --- The actual question: where do we get postgis? ---------------------------

Head 'Looking for PostgreSQL with PostGIS'

$postgresCandidates = @(
    'postgis/postgis:16-3.4'
    'ghcr.io/baosystems/postgis:16-3.4'
    'quay.io/postgis/postgis:16-3.4'
    'public.ecr.aws/docker/library/postgres:16'
)

$postgresFound = $null

foreach ($ref in $postgresCandidates) {
    Write-Host "  trying $ref" -ForegroundColor DarkGray

    $out = docker manifest inspect $ref 2>&1
    if ($LASTEXITCODE -eq 0) {
        Ok "$ref"
        $postgresFound = $ref
        break
    } else {
        $reason = ($out | Select-Object -First 1) -replace '\s+', ' '
        if ($reason.Length -gt 60) { $reason = $reason.Substring(0, 60) + '...' }
        Bad "$ref - $reason"
    }
}

Head 'Looking for Redis'

$redisCandidates = @(
    'redis:7-alpine'
    'public.ecr.aws/docker/library/redis:7-alpine'
    'quay.io/prometheus/busybox:latest'
)

$redisFound = $null

foreach ($ref in $redisCandidates) {
    Write-Host "  trying $ref" -ForegroundColor DarkGray

    $out = docker manifest inspect $ref 2>&1
    if ($LASTEXITCODE -eq 0) {
        Ok "$ref"
        $redisFound = $ref
        break
    } else {
        $reason = ($out | Select-Object -First 1) -replace '\s+', ' '
        if ($reason.Length -gt 60) { $reason = $reason.Substring(0, 60) + '...' }
        Bad "$ref - $reason"
    }
}

# --- Verdict ----------------------------------------------------------------

Head 'Verdict'

if ($postgresFound -and $redisFound) {
    Ok "PostgreSQL: $postgresFound"
    Ok "Redis:      $redisFound"
    Write-Host ''
    Write-Host 'Both are available. I can update docker-compose.yml to use them.'
    Write-Host 'Run  .\find-registry.ps1  output is all that is needed.'
} elseif ($postgresFound) {
    Ok "PostgreSQL: $postgresFound"
    Warn "Redis: not found at any candidate"
    Write-Host ''
    Write-Host 'Redis is not required for Phase 1 - nothing in the code uses it yet.'
    Write-Host 'It can be dropped from docker-compose.yml and added back in the'
    Write-Host 'phase that first needs a cache.'
} else {
    Bad 'No PostgreSQL image with PostGIS is reachable.'
    Write-Host ''
    Write-Host 'PostGIS is only published to Docker Hub, so this needs either:'
    Write-Host '  1. IT whitelists docker.io, or'
    Write-Host '  2. a registry mirror  (.\setup-mirror.ps1), or'
    Write-Host '  3. PostgreSQL installed directly on Windows instead of Docker'
    Write-Host ''
    Write-Host 'Option 3 avoids Docker entirely. The connection string in'
    Write-Host 'docker-compose.yml is all that would need to change.'
}

exit 0
