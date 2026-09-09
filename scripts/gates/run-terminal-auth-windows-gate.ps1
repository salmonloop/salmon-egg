param([string] $Configuration = 'Release')

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) {
    throw 'The terminal authentication gate requires Windows and actual ConPTY processes.'
}

$repositoryRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Push-Location $repositoryRoot
try {
    $outputDirectory = Join-Path $repositoryRoot 'artifacts/terminal-auth-windows'
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
    git rev-parse HEAD | Set-Content (Join-Path $outputDirectory 'source-commit.txt')
    dotnet test --project tests/SalmonEgg.TerminalAuth.Windows.Tests/SalmonEgg.TerminalAuth.Windows.Tests.csproj `
        --configuration $Configuration -p:UseSharedCompilation=false `
        --minimum-expected-tests 4 --timeout 3m --output Normal --no-ansi 2>&1 |
        Tee-Object -FilePath (Join-Path $outputDirectory 'test.log')
    if ($LASTEXITCODE -ne 0) {
        throw "Terminal authentication tests failed with exit code $LASTEXITCODE."
    }

    $log = Get-Content (Join-Path $outputDirectory 'test.log') -Raw
    if ($log -notmatch '(?m)^\s+succeeded:\s+([4-9]|[1-9][0-9]+)\s*$' -or
        $log -notmatch '(?m)^\s+failed:\s+0\s*$' -or
        $log -match '(?im)\bskipped:\s*[1-9]') {
        throw 'The gate requires at least four executed tests, zero failures and zero skips.'
    }
}
finally {
    Pop-Location
}
