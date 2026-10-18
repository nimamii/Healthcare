# Find out what is holding a port, and whether it is usable.
#
#   .\check-port.ps1
#
# The API container failed to start because 5432 was already bound. That is
# usually a locally installed PostgreSQL, which changes the decision: if one
# is running with the credentials we expect, the container is redundant.

$ErrorActionPreference = 'Continue'

function Ok($m)   { Write-Host "  [ok]   $m" -ForegroundColor Green }
function Warn($m) { Write-Host "  [warn] $m" -ForegroundColor Yellow }
function Bad($m)  { Write-Host "  [fail] $m" -ForegroundColor Red }
function Head($m) { Write-Host "`n$m" -ForegroundColor Cyan }

$ports = @(5432, 5080)

foreach ($port in $ports) {
    Head "Port $port"

    $listeners = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue

    if (-not $listeners) {
        Ok "Free"
        continue
    }

    foreach ($l in $listeners) {
        $procId = $l.OwningProcess
        $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue

        if ($proc) {
            Warn "in use by PID $procId ($($proc.ProcessName))"

            $path = $null
            try { $path = (Get-CimInstance Win32_Process -Filter "ProcessId=$procId").ExecutablePath } catch {}

            if ($path) { Write-Host "        $path" }
        } else {
            Warn "in use by PID $procId (process not readable)"
        }
    }

    if ($port -eq 5432) {
        $isPg = Get-Process -ErrorAction SilentlyContinue |
            Where-Object { $_.ProcessName -match 'postgres' }

        if ($isPg) {
            Write-Host ''
            Ok 'A local PostgreSQL is running.'
            Write-Host ''
            Write-Host 'This is usable instead of the container. To use it:'
            Write-Host ''
            Write-Host '  1. Set POSTGRES_PORT to something else in .env so the'
            Write-Host '     container does not fight it for the port, OR'
            Write-Host '  2. Stop the local service and let the container own it:'
            Write-Host '     net stop postgresql-x64-16'
            Write-Host ''
            Write-Host 'The second option keeps the setup reproducible for the team.'
            Write-Host 'The first avoids installing anything for one developer.'
        }
    }
}

Head 'Options for port 5432'

Write-Host 'A. Move the container to another host port (no install needed):' -ForegroundColor Cyan
Write-Host '     In .env, set:  POSTGRES_PORT=55432'
Write-Host '     The API connects inside the network, so it is unaffected.'
Write-Host ''
Write-Host 'B. Free the port by stopping the local service:' -ForegroundColor Cyan
Write-Host '     net stop postgresql-x64-16'
Write-Host '     (find the exact name with: Get-Service *postgres*)'
Write-Host ''
Write-Host 'C. Use the local PostgreSQL and drop the container:' -ForegroundColor Cyan
Write-Host '     Requires creating the database and user to match .env.'
