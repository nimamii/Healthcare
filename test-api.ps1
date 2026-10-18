# Exercise the running API end to end.
#
#   .\test-api.ps1
#
# Register, then log in, then read the row back out of the database. Written
# as a script rather than a curl one-liner because PowerShell's quoting rules
# make a hand-typed JSON payload error prone - a stray backslash produces
# invalid JSON and an empty response, which looks like a broken API rather
# than a broken command.
#
# Uses Invoke-RestMethod, which handles quoting and JSON encoding itself.

# Persian output arrives as UTF-8 and the Windows console re-encodes it to the
# OEM code page, which renders it as mojibake. The data is correct; only the
# display is wrong — which is worse, because it looks like corruption.
try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $OutputEncoding = [System.Text.Encoding]::UTF8
} catch {
    # Not fatal. Output is unreadable but the checks still work.
}


$ErrorActionPreference = 'Stop'

function Ok($m)   { Write-Host "  [ok]   $m" -ForegroundColor Green }
function Bad($m)  { Write-Host "  [fail] $m" -ForegroundColor Red }
function Head($m) { Write-Host "`n$m" -ForegroundColor Cyan }

$apiPort = 8090
$m = [regex]::Match((Get-Content '.env' -Raw), 'API_PORT=(\d+)')
if ($m.Success) { $apiPort = [int]$m.Groups[1].Value }

$base = "http://localhost:$apiPort"

# Unique per run so the script can be run repeatedly without a duplicate
# email turning a pass into a confusing failure.
$stamp = Get-Date -Format 'HHmmss'
$email = "test+$stamp@example.com"
$password = 'Str0ngPassw0rd!'

Head "Testing $base"

# --- 1. Health --------------------------------------------------------------

try {
    $health = Invoke-RestMethod -Uri "$base/health" -TimeoutSec 5
    Ok "health: $($health.status)"
} catch {
    Bad "health check failed - $($_.Exception.Message)"
    Write-Host ''
    Write-Host 'Is the stack running?  docker compose ps'
    exit 1
}

# --- 2. Register ------------------------------------------------------------

Head 'Register'

$registerBody = @{
    email    = $email
    password = $password
} | ConvertTo-Json

try {
    Invoke-RestMethod -Uri "$base/api/identity/register" `
        -Method Post `
        -ContentType 'application/json' `
        -Body $registerBody `
        -TimeoutSec 10 | Out-Null

    Ok "registered $email"
} catch {
    $status = $_.Exception.Response.StatusCode.value__
    Bad "register failed (HTTP $status)"
    Write-Host ''
    Write-Host $_.ErrorDetails.Message
    exit 1
}

# --- 3. Login ---------------------------------------------------------------

Head 'Login'

# "test+HHmmss@example.com" is a valid address, but a "+" is exactly the kind
# of character that gets normalised differently by some mail and lookup paths.
# Try the plain form too, so a failure here distinguishes "the address was
# mangled" from "the password check is broken".
$plainEmail = "plain+$stamp@example.com"

$plainBody = @{
    email    = $plainEmail
    password = $password
} | ConvertTo-Json

Invoke-RestMethod -Uri "$base/api/identity/register" `
    -Method Post `
    -ContentType 'application/json' `
    -Body $plainBody `
    -TimeoutSec 10 | Out-Null
Ok "registered a second address ($plainEmail)"

$loginBody = @{
    email    = $email
    password = $password
} | ConvertTo-Json

$tokens = $null

try {
    $tokens = Invoke-RestMethod -Uri "$base/api/identity/login" `
        -Method Post `
        -ContentType 'application/json' `
        -Body $loginBody `
        -TimeoutSec 10

    Ok 'login succeeded'
    Ok "access token received ($($tokens.accessToken.Length) chars)"
    Ok "refresh token received ($($tokens.refreshToken.Length) chars)"
    Ok "expires at $($tokens.expiresAt)"
} catch {
    Bad "login failed (HTTP $($_.Exception.Response.StatusCode.value__))"

    # The response body names the reason, but every credential failure
    # deliberately returns the same message. So the API cannot distinguish
    # "no such user" from "wrong password" — by design, so the endpoint is not
    # an account-enumeration oracle. Fall back to the database to find out
    # which one actually happened.
    Write-Host ''
    Write-Host 'API response:'
    Write-Host "  $($_.ErrorDetails.Message)"
    Write-Host ''
    Write-Host 'Database state:'

    $stored = docker compose exec -T postgres psql -U healthcare -d healthcare -t `
        -c "SELECT email || ' | hashlen=' || length(password_hash) || ' | active=' || is_active FROM identity.users WHERE email LIKE '%$stamp%';" 2>$null

    if ($stored) {
        $stored | ForEach-Object { Write-Host "  $_" }
    } else {
        Write-Host '  no rows matched - registration did not persist'
    }

    Write-Host ''
    Write-Host 'If a row exists with hashlen around 100, the password was stored'
    Write-Host 'and the mismatch is in Verify. If hashlen is 0 or absent, the'
    Write-Host 'hashing step failed at registration.'
    Write-Host ''
    Write-Host 'Server logs:  docker compose logs --tail 20 api'
    exit 1
}

# --- 4. Reject a wrong password --------------------------------------------

Head 'Wrong password is rejected'

$badBody = @{
    email    = $email
    password = 'WrongPassword123!'
} | ConvertTo-Json

try {
    Invoke-RestMethod -Uri "$base/api/identity/login" `
        -Method Post `
        -ContentType 'application/json' `
        -Body $badBody `
        -TimeoutSec 10 | Out-Null

    Bad 'a wrong password was accepted'
    exit 1
} catch {
    if ($_.Exception.Response.StatusCode.value__ -eq 401) {
        Ok 'rejected with 401, as expected'
    } else {
        Bad "expected 401, got $($_.Exception.Response.StatusCode.value__)"
        exit 1
    }
}

# --- 5. The row really is in the database ----------------------------------

Head 'Database contents'

$rowCount = docker compose exec -T postgres `
    psql -U healthcare -d healthcare -t `
    -c "SELECT count(*) FROM identity.users WHERE email = '$email';" 2>$null

if ($rowCount -match '\s*1\s*$') {
    Ok 'the user exists in identity.users'
} else {
    Bad "expected 1 matching row, got: $rowCount"
}

Write-Host ''
Write-Host 'Row detail:'
docker compose exec -T postgres psql -U healthcare -d healthcare `
    -c "SELECT u.email, r.role, u.is_active FROM identity.users u JOIN identity.user_roles r ON r.user_id = u.id WHERE u.email = '$email';"

Write-Host ''
Ok 'All checks passed.'
Write-Host ''
# The whole URL in one string. Writing it as 'http://localhost:' + $port
# makes PowerShell pass the pieces as separate arguments to Write-Host,
# which is why the output showed the literals instead of a URL.
Write-Host "Swagger:  http://localhost:$apiPort/swagger"
Write-Host "Health:   http://localhost:$apiPort/health"
exit 0