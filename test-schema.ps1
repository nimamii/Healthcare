# Verify the database matches what the code expects.
#
#   .\test-schema.ps1
#
# Checks that every table and column the application maps actually exists.
# A missing table is not a build error - the build only compiles code - so
# this is the check that catches "it compiles but there is nothing behind it".
#
# Also confirms no column has a numeric suffix, which is what a second EF
# Core relationship to the same table produces.

try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $OutputEncoding = [System.Text.Encoding]::UTF8
} catch {
}

$ErrorActionPreference = 'Stop'

function Ok($m)   { Write-Host "  [ok]   $m" -ForegroundColor Green }
function Bad($m)  { Write-Host "  [fail] $m" -ForegroundColor Red }
function Warn($m) { Write-Host "  [warn] $m" -ForegroundColor Yellow }
function Head($m) { Write-Host "`n$m" -ForegroundColor Cyan }

function Invoke-Sql {
    param([string]$Sql)

    $result = & docker compose exec -T postgres psql -U healthcare -d healthcare -A -t -c $Sql 2>$null
    return @($result | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

# --- What the code maps ------------------------------------------------------

# Kept in step with the DbContext entities and with db/init/001-schemas.sql.
# A table listed here that does not exist is a schema the app will fail on.
$expected = @{
    'identity.users'           = @('id','email','password_hash','is_active','created_at','deactivated_at','last_login_at','password_changed_at')
    'identity.user_roles'      = @('user_id','role','granted_at')
    'identity.refresh_tokens'  = @('id','user_id','token_hash','expires_at','created_at','revoked_at','reused_at')
    'audit.entries'            = @('id','occurred_at','category','action','user_id','subject','outcome','detail')
    'catalog.categories'       = @('id','name','slug','parent_id','depth','is_active','created_at','updated_at')
    'catalog.products'         = @('id','name','slug','description','category_id','requires_prescription','is_active','created_at','updated_at')
    'catalog.skus'             = @('id','product_id','code','variant_description','is_active','created_at','updated_at')
    'catalog.supplier_products'= @('id','sku_id','supplier_id','supplier_sku','list_price','currency','is_active','created_at','updated_at')
    'inventory.stock_items'    = @('id','sku_id','supplier_id','available_quantity','reserved_quantity','version','created_at','updated_at')
    'inventory.reservations'   = @('id','order_id','sku_id','quantity','status','expires_at','created_at','resolved_at')
    'geography.service_areas'  = @('id','supplier_id','name','polygon_wkt','polygon','out_of_area_surcharge','out_of_area_currency','is_active','created_at','updated_at')
}

# --- Tables exist? ----------------------------------------------------------

Head 'Tables'

$present = Invoke-Sql "SELECT table_schema || '.' || table_name FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog','information_schema') ORDER BY 1;"

foreach ($schema in @('identity','audit','catalog','inventory')) {
    $count = @($present | Where-Object { $_ -like "$schema.*" }).Count

    if ($count -gt 0) {
        Ok "$schema : $count table(s)"
    } else {
        Bad "$schema : no tables"
    }
}

# --- Columns match? ---------------------------------------------------------

Head 'Columns'

$problems = @()

foreach ($table in $expected.Keys) {
    $actual = Invoke-Sql @"
SELECT column_name FROM information_schema.columns
WHERE table_schema = '$($table.Split('.')[0])'
  AND table_name   = '$($table.Split('.')[1])'
ORDER BY ordinal_position;
"@

    if ($actual.Count -eq 0) {
        $problems += "$table does not exist"
        continue
    }

    $missing = $expected[$table] | Where-Object { $_ -notin $actual }
    $extra   = $actual | Where-Object { $_ -notin $expected[$table] }

    if ($missing) { $problems += "$table missing: $($missing -join ', ')" }
    if ($extra)   { $problems += "$table has unexpected: $($extra -join ', ')" }
}

if ($problems.Count -eq 0) {
    Ok "all $($expected.Count) tables match their expected columns"
} else {
    foreach ($p in $problems) { Bad $p }
}

# --- No suffixed columns ----------------------------------------------------

Head 'Column naming'

$suffixed = Invoke-Sql @"
SELECT table_schema || '.' || table_name || '.' || column_name
FROM information_schema.columns
WHERE table_schema NOT IN ('pg_catalog','information_schema')
  AND column_name ~ '[0-9]$'
ORDER BY 1;
"@

if ($suffixed.Count -eq 0) {
    Ok 'no column ends in a digit'
} else {
    Bad "suffixed columns found: $($suffixed -join ', ')"
    Write-Host '        These come from a second EF Core relationship to one table.'
}

# --- Indexes that carry correctness ----------------------------------------

Head 'Correctness constraints'

$checks = @(
    @{ Name = 'unique refresh token hash'; Sql = "SELECT count(*) FROM pg_indexes WHERE tablename = 'refresh_tokens' AND indexdef LIKE '%UNIQUE%';" }
    @{ Name = 'unique user email';          Sql = "SELECT count(*) FROM pg_indexes WHERE tablename = 'users' AND indexdef LIKE '%UNIQUE%';" }
    @{ Name = 'unique sku code';            Sql = "SELECT count(*) FROM pg_indexes WHERE tablename = 'skus' AND indexdef LIKE '%UNIQUE%';" }
    @{ Name = 'unique sku+supplier';        Sql = "SELECT count(*) FROM pg_indexes WHERE tablename = 'supplier_products' AND indexdef LIKE '%UNIQUE%';" }
    @{ Name = 'price cannot be zero';       Sql = "SELECT count(*) FROM pg_constraint WHERE conname = 'ck_supplier_products_price';" }
    @{ Name = 'one stock row per pair';     Sql = "SELECT count(*) FROM pg_indexes WHERE tablename = 'stock_items' AND indexdef LIKE '%UNIQUE%';" }
    @{ Name = 'stock cannot go negative';   Sql = "SELECT count(*) FROM pg_constraint WHERE conname = 'ck_stock_no_negative';" }
    @{ Name = 'order cannot reserve twice'; Sql = "SELECT count(*) FROM pg_constraint WHERE conname = 'ux_reservations_order_sku';" }
    @{ Name = 'reservation quantity > 0';   Sql = "SELECT count(*) FROM pg_constraint WHERE conname = 'ck_reservations_quantity';" }
    @{ Name = 'sweep can find expiries';   Sql = "SELECT count(*) FROM pg_indexes WHERE tablename = 'reservations' AND indexdef LIKE '%expires_at%';" }
)

foreach ($c in $checks) {
    $r = Invoke-Sql $c.Sql

    if ($r -and [int]$r[0] -gt 0) {
        Ok $c.Name
    } else {
        Bad "$($c.Name) is missing"
    }
}

Write-Host ''

if ($problems.Count -eq 0) {
    Ok 'Schema matches the code.'
    exit 0
}

Bad 'Schema and code disagree. Run: docker compose down -v && .\build.ps1 -All'
exit 1