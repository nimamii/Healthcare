# Verify the configured host ports are usable before starting the stack.
#
#   .\check-ports.ps1
#
# Windows reserves port ranges for Hyper-V and WSL. A port inside a reserved
# range reports as "free" to Get-NetTCPConnection but fails to bind, with an
# error that says "access to a socket in a way forbidden by its access
# permissions" - which does not obviously mean a port conflict.
#
# The ranges change across reboots, so this is worth re-running rather than
# hard-coding a port once and assuming it still works.

$ErrorActionPreference = 'Continue'

function Ok($m)   { Write-Host "  [ok]   $m" -ForegroundColor Green }
function Bad($m)  { Write-Host "  [fail] $m" -ForegroundColor Red }
function Warn($m) { Write-Host "  [warn] $m" -ForegroundColor Yellow }
function Head($m) { Write-Host "`n$m" -ForegroundColor Cyan }

# --- Read the reserved ranges ------------------------------------------------

Head 'Windows reserved port ranges'

$reserved = @()

try {
    $output = netsh interface ipv4 show excludedportrange protocol=tcp 2>&1

    foreach ($line in $output) {
        # Lines look like: "    5378       5477"
        if ($line -match '^\s*(\d+)\s+(\d+)\s*(\*)?\s*$') {
            $start = [int]$Matches[1]
            $end = [int]$Matches[2]
            $reserved += ,@($start, $end, $Matches[3] -eq '*')
        }
    }

    if ($reserved.Count -eq 0) {
        Warn 'Could not parse netsh output. Cannot verify ports.'
    } else {
        Ok "$($reserved.Count) range(s) reserved"
    }
} catch {
    Bad "Could not query reserved ranges: $_"
}

# --- Check the configured ports ---------------------------------------------

Head 'Configured ports'

if (-not (Test-Path '.env')) {
    Bad 'No .env found. Run .env.example through the setup first.'
    exit 1
}

$envContent = Get-Content '.env' -Raw

$ports = @{}
foreach ($key in @('POSTGRES_PORT', 'API_PORT')) {
    $m = [regex]::Match($envContent, "$key=(\d+)")
    if ($m.Success) { $ports[$key] = [int]$m.Groups[1].Value }
}

if ($ports.Count -eq 0) {
    Bad 'Neither POSTGRES_PORT nor API_PORT is set in .env'
    exit 1
}

$problems = @()

foreach ($key in $ports.GetEnumerator() | Sort-Object Name) {
    $port = $key.Value

    $hit = $reserved | Where-Object { $port -ge $_[0] -and $port -le $_[1] }

    if ($hit) {
        Bad "$($key.Name)=$port is inside a reserved range ($($hit[0])-$($hit[1]))"
        $problems += $key.Name
        continue
    }

    # Reserved is not the only failure mode: a real process also blocks it.
    $listener = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue

    if ($listener) {
        $procId = ($listener | Select-Object -First 1).OwningProcess
        $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
        $name = if ($proc) { $proc.ProcessName } else { "PID $procId" }

        Bad "$($key.Name)=$port is used by $name"
        $problems += $key.Name
        continue
    }

    Ok "$($key.Name)=$port is usable"
}

# --- Advice ------------------------------------------------------------------

if ($problems.Count -eq 0) {
    Write-Host ''
    Ok 'All configured ports are usable.'
    Write-Host ''
    Write-Host 'Run: .\build.ps1 -Up'
    exit 0
}

Write-Host ''
Write-Host 'How to fix' -ForegroundColor Cyan
Write-Host ''
Write-Host 'A port in a reserved range can be moved in .env. Any value outside the'
Write-Host 'listed ranges works. 15xxx and 8xxx are usually safe, but re-run this'
Write-Host 'script after a reboot since the ranges shift.'
Write-Host ''
Write-Host '  notepad .env'
Write-Host ''
Write-Host 'Then set a different POSTGRES_PORT and API_PORT.'

exit 1
