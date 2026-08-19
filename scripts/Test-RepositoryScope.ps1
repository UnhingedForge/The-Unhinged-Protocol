[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gitRoot = (git -C $repositoryRoot rev-parse --show-toplevel).Trim().Replace('/', '\')

if (-not [string]::Equals($repositoryRoot, $gitRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Git root '$gitRoot' does not equal repository directory '$repositoryRoot'."
}

$parent = Split-Path $repositoryRoot -Parent
if (Test-Path -LiteralPath (Join-Path $parent '.git')) {
    throw "A parent-level Git repository exists at '$parent'."
}

if ((Split-Path $parent -Leaf) -eq 'DesktopOrganizer') {
    $expected = @('Documentation', 'Images', 'Releases', 'Source_Files')
    $actual = Get-ChildItem -LiteralPath $parent -Force | ForEach-Object Name
    $unexpected = $actual | Where-Object { $_ -notin $expected }
    $missing = $expected | Where-Object { $_ -notin $actual }
    if ($unexpected -or $missing) {
        throw "Workspace scope mismatch. Unexpected: '$($unexpected -join ', ')'; missing: '$($missing -join ', ')'."
    }
}

Write-Host "Repository scope passed: $repositoryRoot"
