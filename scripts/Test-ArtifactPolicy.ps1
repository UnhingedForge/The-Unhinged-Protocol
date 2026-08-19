[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$required = @(
    'src\TheUnhingedProtocol.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\TheUnhingedProtocol.App.dll',
    'src\TheUnhingedProtocol.App\bin\ARM64\Release\net10.0-windows10.0.26100.0\win-arm64\TheUnhingedProtocol.App.dll',
    'src\TheUnhingedProtocol.App\packages.lock.json',
    'src\TheUnhingedProtocol.Application\packages.lock.json',
    'src\TheUnhingedProtocol.Domain\packages.lock.json',
    'src\TheUnhingedProtocol.Infrastructure\packages.lock.json',
    'tests\TheUnhingedProtocol.Architecture.Tests\packages.lock.json',
    'tests\TheUnhingedProtocol.Domain.Tests\packages.lock.json'
)

foreach ($relativePath in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath) -PathType Leaf)) {
        throw "Required validation artifact is missing: $relativePath"
    }
}

$forbiddenTrackedExtensions = @('.pfx', '.p12', '.pem', '.key', '.msix', '.msixbundle', '.appx', '.appxbundle')
$tracked = git -C $repositoryRoot ls-files
foreach ($relativePath in $tracked) {
    if ([IO.Path]::GetExtension($relativePath) -in $forbiddenTrackedExtensions) {
        throw "Forbidden signing/distribution artifact is tracked: $relativePath"
    }
}

Write-Host 'Phase 0 artifact policy passed.'
