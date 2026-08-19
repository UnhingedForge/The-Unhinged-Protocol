[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$candidateFiles = git -C $repositoryRoot ls-files --cached --others --exclude-standard
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate repository files.'
}

$patterns = [ordered]@{
    'Private key' = '-----BEGIN (?:RSA|EC|OPENSSH|DSA) PRIVATE KEY-----'
    'GitHub token' = 'gh[pousr]_[A-Za-z0-9]{36,}'
    'AWS access key' = 'AKIA[0-9A-Z]{16}'
    'Generic assigned secret' = '(?i)(?:api[_-]?key|client[_-]?secret|access[_-]?token|password)\s*[:=]\s*["''][A-Za-z0-9_./+=-]{16,}["'']'
}

$findings = [System.Collections.Generic.List[string]]::new()
foreach ($relativePath in $candidateFiles) {
    $fullPath = Join-Path $repositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        continue
    }

    try {
        $content = Get-Content -LiteralPath $fullPath -Raw -ErrorAction Stop
    }
    catch {
        continue
    }

    foreach ($entry in $patterns.GetEnumerator()) {
        if ($content -match $entry.Value) {
            $findings.Add("$($entry.Key): $relativePath")
        }
    }
}

if ($findings.Count -gt 0) {
    $findings | ForEach-Object { Write-Error $_ }
    throw 'Potential secret material detected.'
}

Write-Host "Secret scan passed for $($candidateFiles.Count) repository files."
