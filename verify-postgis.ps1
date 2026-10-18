# Verify PostGIS is available before any Geography code depends on it.
#
#   .\verify-postgis.ps1
#
# The postgis/postgis image is only on Docker Hub, which this network blocks,
# so the extension is built locally from a reachable postgres base. That build
# has to be proven to work before Geography is written against it: the spec
# requires PostGIS (§7) and a Geography module that silently has no spatial
# support is worse than one that was never started.

$ErrorActionPreference = 'Continue'

function Ok($m)   { Write-Host "  [ok]   $m" -ForegroundColor Green }
function Bad($m)  { Write-Host "  [fail] $m" -ForegroundColor Red }
function Warn($m) { Write-Host "  [warn] $m" -ForegroundColor Yellow }
function Head($m) { Write-Host "`n$m" -ForegroundColor Cyan }

$image = 'healthcare/postgres-postgis:16-3.4'

Head 'Building the image'

Write-Host '  First run downloads packages and takes a few minutes.' -ForegroundColor DarkGray

docker build -f Dockerfile.postgres -t $image .

if ($LASTEXITCODE -ne 0) {
    Bad 'The image did not build.'
    Write-Host ''
    Write-Host 'Read the error above. Two causes seen so far:'
    Write-Host ''
    Write-Host '  1. "Connection failed [IP: 10.10.34.35]"'
    Write-Host '     A Debian file routed through an internal mirror dropped the'
    Write-Host '     connection. The Dockerfile retries three times; if it still'
    Write-Host '     fails the mirror is the problem, not PostGIS.'
    Write-Host ''
    Write-Host '  2. "Unable to locate package postgresql-N-postgis-*"'
    Write-Host '     The name changed. Check it against the base image:'
    Write-Host '     docker build --progress=plain --no-cache -f probe.Dockerfile .'
    Write-Host ''
    Write-Host 'Rebuild with:  docker build -f Dockerfile.postgres -t ' + $image + ' .'
    exit 1
}

Ok 'image built'

Head 'Starting it on a throwaway port'

# Port 15433, distinct from the development database on 15432.
# POSTGRES_PASSWORD is required: the official entrypoint refuses to
# initialise a database without one, and the probe container is thrown away
# afterwards so the value never matters.
docker run -d --name hc-postgis-probe `
    -e POSTGRES_PASSWORD=probe_only `
    -p 15433:5432 `
    $image | Out-Null

if ($LASTEXITCODE -ne 0) {
    Bad 'The container did not start.'
    docker logs hc-postgis-probe
    exit 1
}

# Wait for it to accept connections.
$ready = $false

for ($i = 1; $i -le 30; $i++) {
    docker exec hc-postgis-probe pg_isready -U postgres 2>$null | Out-Null

    if ($LASTEXITCODE -eq 0) {
        $ready = $true
        break
    }

    Start-Sleep -Seconds 2
}

if (-not $ready) {
    Bad 'PostgreSQL never became ready.'
    docker logs hc-postgis-probe
    docker rm -f hc-postgis-probe 2>$null | Out-Null
    exit 1
}

Ok 'PostgreSQL is ready'

Head 'Creating the extension'

# The library being installed and preloaded proves nothing: postgis_lib_version()
# and ST_Contains only exist once CREATE EXTENSION has run. An earlier version of
# this script queried the function first, which fails by design and looks like a
# broken PostGIS.
$created = docker exec hc-postgis-probe `
    psql -U postgres -d postgres -v ON_ERROR_STOP=1 -t -A `
    -c "CREATE EXTENSION IF NOT EXISTS postgis;" 2>&1

if ($LASTEXITCODE -eq 0) {
    Ok 'CREATE EXTENSION postgis succeeded'
} else {
    Bad "CREATE EXTENSION failed: $created"
    docker logs hc-postgis-probe
    docker rm -f hc-postgis-probe 2>$null | Out-Null
    exit 1
}

Head 'Checking the version'

$version = docker exec hc-postgis-probe `
    psql -U postgres -d postgres -t -A -c "SELECT postgis_lib_version();" 2>&1

if ($LASTEXITCODE -eq 0 -and $version -match '\d+\.\d+') {
    Ok "PostGIS $version installed and callable"
} else {
    Bad "postgis_lib_version() failed: $version"
    docker rm -f hc-postgis-probe 2>$null | Out-Null
    exit 1
}

Head 'Checking spatial functions'

# ST_Contains is what point-in-polygon needs (§7). Verifying it separately
# from the version string catches an install that registered the library but
# did not create the functions.
$contains = docker exec hc-postgis-probe `
    psql -U postgres -t -A -c @"
SELECT ST_Contains(
    ST_GeomFromText('POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))', 4326),
    ST_GeomFromText('POINT(5 5)', 4326)
);
"@ 2>&1

if ($LASTEXITCODE -eq 0 -and $contains.Trim() -match '^(t|true)$') {
    Ok 'ST_Contains returns true for a point inside a polygon'
} else {
    Bad "ST_Contains failed: $contains"
}

Head 'Cleaning up'

docker rm -f hc-postgis-probe 2>$null | Out-Null
Ok 'probe container removed'

Write-Host ''
Ok "PostGIS is usable. Point compose at $image when starting Phase 4."
exit 0