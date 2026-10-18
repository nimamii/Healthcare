# Catch the Directory.Build.props trap before it costs a debugging session.
#
#   .\check-props.ps1
#
# MSBuild searches upward from each project file for Directory.Build.props
# and Directory.Packages.props, and uses the FIRST match. A stray copy one
# level below the real one shadows it completely — the real settings are never
# read, and the build fails with "Root element is missing" pointing at a file
# that is perfectly valid where it sits.
#
# This repo has src/Directory.Build.props and src/Directory.Packages.props
# that exist only to stop the upward search. They must stay non-empty, or the
# build breaks in a way that points at the wrong file.

$ErrorActionPreference = 'Continue'

function Ok($m)   { Write-Host "  [ok]   $m" -ForegroundColor Green }
function Bad($m)  { Write-Host "  [fail] $m" -ForegroundColor Red }
function Warn($m) { Write-Host "  [warn] $m" -ForegroundColor Yellow }
function Head($m) { Write-Host "`n$m" -ForegroundColor Cyan }

$propNames = @('Directory.Build.props', 'Directory.Packages.props')
$problems = 0

Head 'Checking MSBuild property files'

$allProps = Get-ChildItem -Path . -Recurse -File -Include 'Directory.Build.props','Directory.Packages.props' `
    -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' }

foreach ($file in $allProps) {
    $relative = Resolve-Path -Relative $file.FullName
    $size = $file.Length

    # An empty file is the failure mode: MSBuild stops here and then cannot
    # parse it.
    if ($size -eq 0) {
        Bad "$relative is 0 bytes - this shadows the real file and breaks the build"
        $problems++
        continue
    }

    # Confirm it actually parses. A non-empty file with a broken root element
    # fails the same way, one step later.
    $content = Get-Content $file.FullName -Raw

    if ($content -notmatch '^\s*<\?xml' -and $content -notmatch '<Project[\s>]') {
        Bad "$relative has no <Project> root element"
        $problems++
        continue
    }

    $open  = ([regex]::Matches($content, '<Project[\s>]')).Count
    $close = ([regex]::Matches($content, '</Project>')).Count

    if ($open -ne $close) {
        Bad "$relative is unbalanced ($open open, $close close)"
        $problems++
        continue
    }

    # A shadowing file that neither defines settings nor imports the real one
    # stops MSBuild's upward search and leaves everything undefined. That is
    # the "TargetFramework value ''" failure, and it is silent: the file
    # parses fine.
    $isRoot = ($file.FullName -eq (Join-Path $PWD $file.Name))

    if (-not $isRoot) {
        $hasSettings = $content -match '<(PropertyGroup|PackageReference|ItemGroup)'
        $hasImport   = $content -match '<Import\s+Project='

        if (-not $hasSettings -and -not $hasImport) {
            Bad "$relative has neither settings nor an Import"
            Write-Host '        This shadows the root file and leaves every'
            Write-Host '        setting undefined. Add an Import of ../' + $file.Name
            $problems++
            continue
        }

        if ($hasImport -and -not $hasSettings) {
            Ok "$relative ($size bytes) - forwards to the root copy"
            continue
        }
    }

    Ok "$relative ($size bytes)"
}

Head 'Root files'

foreach ($name in $propNames) {
    if (Test-Path $name) {
        Ok "$name exists at the repository root"
    } else {
        Bad "$name is missing from the repository root"
        $problems++
    }
}

if ($problems -eq 0) {
    Write-Host ''
    Ok 'Property files look correct.'
    exit 0
}

Write-Host ''
Write-Host 'Why this matters' -ForegroundColor Cyan
Write-Host 'MSBuild uses the first Directory.Build.props it finds while walking'
Write-Host 'up from the project. An empty file under src/ stops that walk, so the'
Write-Host 'real settings above it are never applied.'
Write-Host ''
Write-Host 'To fix an empty one, give it valid content. Deleting it also works,'
Write-Host 'but then MSBuild finds the root copy, which is usually what you want.'
exit 1