[CmdletBinding()]
param(
    [switch]$SkipAdvisoryQuery
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

Push-Location $repositoryRoot
try {
    & "$PSScriptRoot\Test-RepositoryScope.ps1"
    & "$PSScriptRoot\Test-Secrets.ps1"

    dotnet restore .\TheUnhingedProtocol.slnx --locked-mode
    $formatProjects = @(
        '.\src\TheUnhingedProtocol.Domain\TheUnhingedProtocol.Domain.csproj',
        '.\src\TheUnhingedProtocol.Application\TheUnhingedProtocol.Application.csproj',
        '.\src\TheUnhingedProtocol.Infrastructure\TheUnhingedProtocol.Infrastructure.csproj',
        '.\tests\TheUnhingedProtocol.Domain.Tests\TheUnhingedProtocol.Domain.Tests.csproj',
        '.\tests\TheUnhingedProtocol.Architecture.Tests\TheUnhingedProtocol.Architecture.Tests.csproj'
    )
    foreach ($project in $formatProjects) {
        dotnet format $project --verify-no-changes --no-restore --verbosity quiet
    }
    dotnet build .\TheUnhingedProtocol.slnx --configuration Release --no-restore -p:Platform=x64
    dotnet build .\src\TheUnhingedProtocol.App\TheUnhingedProtocol.App.csproj --configuration Release --no-restore -p:Platform=ARM64
    dotnet test .\TheUnhingedProtocol.slnx --configuration Release --no-build --no-restore -p:Platform=x64

    & "$PSScriptRoot\Test-DependencyPolicy.ps1"
    & "$PSScriptRoot\Test-ArtifactPolicy.ps1"

    if (-not $SkipAdvisoryQuery) {
        $advisoryJson = dotnet list .\TheUnhingedProtocol.slnx package --vulnerable --include-transitive --format json --output-version 1
        if ($advisoryJson -match '"severity"\s*:') {
            throw 'NuGet reported one or more vulnerable dependencies.'
        }
    }

    git diff --check
    if ($LASTEXITCODE -ne 0) {
        throw 'Git whitespace validation failed.'
    }

    Write-Host 'Phase 0 automated validation passed.'
}
finally {
    Pop-Location
}
