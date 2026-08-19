[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$approvedPatterns = @(
    '^CommunityToolkit\.Mvvm$',
    '^coverlet\.collector$',
    '^Microsoft\.',
    '^SQLite$',
    '^SQLitePCLRaw\.',
    '^System\.Numerics\.Tensors$',
    '^xunit\.'
)

$packageNames = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)

Get-ChildItem -Path $repositoryRoot -Recurse -Filter packages.lock.json | ForEach-Object {
    $lock = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
    foreach ($framework in $lock.dependencies.PSObject.Properties.Value) {
        foreach ($package in $framework.PSObject.Properties) {
            if ($package.Value.type -ne 'Project') {
                [void]$packageNames.Add($package.Name)
            }
        }
    }
}

$unapproved = $packageNames | Where-Object {
    $name = $_
    -not ($approvedPatterns | Where-Object { $name -match $_ })
}

if ($unapproved) {
    throw "Unreviewed dependencies found: $($unapproved -join ', ')"
}

$forbiddenProductionPackagePatterns = @(
    '^Microsoft\.ApplicationInsights$',
    '^Microsoft\.Testing\.Extensions\.Telemetry$',
    '^Microsoft\.WindowsAppSDK\.(AI|Widgets|ML|Search)$',
    '^Microsoft\.Windows\.AI\.'
)

$productionLocks = Get-ChildItem -Path (Join-Path $repositoryRoot 'src') -Recurse -Filter packages.lock.json
foreach ($lockFile in $productionLocks) {
    $lock = Get-Content -LiteralPath $lockFile.FullName -Raw | ConvertFrom-Json
    foreach ($framework in $lock.dependencies.PSObject.Properties.Value) {
        foreach ($package in $framework.PSObject.Properties) {
            foreach ($forbiddenPattern in $forbiddenProductionPackagePatterns) {
                if ($package.Name -match $forbiddenPattern) {
                    throw "Later-phase or telemetry dependency '$($package.Name)' found in $($lockFile.FullName)."
                }
            }
        }
    }
}

Write-Host "Dependency policy passed for $($packageNames.Count) resolved packages."
