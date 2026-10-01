$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $files = @(git -c core.quotepath=false diff --cached --name-only --diff-filter=ACMR)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read staged files.' }
    if ($files.Count -eq 0) { throw 'No staged files to review.' }
    $blocked = '(^|/)(bin|vendor|artifacts|dist|\.git|\.codex|\.tools|__pycache__)/|(^|/)(PLAN\.md|VALIDATION\.md|auth\.json|credentials\.json|settings\.json|sensors\.json|command\.json|error\.txt|self-test\.txt)$|(^|/)\.env($|\.)|\.(pfx|p12|pem|key|log|tmp|exe|dll|zip|png|jpe?g)$'
    $patterns = [ordered]@{
        'Private key' = '-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----'
        'OpenAI-style key' = '\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{20,}'
        'GitHub token' = '\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})'
        'JWT' = '\beyJ[A-Za-z0-9_-]{12,}\.[A-Za-z0-9_-]{12,}\.[A-Za-z0-9_-]{12,}'
        'Personal Windows path' = '[A-Za-z]:[\\/]+Users[\\/]+[^\s"''<>]+'
        'Credential assignment' = '(?i)(?:api[_-]?key|access[_-]?token|refresh[_-]?token|password)\s*[=:]\s*["''][A-Za-z0-9_./+\-=]{12,}["'']'
    }
    $findings = @()
    foreach ($file in $files) {
        if ($file -match $blocked) { $findings += "$file : blocked publication path"; continue }
        $lines = @(git show (':' + $file))
        if ($LASTEXITCODE -ne 0) { throw "Cannot read staged file: $file" }
        for ($i = 0; $i -lt $lines.Count; $i++) {
            foreach ($entry in $patterns.GetEnumerator()) {
                if ([regex]::IsMatch($lines[$i], $entry.Value)) { $findings += ('{0}:{1} : {2}' -f $file, ($i + 1), $entry.Key) }
            }
        }
    }
    if ($findings.Count -gt 0) { $findings | Write-Output; throw 'Publication check failed. Review locally; do not print secret contents.' }
    Write-Output ('PASS: {0} staged files; no blocked paths or configured secret patterns found.' -f $files.Count)
} finally { Pop-Location }
