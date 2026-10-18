# Regenerate the Dockerfile restore layer from the solution.
#
#   .\sync-dockerfile.ps1
#
# The restore layer copies each project file before `dotnet restore`, so the
# layer cache survives a source-only change. That optimisation depends on
# the list being complete: a missing entry does not fail at restore, it is
# skipped with a warning, and the build fails much later with
# "Assets file not found" - an error that names neither the Dockerfile nor the
# missing line.
#
# The list is derived from the solution rather than hand-maintained, so adding
# a module cannot produce that error.

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$sln = Join-Path $root 'Healthcare.sln'
$dockerfile = Join-Path $root 'Dockerfile'

function Ok($m)   { Write-Host "  [ok]   $m" -ForegroundColor Green }
function Bad($m)  { Write-Host "  [fail] $m" -ForegroundColor Red }
function Head($m) { Write-Host "`n$m" -ForegroundColor Cyan }

Head 'Reading the solution'

$text = [System.IO.File]::ReadAllText($sln)

# Only real projects. The tests are built by build.ps1 in their own container,
# so they are deliberately not in the image.
# The pipeline cannot start on the line after a closing paren, so the regex
# result is assigned first and piped afterwards.
$matches = [regex]::Matches(
    $text,
    '(?m)^Project\("\{[0-9A-F-]+\}"\)\s*=\s*"[^"]+",\s*"([^"]+\.csproj)"')

$paths = $matches |
    ForEach-Object { $_.Groups[1].Value } |
    Where-Object { $_ -notlike 'tests\*' } |
    Sort-Object -Unique

if ($paths.Count -eq 0) {
    Bad 'No projects found in the solution.'
    exit 1
}

Ok "$($paths.Count) buildable project(s) (tests excluded)"

# --- Rebuild the COPY block -------------------------------------------------

Head 'Rewriting the restore layer'

# Forward slashes, not the solution file's backslashes. Docker normalises
# them, and it sidesteps the trap that broke an earlier version: PowerShell's
# -f treats a backslash as an escape character, so 'src\Modules\X' -f
# silently produced 'srcModulesX'.
$lines = foreach ($p in $paths) {
    $posix = $p.Replace('\', '/')
    'COPY {0} {0}' -f $posix
}

$block = $lines -join "`n"

$existing = [System.IO.File]::ReadAllText($dockerfile)

# Replace everything from the first COPY *.csproj to the RUN dotnet restore.
$pattern = '(?s)(#\[restore.*?#\[restore\]\n)?(COPY [^\n]*\.csproj[^\n]*\n)+RUN dotnet restore'

if ($existing -notmatch $pattern) {
    Bad 'Could not find the restore COPY block in the Dockerfile.'
    Write-Host '    The pattern this script rewrites may have changed.'
    exit 1
}

$replacement = "$block`nRUN dotnet restore"

$updated = [regex]::Replace($existing, $pattern, $replacement, 1)
[System.IO.File]::WriteAllText($dockerfile, $updated)

Ok "rewrote the COPY block with $($paths.Count) entries"

# --- Verify -----------------------------------------------------------------

Head 'Verifying'

$after = [System.IO.File]::ReadAllText($dockerfile)

$missing = @()
foreach ($p in $paths) {
    $posix = $p.Replace('\', '/')
    if ($after -notmatch [regex]::Escape("COPY $posix ")) { $missing += $posix }
}

if ($missing.Count -gt 0) {
    foreach ($m in $missing) { Bad "missing from Dockerfile: $m" }
    exit 1
}

# Every path copied must exist on disk.
$notOnDisk = @()
foreach ($p in $paths) {
    $full = Join-Path $root ($p -replace '\\', '/')
    if (-not (Test-Path $full)) { $notOnDisk += $p }
}

if ($notOnDisk.Count -gt 0) {
    foreach ($p in $notOnDisk) { Bad "referenced but missing: $p" }
    exit 1
}

Ok 'every buildable project is copied and every copied path exists'
exit 0