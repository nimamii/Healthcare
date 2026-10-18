# Find the real PostGIS package name for this postgres base.
#
#   .\probe-postgis-package.ps1
#
# Guessing package names wasted one build already. This asks the repository
# directly, inside the same base image the build uses, so the answer is the one
# that will actually resolve.

$ErrorActionPreference = 'Continue'

$probe = @'
FROM public.ecr.aws/docker/library/postgres:16

# Which postgres major versions does this base carry, and what postgis
# packages exist for them?
RUN set -eux; \
    apt-get update; \
    echo "--- pgdg suite:"; \
    cat /etc/apt/sources.list.d/*.list 2>/dev/null || true; \
    echo "--- installed postgres version:"; \
    ls /usr/lib/postgresql/; \
    echo "--- available postgis packages:"; \
    apt-cache search postgis | head -40
'@

$tmp = Join-Path $env:TEMP 'Dockerfile.postgis-probe'
[System.IO.File]::WriteAllText($tmp, $probe)

docker build --progress=plain --no-cache -f $tmp -t postgis-probe:tmp . 2>&1 |
    Select-String -Pattern 'pgdg suite|postgres|postgis|Installed|deb ' |
    ForEach-Object { $_.Line }

Remove-Item $tmp -ErrorAction SilentlyContinue
docker rmi postgis-probe:tmp -f 2>$null | Out-Null

Write-Host ''
Write-Host 'Read the output above: the postgis package names are the answer.'
Write-Host 'If none appeared, apt-get update did not complete inside the build.'
