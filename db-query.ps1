# Inspect the identity schema and its rows.
#
#   .\db-query.ps1 -Users
#   .\db-query.ps1 -Describe
#   .\db-query.ps1 "SELECT count(*) FROM identity.users;"
#
# Why this wrapper exists
# ----------------------
# The columns in this database are PascalCase (Email, PasswordHash, Role) even
# though CONVENTIONS.md specifies snake_case. That mismatch is being fixed, but
# until it is, any query touching those tables must quote its identifiers.
#
# PowerShell strips the double quotes from an argument before the process sees
# them, so a hand-written
#   ... -c 'SELECT "Email" FROM identity.users'
# arrives at psql as SELECT Email ... and Postgres folds that to lowercase,
# producing an error about a column that demonstrably exists.
#
# So this script reads the real column names from information_schema and
# quotes them with chr(34), which is the SQL-safe way to embed a quote without
# relying on the shell preserving one.

param(
    [Parameter(Position = 0)]
    [string]$Query,

    [switch]$Users,

    [switch]$Describe,

    [switch]$Roles
)

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
function Head($m) { Write-Host "`n$m" -ForegroundColor Cyan }

function Invoke-Sql {
    param([string]$Sql)

    Write-Host "`n  $Sql" -ForegroundColor DarkGray
    & docker compose exec -T postgres psql -U healthcare -d healthcare -A -t -c $Sql
}

# Returns the real column names for a table, trimmed.
function Get-Columns {
    param([string]$Table)

    $rows = & docker compose exec -T postgres psql -U healthcare -d healthcare -A -t -c `
        "SELECT column_name FROM information_schema.columns WHERE table_schema = 'identity' AND table_name = '$Table' ORDER BY ordinal_position;"

    return @($rows | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

# Quotes an identifier using chr(34) so the value never passes through a
# shell-quoting layer.
function Quote {
    param([string]$Name)

    return "chr(34) || '$Name' || chr(34)"
}

# --- Schema ------------------------------------------------------------------

if ($Describe) {
    Head 'Tables'

    Invoke-Sql "SELECT table_name FROM information_schema.tables WHERE table_schema = 'identity' ORDER BY table_name;"

    Head 'Columns'

    Invoke-Sql "SELECT table_name || '.' || column_name || ' : ' || data_type FROM information_schema.columns WHERE table_schema = 'identity' ORDER BY table_name, ordinal_position;"
}

# --- Users -------------------------------------------------------------------

if ($Users) {
    Head 'Users'

    $columns = Get-Columns -Table 'users'

    if (-not $columns) {
        Bad 'identity.users not found. Is the API running?'
        exit 1
    }

    Write-Host "  columns: $($columns -join ', ')" -ForegroundColor DarkGray

    $pick = {
        param($Pattern)
        $columns | Where-Object { $_ -match $Pattern } | Select-Object -First 1
    }

    $email   = & $pick 'email'
    $hash    = & $pick 'passwordhash'
    $active  = & $pick 'isactive'
    $created = & $pick '^createdat$'

    $parts = [System.Collections.Generic.List[string]]::new()
    $parts.Add((Quote $email))

    if ($hash)    { $parts.Add("length($(Quote $hash)) AS hashlen") }
    if ($active)  { $parts.Add((Quote $active)) }
    if ($created) { $parts.Add((Quote $created)) }

    Invoke-Sql ("SELECT " + ($parts -join ', ') + " FROM identity.users ORDER BY $(Quote $created);")

    # A stored hash of the form <iterations>.<salt>.<hash> is roughly 90-120
    # characters. A much shorter value means hashing did not happen.
    $rowCount = & docker compose exec -T postgres psql -U healthcare -d healthcare -A -t -c `
        "SELECT count(*) FROM identity.users;"

    if ([int]($rowCount.Trim()) -eq 0) {
        Write-Host ''
        Bad 'no users stored - registration is not persisting'
        Write-Host '  docker compose logs --tail 30 api'
    } else {
        Write-Host ''
        Ok "$($rowCount.Trim()) user(s) stored"
    }
}

# --- Roles -------------------------------------------------------------------

if ($Roles) {
    Head 'Roles'

    $columns = Get-Columns -Table 'user_roles'

    $roleColumn = $columns | Where-Object { $_ -match 'role' -and $_ -notmatch 'userid' } | Select-Object -First 1

    if (-not $roleColumn) { Bad 'no role column found'; exit 1 }

    Invoke-Sql ("SELECT $(Quote $roleColumn) || ' : ' || count(*) FROM identity.user_roles GROUP BY $(Quote $roleColumn) ORDER BY 1;")
}

# --- Ad-hoc query ------------------------------------------------------------

if ($Query) {
    Head 'Query'
    Invoke-Sql $Query
}

exit 0