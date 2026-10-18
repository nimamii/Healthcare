# Add a module to the solution, correctly.
#
#   .\add-module.ps1 -Name Inventory
#   .\add-module.ps1 -Name Inventory -WithTests
#
# Editing Healthcare.sln by hand is a reliable way to produce "It is nested
# under {guid} but that project is not found in the solution" - an error that
# points at the solution while the real mistake was one missing folder line.
#
# This creates the folder, the project entries, their configurations, the
# nesting, AND the .csproj files, then verifies the result. Creating the
# csproj files matters because a project entry pointing at a missing file
# fails restore with a different, equally confusing message.

param(
    [Parameter(Mandatory = $true)]
    [string]$Name,

    # Also create a {Name}.Tests project and register it.
    [switch]$WithTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$sln = Join-Path $root 'Healthcare.sln'

function Ok($m)   { Write-Host "  [ok]   $m" -ForegroundColor Green }
function Bad($m)  { Write-Host "  [fail] $m" -ForegroundColor Red }
function Warn($m) { Write-Host "  [warn] $m" -ForegroundColor Yellow }

$name = $Name.Trim()

Write-Host "`nAdding module: $name" -ForegroundColor Cyan

if ($name -notmatch '^[A-Z][A-Za-z0-9]*$') {
    Bad "Invalid name. Use PascalCase with no spaces (got: '$name')"
    exit 1
}

# --- Read the solution ------------------------------------------------------

$lines = [System.Collections.Generic.List[string]](Get-Content $sln)

if ($lines | Where-Object { $_ -match '=\s*"' + $name + '"' }) {
    Bad "$name is already referenced in the solution. Nothing changed."
    exit 1
}

# A deterministic GUID from the name, so re-running cannot create duplicates.
$seed = 0
foreach ($c in $name.ToCharArray()) { $seed = ($seed * 31 + [int]$c) % 100000 }

$folderGuid = '{B2000000-0000-0000-0000-' + $seed.ToString('D12') + '}'

# --- Build the entries ------------------------------------------------------

$layers = @('Domain', 'Contracts', 'Application', 'Infrastructure', 'Api')

$projectLines = [System.Collections.Generic.List[string]]::new()
$configLines = [System.Collections.Generic.List[string]]::new()
$nestingLines = [System.Collections.Generic.List[string]]::new()
$guids = [System.Collections.Generic.List[string]]::new()

$projectLines.Add('Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "' + $name + '", "src\Modules\' + $name + '", "' + $folderGuid + '"')
$projectLines.Add('EndProject')
$projectLines.Add('')

# Domain first, then Contracts: order in the file is cosmetic but this reads
# in dependency order.
$order = @('Domain', 'Contracts', 'Application', 'Infrastructure', 'Api')

for ($i = 0; $i -lt $order.Count; $i++) {
    $layer = $order[$i]
    $guid = '{A2' + $seed.ToString('D4') + '-' + $i.ToString('D4') + '-0000-000000000000}'
    $projectName = "$name.$layer"

    # -f formatting, not string concatenation: the latter silently keeps
    # $name literal inside single quotes, which produced solution entries
    # pointing at "src/Modules/$name/$projectName/$projectName.csproj".
    $projectLines.Add(('Project("{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}") = "{0}", "src\Modules\{1}\{0}\{0}.csproj", "{2}"' -f $projectName, $name, $guid))
    $projectLines.Add('EndProject')
    $projectLines.Add('')

    $configLines.Add("`t`t$guid.Debug|Any CPU.ActiveCfg = Debug|Any CPU")
    $configLines.Add("`t`t$guid.Debug|Any CPU.Build.0 = Debug|Any CPU")
    $configLines.Add("`t`t$guid.Release|Any CPU.ActiveCfg = Release|Any CPU")
    $configLines.Add("`t`t$guid.Release|Any CPU.Build.0 = Release|Any CPU")

    $nestingLines.Add("`t`t$guid = $folderGuid")
    $guids.Add($guid)
}

# --- Insert project entries -------------------------------------------------

$globalIndex = $lines.IndexOf('Global')
if ($globalIndex -lt 0) {
    Bad 'Could not find the Global section in the solution'
    exit 1
}

$lines.InsertRange($globalIndex, $projectLines)
Ok "added $($guids.Count) project entries and the $name folder"

# --- Configurations ---------------------------------------------------------

function Find-SectionEnd {
    param([System.Collections.Generic.List[string]]$Lines, [string]$SectionName)

    $inSection = $false

    for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i] -match "GlobalSection\($SectionName\)") { $inSection = $true; continue }
        if ($inSection -and $Lines[$i] -match '^\tEndGlobalSection') { return $i }
    }

    return -1
}

$configEnd = Find-SectionEnd -Lines $lines -SectionName 'ProjectConfigurationPlatforms'
if ($configEnd -lt 0) { Bad 'Could not find the end of ProjectConfigurationPlatforms'; exit 1 }

$lines.InsertRange($configEnd, $configLines)
Ok 'added build configurations'

# --- Nesting ----------------------------------------------------------------

$nestingEnd = Find-SectionEnd -Lines $lines -SectionName 'NestedProjects'
if ($nestingEnd -lt 0) { Bad 'Could not find the end of NestedProjects'; exit 1 }

$lines.InsertRange($nestingEnd, $nestingLines)
Ok 'added nesting'

[System.IO.File]::WriteAllLines($sln, $lines)

# --- csproj files -----------------------------------------------------------

# Created here rather than left for the caller. A solution entry pointing at a
# missing file fails restore, and the error names the file rather than the
# missing entry.

function New-Csproj {
    param([string]$Path, [string]$RootNamespace, [string[]]$ProjectRefs, [string[]]$Packages, [string[]]$FrameworkRefs = @())

    $dir = Split-Path -Parent $Path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    $xml = "<Project Sdk=`"Microsoft.NET.Sdk`">`n`n"
    $xml += "  <PropertyGroup>`n"
    $xml += "    <RootNamespace>$RootNamespace</RootNamespace>`n"
    $xml += "  </PropertyGroup>`n"

    if ($ProjectRefs.Count -gt 0) {
        $xml += "`n  <ItemGroup>`n"
        foreach ($r in $ProjectRefs) { $xml += "    <ProjectReference Include=`"$r`" />`n" }
        $xml += "  </ItemGroup>`n"
    }

    if ($FrameworkRefs.Count -gt 0) {
        $xml += "`n  <ItemGroup>`n"
        foreach ($f in $FrameworkRefs) { $xml += "    <FrameworkReference Include=`"$f`" />`n" }
        $xml += "  </ItemGroup>`n"
    }

    if ($Packages.Count -gt 0) {
        $xml += "`n  <ItemGroup>`n"
        foreach ($p in $Packages) { $xml += "    <PackageReference Include=`"$p`" />`n" }
        $xml += "  </ItemGroup>`n"
    }

    $xml += "`n</Project>`n"

    [System.IO.File]::WriteAllText($Path, $xml)
}

$sharedKernel = '..\..\..\BuildingBlocks\SharedKernel\SharedKernel.csproj'
$base = "src\Modules\$name"

New-Csproj -Path (Join-Path $root "$base\$name.Domain\$name.Domain.csproj") `
    -RootNamespace "Healthcare.Modules.$name.Domain" `
    -ProjectRefs @($sharedKernel)

New-Csproj -Path (Join-Path $root "$base\$name.Contracts\$name.Contracts.csproj") `
    -RootNamespace "Healthcare.Modules.$name.Contracts" `
    -ProjectRefs @()

New-Csproj -Path (Join-Path $root "$base\$name.Application\$name.Application.csproj") `
    -RootNamespace "Healthcare.Modules.$name.Application" `
    -ProjectRefs @("..\$name.Domain\$name.Domain.csproj", $sharedKernel) `
    -Packages @('Microsoft.Extensions.DependencyInjection.Abstractions')

New-Csproj -Path (Join-Path $root "$base\$name.Infrastructure\$name.Infrastructure.csproj") `
    -RootNamespace "Healthcare.Modules.$name.Infrastructure" `
    -ProjectRefs @("..\$name.Application\$name.Application.csproj",
                   "..\$name.Domain\$name.Domain.csproj",
                   "..\$name.Contracts\$name.Contracts.csproj",
                   $sharedKernel) `
    -Packages @('Microsoft.EntityFrameworkCore',
                'Microsoft.EntityFrameworkCore.Relational',
                'Npgsql.EntityFrameworkCore.PostgreSQL',
                'Microsoft.Extensions.Configuration.Abstractions',
                'Microsoft.Extensions.DependencyInjection.Abstractions',
                'Microsoft.Extensions.Hosting.Abstractions',
                'Microsoft.Extensions.Logging.Abstractions')

New-Csproj -Path (Join-Path $root "$base\$name.Api\$name.Api.csproj") `
    -RootNamespace "Healthcare.Modules.$name.Api" `
    -ProjectRefs @("..\$name.Application\$name.Application.csproj",
                   "..\$name.Contracts\$name.Contracts.csproj",
                   $sharedKernel) `
    -FrameworkRefs @('Microsoft.AspNetCore.App') `
    -Packages @('FluentValidation')

Ok "created 5 csproj files under $base"

# --- Tests ------------------------------------------------------------------

if ($WithTests) {
    $testsFolder = 'tests'
    $testProject = "$name.Tests"
    $testGuid = '{A4' + $seed.ToString('D4') + '-0000-0000-000000000000}'

    $lines = [System.Collections.Generic.List[string]](Get-Content $sln)

    $entry = 'Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "' + $testProject + '", "tests\' + $testProject + '\' + $testProject + '.csproj", "' + $testGuid + '"'
    $lines.Insert($lines.IndexOf('Global'), $entry)
    $lines.Insert($lines.IndexOf('Global') + 1, 'EndProject')

    $cfg = [System.Collections.Generic.List[string]]::new()
    $cfg.Add("`t`t$testGuid.Debug|Any CPU.ActiveCfg = Debug|Any CPU")
    $cfg.Add("`t`t$testGuid.Debug|Any CPU.Build.0 = Debug|Any CPU")
    $cfg.Add("`t`t$testGuid.Release|Any CPU.ActiveCfg = Release|Any CPU")
    $cfg.Add("`t`t$testGuid.Release|Any CPU.Build.0 = Release|Any CPU")
    $lines.InsertRange((Find-SectionEnd -Lines $lines -SectionName 'ProjectConfigurationPlatforms'), $cfg)

    [System.IO.File]::WriteAllLines($sln, $lines)

    New-Csproj -Path (Join-Path $root "tests\$testProject\$testProject.csproj") `
        -RootNamespace "Healthcare.Tests.$name" `
        -ProjectRefs @("..\..\src\Modules\$name\$name.Domain\$name.Domain.csproj",
                       "..\..\src\Modules\$name\$name.Infrastructure\$name.Infrastructure.csproj",
                       '..\..\src\BuildingBlocks\SharedKernel\SharedKernel.csproj') `
        -Packages @('Microsoft.NET.Test.Sdk', 'xunit', 'xunit.runner.visualstudio', 'FluentAssertions')

    # Add the test markers so `dotnet test` picks it up.
    $projPath = Join-Path $root "tests\$testProject\$testProject.csproj"
    $content = [System.IO.File]::ReadAllText($projPath)
    $content = $content.Replace(
        '    <RootNamespace>',
        "    <IsPackable>false</IsPackable>`n    <IsTestProject>true</IsTestProject>`n    <RootNamespace>")
    [System.IO.File]::WriteAllText($projPath, $content)

    Ok "created $testProject"
}

# --- Verify -----------------------------------------------------------------

Write-Host "`nVerifying" -ForegroundColor Cyan

$problems = @()

$text = [System.IO.File]::ReadAllText($sln)

# Every nesting target must be declared.
$declared = @{}
foreach ($l in $text -split "`n") {
    if ($l -match 'Project\("\{[0-9A-F-]+\}"\)\s*=\s*"[^"]+",\s*"[^"]*",\s*"\{([0-9A-F-]+)\}"') {
        $declared[$Matches[1]] = $true
    }
}

$nested = [regex]::Matches($text, '(?m)^\t\t\{[0-9A-F-]+\}\s*=\s*\{([0-9A-F-]+)\}')
foreach ($m in $nested) {
    if (-not $declared.ContainsKey($m.Groups[1].Value)) {
        $problems += "nesting target $($m.Groups[1].Value) is not declared"
    }
}

# Every referenced csproj must exist.
foreach ($m in [regex]::Matches($text, 'csproj",\s*"\{[0-9A-F-]+\}"')) { }

foreach ($m in [regex]::Matches($text, '(?m)^Project\("\{[0-9A-F-]+\}"\)\s*=\s*"[^"]+",\s*"([^"]+\.csproj)"')) {
    $p = Join-Path $root ($m.Groups[1].Value -replace '\\', '/')
    if (-not (Test-Path $p)) {
        $problems += "referenced but missing: $($m.Groups[1].Value)"
    }
}

if ($problems.Count -gt 0) {
    foreach ($p in $problems) { Bad $p }
    Write-Host ''
    Write-Host 'Revert Healthcare.sln before continuing.' -ForegroundColor Yellow
    exit 1
}

Ok 'solution is internally consistent'

Write-Host ''
Write-Host "Remaining steps for $name" -ForegroundColor Cyan
Write-Host ''
Write-Host "  1. write the domain types under src\Modules\$name\$name.Domain\"
Write-Host "  2. UnitOfWorkKeys in src\BuildingBlocks\SharedKernel\IUnitOfWork.cs"
Write-Host "     Add: public const string $name = `"$($name.ToLower())`";"
Write-Host '     Every module needs a key, or DI resolves the wrong context and'
Write-Host '     writes are silently discarded.'
Write-Host ''
Write-Host "  3. tables in db\init\001-schemas.sql under schema `"$($name.ToLower())`""
Write-Host '     The file only runs on a fresh volume: docker compose down -v'
Write-Host ''
Write-Host '  4. COPY lines in the Dockerfile restore layer'
Write-Host '     Without these, dotnet restore fails on the missing project file.'
Write-Host ''
Write-Host "  5. ProjectReference lines in src\Healthcare.Api\Healthcare.Api.csproj"
Write-Host ''
Write-Host "  6. add new ${name}Module() to the IModule[] array in Program.cs"
Write-Host ''
Write-Host 'Do NOT run dotnet sln add - the entries are already there.'
exit 0