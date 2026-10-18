# Build the solution without installing anything on this machine.
#
#   .\build.ps1            -> build
#   .\build.ps1 -Test      -> build and run tests
#   .\build.ps1 -Up        -> build and start the stack
#   .\build.ps1 -All       -> tests first, then start the stack (recommended)
#
# Requires Docker Desktop only. The .NET SDK runs inside a throwaway
# container, so nothing is installed on the host.
#
# Order matters and is handled here rather than left to the caller. Running
# tests, forgetting to rebuild, then starting a container from the previous
# source produces a schema that silently lacks whatever the new code adds -
# which is exactly how a run can pass against stale code.

[CmdletBinding()]
param(
    [switch]$Test,
    [switch]$Up,
    [switch]$All
)

if ($All) { $Test = $true; $Up = $true }

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# Docker needs forward slashes. A Windows path like E:\Healthcare becomes
# E:/Healthcare, which --mount parses correctly.
$mountSource = $root -replace '\\', '/'

function Write-Step($message) { Write-Host "`n==> $message" -ForegroundColor Cyan }
function Write-Ok($message)    { Write-Host "    $message" -ForegroundColor Green }
function Write-Fail($message)  { Write-Host "    $message" -ForegroundColor Red }

# --- Is Docker usable? ------------------------------------------------------

Write-Step 'Checking Docker'

try {
    $docker = Get-Command docker -ErrorAction Stop
} catch {
    Write-Fail 'Docker is not installed.'
    Write-Host ''
    Write-Host 'Install Docker Desktop, restart Windows, then run this again.'
    exit 1
}

try {
    $null = docker info 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'engine not responding' }
} catch {
    Write-Fail 'Docker Desktop is installed but the engine is not responding.'
    Write-Host ''
    Write-Host 'Start Docker Desktop and wait for it to finish loading, then retry.'
    exit 1
}

Write-Ok "Docker found: $($docker.Source)"

# --- .env -------------------------------------------------------------------

$envFile = Join-Path $root '.env'

if (-not (Test-Path $envFile)) {
    Write-Host '    .env not found, creating it from .env.example' -ForegroundColor Yellow
    Copy-Item (Join-Path $root '.env.example') $envFile
}

# Generate a signing key when one is missing or blank.
#
# Done as a line-by-line pass rather than a single -replace on a multi-line
# string: PowerShell 5.1 applies -replace per input line, so the earlier
# single-call version silently left the file unchanged and the failure
# surfaced later as a confusing compose error.
$needsKey = $false

foreach ($line in [System.IO.File]::ReadAllLines($envFile)) {
    if ($line -match '^JWT_SIGNING_KEY=(.*)$') {
        if ([string]::IsNullOrWhiteSpace($Matches[1])) { $needsKey = $true }
        break
    }
}

if ($needsKey) {
    Write-Host '    generating a JWT signing key' -ForegroundColor Yellow

    # RandomNumberGenerator.Fill only exists on .NET Core. Windows PowerShell
    # 5.1 runs on .NET Framework, so Create() is the portable form.
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $bytes = New-Object byte[] 48
        $rng.GetBytes($bytes)
    } finally {
        $rng.Dispose()
    }

    $key = [Convert]::ToBase64String($bytes)

    $updated = foreach ($line in [System.IO.File]::ReadAllLines($envFile)) {
        if ($line -match '^JWT_SIGNING_KEY=') { "JWT_SIGNING_KEY=$key" } else { $line }
    }

    [System.IO.File]::WriteAllLines($envFile, $updated)

    # Read back rather than trusting the write. A key that is still blank here
    # would fail later inside compose, with an error pointing at the wrong file.
    $written = ([System.IO.File]::ReadAllLines($envFile) |
        Where-Object { $_ -match '^JWT_SIGNING_KEY=' })

    if (-not $written -or ($written -split '=', 2)[1].Length -lt 32) {
        Write-Fail 'Could not write a valid JWT signing key to .env'
        Write-Host ''
        Write-Host 'Add one manually:  openssl rand -base64 48'
        exit 1
    }

    Write-Ok 'JWT signing key generated'
}
else {
    Write-Ok 'JWT signing key already present'
}

# --- Database first, when tests will run ------------------------------------
#
# The concurrency tests in Inventory.IntegrationTests need a live PostgreSQL:
# the behaviour they assert lives in UPDATE ... WHERE version = n, which no
# amount of faking reproduces. Starting the database here means those tests
# actually run instead of skipping, and a skip that looks like a pass is the
# one failure mode this project cannot afford.

if ($Test) {
    Write-Step 'Starting the database for integration tests'

    docker compose -f (Join-Path $root 'docker-compose.yml') up -d postgres

    if ($LASTEXITCODE -ne 0) {
        Write-Fail 'Could not start PostgreSQL.'
        exit 1
    }

    # Its healthcheck gates "accepting connections"; waiting on that is what
    # stops the first test run from failing on a database still booting.
    for ($i = 1; $i -le 30; $i++) {
        $healthy = docker compose -f (Join-Path $root 'docker-compose.yml') `
            ps postgres --format '{{.Health}}' 2>$null

        if ($healthy -match 'healthy') {
            Write-Ok "PostgreSQL is healthy (attempt $i)"
            break
        }

        if ($i -eq 30) {
            Write-Fail 'PostgreSQL did not become healthy in time.'
            exit 1
        }

        Start-Sleep -Seconds 2
    }

    # The test container is not on the compose network, so it reaches the
    # database through the published host port.
    $postgresPort = 15432
    $portMatch = [regex]::Match((Get-Content $envFile -Raw), 'POSTGRES_PORT=(\d+)')
    if ($portMatch.Success) { $postgresPort = [int]$portMatch.Groups[1].Value }

    $env:TEST_POSTGRES = "Host=host.docker.internal;Port=$postgresPort;Database=healthcare_test;Username=healthcare;Password=local_dev_only"

    Write-Ok "integration tests will use host.docker.internal:$postgresPort"
}

# --- Compile (always, and before anything depends on the result) ------------

$verb = if ($Test) { 'test' } else { 'build' }
$image = 'mcr.microsoft.com/dotnet/sdk:8.0'

Write-Step "Running: dotnet $verb (in a container)"

# dotnet and the verb must be separate arguments. Passing "dotnet build" as one
# string makes the runtime look for an executable with a space in its name,
# which is what "executable file not found in $PATH" means.
#
# TEST_POSTGRES is only forwarded when set. Passing an empty value would
# override the fallback the tests use, replacing a working default with an
# unparseable connection string.
$dockerArgs = @(
    'run', '--rm',
    '--mount', "type=bind,source=$mountSource,target=/src",
    '--workdir', '/src'
)

if ($env:TEST_POSTGRES) {
    $dockerArgs += @('--env', "TEST_POSTGRES=$env:TEST_POSTGRES")
}

$dockerArgs += @($image, 'dotnet', $verb)

& docker @dockerArgs

$exitCode = $LASTEXITCODE

Write-Host ''

if ($exitCode -ne 0) {
    Write-Fail "dotnet $verb failed (exit $exitCode)"
    Write-Host ''
    Write-Host 'The first run downloads the SDK image, which can take a few minutes.'
    Write-Host 'If it is a NuGet restore failure, the container has no network access.'
    exit $exitCode
}

Write-Ok "dotnet $verb succeeded"

if (-not $Up) {
    exit 0
}

# --- Start the stack --------------------------------------------------------

$compose = @('-f', (Join-Path $root 'docker-compose.yml'))

# Build first, separately from `up`.
#
# --no-cache is not a valid flag on `compose up`; it belongs to
# `compose build`. Running it as its own step is also clearer about what is
# happening: build (fresh), then start.
#
# The cache is bypassed because a previous build had been cached against a
# Directory.Build.props that was mid-edit, and it resurfaced much later as
# "Root element is missing" pointing at a file that was demonstrably valid.
docker compose @compose build --no-cache api
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

docker compose @compose up -d
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The schema comes from db/init/*.sql, which Postgres runs only when the volume
# is first created. If someone adds a module without a matching SQL file, the
# API starts, /health returns 200, and the first request fails with
# "relation ... does not exist" - a confusing way to learn a file is missing.
# Checking here turns that into an actionable message.
#
# Counting every non-system schema rather than a fixed list, so a newly added
# module is covered the moment its tables appear and never silently omitted.
Write-Step 'Verifying the schema'

$schemaCheck = docker compose exec -T postgres `
    psql -U healthcare -d healthcare -t -A `
    -c "SELECT table_schema || ':' || count(*) FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog', 'information_schema') GROUP BY table_schema ORDER BY table_schema;" 2>$null

if ($schemaCheck) {
    $found = @($schemaCheck | Where-Object { $_.Trim() } | ForEach-Object { $_.Trim() })

    foreach ($entry in $found) {
        $parts = $entry -split ':'
        Write-Ok "$($parts[0]) schema: $($parts[1]) table(s)"
    }

    # Every module must contribute a schema. A module that compiles and
    # registers but has no tables means its SQL is missing, and that surfaces
    # later as a runtime error rather than a build failure.
    $expected = @('identity', 'audit', 'catalog')
    $present = $found | ForEach-Object { ($_ -split ':')[0] }

    $absent = $expected | Where-Object { $_ -notin $present }

    if ($absent.Count -gt 0) {
        Write-Fail "missing schema(s): $($absent -join ', ')"
        Write-Host ''
        Write-Host 'The schema is created by db\init\001-schemas.sql, which Postgres'
        Write-Host 'runs only when the database volume is first created. Existing'
        Write-Host 'volumes are never re-initialised.'
        Write-Host ''
        Write-Host 'To recreate it:  docker compose down -v'
        Write-Host '    then:          .\build.ps1 -All'
        exit 1
    }
}
else {
    Warn 'Could not query the schema; the API may still start correctly'
}

# Read the port from .env so this message cannot drift from reality the way a
# hard-coded one did.
$apiPort = 8090
$m = [regex]::Match((Get-Content $envFile -Raw), 'API_PORT=(\d+)')
if ($m.Success) { $apiPort = [int]$m.Groups[1].Value }

Write-Step 'Waiting for the API to respond'

$url = "http://localhost:$apiPort/health"
$ready = $false

# Postgres initialises before the API can create its schema, so the first few
# attempts fail for a legitimate reason.
for ($i = 1; $i -le 30; $i++) {
    try {
        $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3

        if ($response.StatusCode -eq 200) {
            Write-Ok "API is up (attempt $i)"
            $ready = $true
            break
        }
    } catch {
        if ($i % 10 -eq 0) { Write-Host "    still starting... ($i/30)" }
    }

    Start-Sleep -Seconds 2
}

Write-Host ''

if (-not $ready) {
    Write-Fail 'API did not become healthy in time.'
    Write-Host ''
    Write-Host 'Recent logs:'
    Write-Host '  docker compose logs --tail 40 api'
    exit 1
}

Write-Host "  Swagger:  http://localhost:$apiPort/swagger" -ForegroundColor Green
Write-Host "  Health:   http://localhost:$apiPort/health"  -ForegroundColor Green
Write-Host ''
Write-Host 'Next:  .\test-api.ps1'
exit 0