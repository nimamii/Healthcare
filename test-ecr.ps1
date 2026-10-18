# Check which PostgreSQL images are actually pullable on this network.
#
#   .\test-ecr.ps1
#
# public.ecr.aws was found reachable, and it mirrors Docker Hub's library
# images. This verifies whether the PostGIS build is there too, since the
# spec (§7) requires PostGIS for the Geography phase.
#
# It uses `docker manifest inspect`, which asks the registry about an image
# without downloading it. A failure here means "not available or not
# reachable" — it does not distinguish between the two, so the fallback
# list is ordered by likelihood.

$ErrorActionPreference = 'Continue'

function Ok($m)   { Write-Host "  [ok]   $m" -ForegroundColor Green }
function Bad($m)  { Write-Host "  [fail] $m" -ForegroundColor Red }
function Head($m) { Write-Host "`n$m" -ForegroundColor Cyan }

$candidates = @(
    'public.ecr.aws/docker/library/postgis:16-3.4'
    'public.ecr.aws/docker/library/postgis:16-3.5'
    'public.ecr.aws/docker/library/postgis:16-2.5'
    'public.ecr.aws/docker/library/postgis:16-1.5'
    'public.ecr.aws/postgres/postgis:16-3.4'
    'public.ecr.aws/debian/postgres-postgis:16-3.4'
    'quay.io/postgis/postgis:latest'
    'quay.io/sclorg/postgresql-16-c9s:latest'
    'public.ecr.aws/docker/library/postgres:16'
    'public.ecr.aws/docker/library/postgres:15'
)

Head 'Checking for a PostGIS image'

$found = @()

foreach ($ref in $candidates) {
    Write-Host "  trying $ref" -ForegroundColor DarkGray

    $out = docker manifest inspect $ref 2>&1

    if ($LASTEXITCODE -eq 0) {
        Ok $ref
        $found += $ref
    } else {
        $reason = ($out | Select-Object -First 1) -replace '\s+', ' '
        if ($reason.Length -gt 64) { $reason = $reason.Substring(0, 64) + '...' }
        Bad "$ref - $reason"
    }
}

Head 'Result'

if ($found.Count -gt 0) {
    Write-Host "  PostGIS images available:" -ForegroundColor Green
    $found | ForEach-Object { Write-Host "    $_" -ForegroundColor Green }
    Write-Host ''
    Write-Host 'Use the first entry for docker-compose.yml.'
} else {
    Bad 'No PostGIS image is reachable.'
    Write-Host ''
    Write-Host 'PostGIS is only needed in Phase 4 (Geography), per the spec.'
    Write-Host 'Phase 1 is authentication only and uses plain PostgreSQL.'
    Write-Host ''
    Write-Host 'Option A: start with plain postgres:16 and add PostGIS in Phase 4'
    Write-Host 'Option B: build PostGIS from a plain postgres image + extension package'
    Write-Host ''
    Write-Host 'Option B needs a Dockerfile that installs postgis, which requires'
    Write-Host 'apt access from inside the container. Testable with:'
    Write-Host ''
    Write-Host '  docker run --rm public.ecr.aws/docker/library/postgres:16 \'
    Write-Host '    apt-get update && apt-cache search postgis'
}
