# Work out why audit.entries has no rows.
#
#   .\diag-audit.ps1
#
# Three things can produce an empty audit table, and they need different fixes:
#
#   1. The table does not exist      -> schema SQL never ran
#   2. The table exists but is empty -> the recorder is not being called, or
#                                      its write is failing and being swallowed
#   3. The table has rows but the query hides them -> wrong schema or case
#
# Case 2 is the one worth scripting: the recorder deliberately swallows
# failures so a logging outage cannot break login, which means the symptom and
# the cause are in different places. Only the server log separates them.

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
function Bad($m)  { Write-Host "  [fail] $m" -ForegroundColor Red }
function Warn($m) { Write-Host "  [warn] $m" -ForegroundColor Yellow }
function Head($m) { Write-Host "`n$m" -ForegroundColor Cyan }

function Invoke-Sql {
    param([string]$Sql)

    & docker compose exec -T postgres psql -U healthcare -d healthcare -A -t -c $Sql
}

Head '1. Does the table exist?'

$exists = Invoke-Sql "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'audit' AND table_name = 'entries';"

if ($exists.Trim() -eq '0') {
    Bad 'audit.entries does not exist.'
    Write-Host ''
    Write-Host 'The schema file has not been applied. It runs only on a fresh volume:'
    Write-Host ''
    Write-Host '  docker compose down -v'
    Write-Host '  .\build.ps1 -All'
    exit 1
}

Ok 'audit.entries exists'

Head '2. Does it have rows?'

$count = Invoke-Sql "SELECT count(*) FROM audit.entries;"

if ($count.Trim() -eq '0') {
    Warn 'the table is empty'

    Write-Host ''
    Write-Host 'The recorder either is not being called, or its write is failing.'
    Write-Host 'The second case is invisible from here on purpose: RecordAsync'
    Write-Host 'swallows failures so a logging outage cannot break login.'
    Write-Host ''
    Write-Host 'So check the server log:'

    $found = docker compose logs api 2>&1 |
        Select-String -Pattern 'Failed to record security event' -SimpleMatch

    if ($found) {
        Bad 'the recorder IS failing, and this is why:'
        $found | Select-Object -Last 3 | ForEach-Object { Write-Host "        $_" -ForegroundColor DarkGray }
    } else {
        Write-Host '  [ok]   no "Failed to record security event" in the log'
        Write-Host ''
        Write-Host '  So the recorder is not being reached at all. Check that'
        Write-Host '  AuditModule is in the IModule[] array in Program.cs.'
    }

    Write-Host ''
    Write-Host 'Full log:  docker compose logs --tail 40 api'
    exit 1
}

Ok "$($count.Trim()) row(s) present"

Head '3. Recent entries'

Invoke-Sql "SELECT occurred_at || ' | ' || category || ' | ' || action || ' | ' || outcome FROM audit.entries ORDER BY occurred_at DESC LIMIT 10;"

Write-Host ''
Write-Host 'To exercise both paths, run:'
Write-Host ''
Write-Host '  .\test-api.ps1'
Write-Host ''
Write-Host 'It registers, logs in, and attempts a wrong password — which should'
Write-Host 'produce a login.failed_credentials row with outcome = failure.'
exit 0