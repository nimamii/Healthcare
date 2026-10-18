# Add a registry mirror to Docker's daemon config without destroying what is
# already there.
#
#   .\setup-mirror.ps1
#
# daemon.json may already contain proxy or DNS settings. Overwriting it
# wholesale can break those, so this merges: it reads the existing JSON,
# adds or replaces only the registry-mirrors key, and writes the rest back
# unchanged.
#
# Run it again to change or remove the mirror. Existing images are not
# affected; Docker Desktop must be restarted before the change takes effect.

[CmdletBinding()]
param(
    # Pass a mirror URL to set it. Pass nothing to be prompted, or '-Remove'
    # to strip the key.
    [string]$Mirror,

    [switch]$Remove,

    # Used when the file cannot be parsed as JSON, so nothing is lost.
    [string]$BackupPath = "$env:USERPROFILE\.docker\daemon.json.bak"
)

$ErrorActionPreference = 'Stop'

$daemonDir = Join-Path $env:USERPROFILE '.docker'
$daemonFile = Join-Path $daemonDir 'daemon.json'

function Ok($m)   { Write-Host "  [ok]   $m" -ForegroundColor Green }
function Warn($m) { Write-Host "  [warn] $m" -ForegroundColor Yellow }
function Bad($m)  { Write-Host "  [fail] $m" -ForegroundColor Red }

Write-Host "`nDocker registry mirror setup" -ForegroundColor Cyan

# --- What is there now? -----------------------------------------------------

$existing = $null

if (Test-Path $daemonFile) {
    $raw = Get-Content $daemonFile -Raw
    Ok "Found $daemonFile"

    if ([string]::IsNullOrWhiteSpace($raw)) {
        Warn 'File is empty; starting from scratch.'
    } else {
        # Back up before touching anything. If parsing fails, the original is
        # still on disk and the user can restore it.
        Copy-Item $daemonFile $BackupPath -Force
        Ok "Backed up to $BackupPath"

        try {
            $existing = $raw | ConvertFrom-Json
        } catch {
            Bad 'Existing file is not valid JSON. Leaving it untouched.'
            Write-Host ''
            Write-Host 'Open it manually and add the registry-mirrors key by hand:'
            Write-Host "  $daemonFile"
            Write-Host ''
            Write-Host "Your original is also at: $BackupPath"
            exit 1
        }
    }
} else {
    New-Item -ItemType Directory -Path $daemonDir -Force | Out-Null
    Ok "Created $daemonDir"
}

# --- Decide the mirror list -------------------------------------------------

if ($Remove) {
    Write-Host "`nRemoving registry-mirrors" -ForegroundColor Cyan

    if ($null -eq $existing) {
        Write-Host '  Nothing to remove.'
        exit 0
    }

    $existing.PSObject.Properties.Remove('registry-mirrors')

    $remaining = @($existing.PSObject.Properties | ForEach-Object { $_.Name })
    if ($remaining.Count -eq 0) {
        Remove-Item $daemonFile -Force
        Ok 'No settings remained, so daemon.json was deleted.'
    } else {
        $existing | ConvertTo-Json -Depth 10 | Set-Content $daemonFile
        Ok 'Key removed; other settings kept.'
    }

    Write-Host ''
    Write-Host 'Restart Docker Desktop to apply.'
    exit 0
}

if (-not $Mirror) {
    $default = 'https://docker.m.daocloud.io'

    Write-Host ''
    Write-Host "Enter the mirror your network allows, or press Enter for $default" -ForegroundColor Yellow
    Write-Host 'Ask IT first if unsure - a wrong address fails the same way.'

    $Mirror = Read-Host 'mirror'
    if ([string]::IsNullOrWhiteSpace($Mirror)) { $Mirror = $default }
}

$Mirror = $Mirror.TrimEnd('/')

Write-Host "`nConfiguring mirror" -ForegroundColor Cyan
Write-Host "  mirror: $Mirror"

if ($null -eq $existing) { $existing = [pscustomobject]@{} }

$existing | Add-Member -MemberType NoteProperty -Name 'registry-mirrors' -Value @($Mirror) -Force

$json = $existing | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($daemonFile, $json)

Ok "Wrote $daemonFile"

# --- Verify it is valid JSON before telling them to restart -----------------

try {
    Get-Content $daemonFile -Raw | ConvertFrom-Json | Out-Null
    Ok 'File is valid JSON'
} catch {
    Bad 'Wrote invalid JSON; Docker will not start. Restoring backup.'
    Copy-Item $BackupPath $daemonFile -Force
    Ok 'Original restored'
    exit 1
}

Write-Host ''
Write-Host 'Current contents:' -ForegroundColor Cyan
Get-Content $daemonFile | ForEach-Object { Write-Host "  $_" }

Write-Host ''
Write-Host 'Next steps:' -ForegroundColor Cyan
Write-Host '  1. Restart Docker Desktop (tray icon > Restart Docker Desktop)'
Write-Host '  2. docker pull redis:7-alpine'
Write-Host '  3. .\build.ps1 -Up'
Write-Host ''
Write-Host 'To undo:  .\setup-mirror.ps1 -Remove'
