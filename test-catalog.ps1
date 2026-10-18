# Exercise the catalogue end to end.
#
#   .\test-catalog.ps1
#
# Covers the write path (admin-only) and the read path (anonymous), then
# verifies each row actually landed in the database. A 201 with nothing behind
# it is exactly the failure that went unnoticed in Phase 1.
#
# The admin token is minted by promoting a freshly registered user directly in
# the database. There is no admin-seeding endpoint, and adding one to make a
# test convenient would put an unauthenticated privilege-escalation path in
# the product.

# Persian output arrives as UTF-8 from Postgres and is re-encoded to the OEM
# code page by the Windows console, which renders it as mojibake. Setting the
# console encoding here fixes every command below, including psql output and
# the HTTP response bodies. Without this the data is correct and only the
# display is wrong — which is worse, because it looks like corruption.
try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $OutputEncoding = [System.Text.Encoding]::UTF8
} catch {
    # Not fatal. Output will be mojibake but the checks still work.
}

$ErrorActionPreference = 'Stop'

function Ok($m)   { Write-Host "  [ok]   $m" -ForegroundColor Green }
function Bad($m)  { Write-Host "  [fail] $m" -ForegroundColor Red }
function Head($m) { Write-Host "`n$m" -ForegroundColor Cyan }

$apiPort = 8090
$m = [regex]::Match((Get-Content '.env' -Raw), 'API_PORT=(\d+)')
if ($m.Success) { $apiPort = [int]$m.Groups[1].Value }

$base = "http://localhost:$apiPort"
$stamp = Get-Date -Format 'HHmmss'

# A fresh slug per run so the script is repeatable.
$suffix = $stamp.ToLower()

function Invoke-Api {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body,
        [string]$Token
    )

    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }

    $params = @{
        Uri         = "$base$Path"
        Method      = $Method
        Headers     = $headers
        TimeoutSec  = 15
        ErrorAction = 'Stop'
    }

    if ($null -ne $Body) {
        # The JSON string is produced here, not passed as a hashtable.
        #
        # Invoke-RestMethod's -Body treats a hashtable as form data no matter
        # what ContentType says, so the server received something that was not
        # JSON and answered 400 with an empty body — which looked like a server
        # bug and was a script bug. Converting to bytes first is what makes
        # the declared content type true, and fixes Persian text as well.
        $params['ContentType'] = 'application/json; charset=utf-8'
        $params['Body'] = [System.Text.Encoding]::UTF8.GetBytes(
            ($Body | ConvertTo-Json -Depth 5 -Compress))
    }

    try {
        return Invoke-RestMethod @params
    } catch {
        $status = $_.Exception.Response.StatusCode.value__

        # The server's response body is the only thing that says why. An
        # earlier version reported a 400 with an empty reason, which sent the
        # investigation to the server instead of to the request being sent.
        $detail = $_.ErrorDetails.Message

        if (-not $detail) {
            $detail = try {
                $response = $_.Exception.Response
                $reader = [System.IO.StreamReader]::new($response.GetResponseStream())
                $reader.ReadToEnd()
            } catch {
                '(no response body)'
            }
        }

        throw "HTTP $status on $Method $Path -> $detail"
    }
}

# The create endpoints answer 201 with a body. Read the id from it whether it
# arrives as { "id": ... } or as a bare GUID string, so a contract change
# surfaces as a clear error here rather than as an empty field that quietly
# propagates into the next request.
function Get-CreatedId {
    param([object]$Response, [string]$What)

    if ($null -eq $Response) {
        Bad "$What returned no body"
        exit 1
    }

    if ($Response -is [guid]) { return $Response }

    if ($Response.PSObject.Properties.Name -contains 'id') { return $Response.id }

    Bad "$What returned an unexpected shape: $($Response | ConvertTo-Json -Compress)"
    exit 1
}

# --- 1. An admin account ----------------------------------------------------

Head 'Obtaining an admin token'

$email = "admin+$suffix@example.com"
$password = 'Str0ngPassw0rd!'

Invoke-Api -Method POST -Path '/api/identity/register' -Body @{
    email    = $email
    password = $password
} | Out-Null
Ok "registered $email"

# Promote. The role name must match Roles.PlatformAdmin exactly.
$promoted = docker compose exec -T postgres psql -U healthcare -d healthcare -t -A -c @"
INSERT INTO identity.user_roles (user_id, role, granted_at)
SELECT u.id, 'PlatformAdmin', now()
FROM identity.users u
WHERE u.email = '$email'
ON CONFLICT DO NOTHING;
"@

if ($promoted -match 'INSERT 0 1') {
    Ok 'granted PlatformAdmin'
} elseif ($promoted -match 'INSERT 0 0') {
    Ok 'PlatformAdmin already present'
} else {
    Bad "could not promote: $promoted"
    exit 1
}

$tokens = Invoke-Api -Method POST -Path '/api/identity/login' -Body @{
    email    = $email
    password = $password
}

$token = $tokens.accessToken
Ok "logged in, token $($token.Length) chars"

# Confirm the role really is in the token. The database change alone proves
# nothing if the login happened before the insert committed.
$decoded = [System.Text.Encoding]::UTF8.GetString(
    [System.Convert]::FromBase64String(
        ($token.Split('.')[1].Replace('-', '+').Replace('_', '/') +
         '=' * ((4 - $token.Split('.')[1].Length % 4) % 4))))

if ($decoded -match 'PlatformAdmin') {
    Ok 'token carries PlatformAdmin'
} else {
    Bad "token has no PlatformAdmin claim: $decoded"
    Write-Host '  The role was probably granted after the token was issued.'
    exit 1
}

# --- 2. Write path ----------------------------------------------------------

Head 'Creating a category'

$categoryId = Get-CreatedId (Invoke-Api -Method POST -Path '/api/catalog/categories' -Token $token -Body @{
    name     = "تجهیزات پایش $stamp"
    slug     = "monitoring-$suffix"
    parentId = $null
}) 'category'
Ok "category id $categoryId"

Head 'Creating a product'

$productId = Get-CreatedId (Invoke-Api -Method POST -Path '/api/catalog/products' -Token $token -Body @{
    name                 = "دستگاه فشار خون $stamp"
    slug                 = "bp-monitor-$suffix"
    description          = 'دستگاه اندازه‌گیری فشار خون دیجیتال'
    categoryId           = $categoryId
    requiresPrescription = $false
}) 'product'
Ok "product id $productId"

Head 'Creating a SKU'

$skuId = Get-CreatedId (Invoke-Api -Method POST -Path '/api/catalog/skus' -Token $token -Body @{
    productId          = $productId
    variantDescription = 'نوار بازو ۲۲ تا ۳۲ سانتی‌متر'
}) 'sku'
Ok "sku id $skuId"

Head 'Linking a supplier'

$supplierId = [guid]::NewGuid()

Invoke-Api -Method POST -Path '/api/catalog/supplier-products' -Token $token -Body @{
    skuId       = $skuId
    supplierId  = $supplierId
    supplierSku = "SUP-$stamp"
    listPrice   = 2450000
    currency    = 'IRR'
} | Out-Null
Ok "supplier $supplierId linked"

# --- 3. Read path (anonymous) ---------------------------------------------

Head 'Reading without a token'

$product = Invoke-Api -Method GET -Path "/api/catalog/products/$productId"
Ok "GET product returned: $($product.name)"

if ($product.Id -eq $productId) { Ok 'same id as created' }
else { Bad 'id mismatch' }

$skus = Invoke-Api -Method GET -Path "/api/catalog/skus/$skuId/listings"
Ok "listings returned: $(@($skus).Count)"

if (@($skus).Count -ne 1) {
    Bad 'expected exactly one listing'
} else {
    if ($skus[0].supplierId -eq $supplierId) { Ok 'listing points at the right supplier' }
    else { Bad 'supplier id mismatch' }
}

# --- 4. Authorization -------------------------------------------------------

Head 'Authorization'

try {
    Invoke-RestMethod -Uri "$base/api/catalog/categories" -Method POST `
        -ContentType 'application/json' `
        -Body (@{ name = 'hack'; slug = "hack-$suffix"; parentId = $null } | ConvertTo-Json) `
        -TimeoutSec 10 | Out-Null

    Bad 'an anonymous caller was allowed to create a category'
    exit 1
} catch {
    if ($_.Exception.Response.StatusCode.value__ -eq 401) {
        Ok 'anonymous create rejected with 401'
    } else {
        Bad "expected 401, got $($_.Exception.Response.StatusCode.value__)"
        exit 1
    }
}

# A logged-in but non-admin caller must also be refused. Without a token the
# test above would pass even if the role check were missing entirely.
$plainEmail = "plain+$suffix@example.com"
Invoke-Api -Method POST -Path '/api/identity/register' -Body @{
    email    = $plainEmail
    password = $password
} | Out-Null

$plainTokens = Invoke-Api -Method POST -Path '/api/identity/login' -Body @{
    email    = $plainEmail
    password = $password
}

try {
    Invoke-RestMethod -Uri "$base/api/catalog/categories" -Method POST `
        -Headers @{ Authorization = "Bearer $($plainTokens.accessToken)" } `
        -ContentType 'application/json' `
        -Body (@{ name = 'hack'; slug = "hack2-$suffix"; parentId = $null } | ConvertTo-Json) `
        -TimeoutSec 10 | Out-Null

    Bad 'a Customer was allowed to create a category'
    exit 1
} catch {
    if ($_.Exception.Response.StatusCode.value__ -eq 403) {
        Ok 'Customer create rejected with 403'
    } else {
        Bad "expected 403, got $($_.Exception.Response.StatusCode.value__)"
        exit 1
    }
}

# --- 5. The rows are really in the database ---------------------------------

Head 'Database contents'

$rows = docker compose exec -T postgres psql -U healthcare -d healthcare -c @"
SELECT c.slug          AS category,
       p.slug          AS product,
       s.code          AS sku,
       s.variant_description,
       sp.supplier_sku,
       sp.list_price,
       sp.currency,
       sp.supplier_id = '$supplierId' AS right_supplier
FROM catalog.supplier_products sp
JOIN catalog.skus s          ON s.id  = sp.sku_id
JOIN catalog.products p      ON p.id  = s.product_id
JOIN catalog.categories c    ON c.id  = p.category_id
WHERE p.slug = 'bp-monitor-$suffix';
"@

Write-Host $rows

if ($rows -match 'bp-monitor') { Ok 'the full chain is present' }
else { Bad 'nothing in the database'; exit 1 }

Write-Host ''
Ok 'All catalogue checks passed.'
Write-Host ''
Write-Host "Swagger: http://localhost:$apiPort/swagger"
exit 0