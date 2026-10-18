# Find out what Docker actually receives as build context.
#
#   .\debug-context.ps1
#
# The build failed with "Directory.Build.props - Root element is missing",
# but that file is valid on disk. The build log showed a context of only
# 4.22kB, when the source alone is ~83kB — so something is filtering the
# context, and guessing at it has already cost several wrong fixes.
#
# This asks Docker directly instead of inferring.

$ErrorActionPreference = 'Continue'

function Head($m) { Write-Host "`n$m" -ForegroundColor Cyan }

Write-Host "`nWhat Docker sees in the build context" -ForegroundColor Cyan

# `docker build` prints the transferred context size. Creating the context
# with an unbuildable Dockerfile isolates the context step from the build.
$tempDockerfile = Join-Path $env:TEMP 'ctx-probe.Dockerfile'

@'
FROM busybox
# Intentionally never runs; this only measures the context.
RUN echo "context accepted"
'@ | Set-Content $tempDockerfile

Write-Host "`n1. With the project's .dockerignore:" -ForegroundColor Cyan
docker build -f $tempDockerfile -t ctx-probe:project .
if ($LASTEXITCODE -eq 0) { Write-Host '   (built OK)' }

Write-Host "`n2. With an empty .dockerignore (shows the true repo size):" -ForegroundColor Cyan
docker build -f $tempDockerfile -t ctx-probe:full -f $tempDockerfile .
if ($LASTEXITCODE -eq 0) { Write-Host '   (built OK)' }

# Compare: temporarily move .dockerignore aside.
Write-Host "`n3. Temporarily moving .dockerignore aside:" -ForegroundColor Cyan

if (Test-Path '.dockerignore') {
    Move-Item '.dockerignore' '.dockerignore.bak' -Force

    Write-Host '   building without .dockerignore...'
    docker build -f $tempDockerfile -t ctx-probe:nofilter .
    $fullResult = $LASTEXITCODE

    Move-Item '.dockerignore.bak' '.dockerignore' -Force
    Write-Host '   .dockerignore restored'
}

Write-Host "`n4. Does the context actually contain the props file?" -ForegroundColor Cyan
Write-Host '   Probing by copying only that file:'
@'
FROM busybox
COPY Directory.Build.props /probe.props
RUN wc -c /probe.props
'@ | Set-Content $tempDockerfile

docker build -f $tempDockerfile -t ctx-probe:props .
if ($LASTEXITCODE -eq 0) {
    Write-Host '   Directory.Build.props IS in the context'
} else {
    Write-Host '   Directory.Build.props is NOT reaching the context'
}

Write-Host "`n5. And the C# sources?" -ForegroundColor Cyan
@'
FROM busybox
COPY src /probe-src
RUN find /probe-src -name "*.cs" | wc -l
'@ | Set-Content $tempDockerfile

docker build -f $tempDockerfile -t ctx-probe:src .
if ($LASTEXITCODE -eq 0) {
    Write-Host '   src/ IS in the context'
} else {
    Write-Host '   src/ is NOT reaching the context'
}

Remove-Item $tempDockerfile -ErrorAction SilentlyContinue
docker rmi ctx-probe:project ctx-probe:full ctx-probe:nofilter ctx-probe:props ctx-probe:src -f 2>$null | Out-Null

Write-Host "`nCompare the context sizes reported above. A build with"
Write-Host '.dockerignore far smaller than one without means the ignore file'
Write-Host 'is excluding more than intended.'