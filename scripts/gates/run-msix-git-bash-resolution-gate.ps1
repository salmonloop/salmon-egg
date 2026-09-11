#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runScriptPath = Join-Path $repoRoot '.tools\run-winui3-msix.ps1'
$runScript = Get-Content -LiteralPath $runScriptPath -Raw
if ($runScript -notmatch "(?m)^\. \(Join-Path \`$PSScriptRoot 'msix-tooling\.ps1'\)\s*\r?$") {
    throw "The MSIX runner does not load its tooling helper."
}

. (Join-Path $repoRoot '.tools\msix-tooling.ps1')

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("salmonegg-msix-bash-" + [guid]::NewGuid().ToString('n'))
$originalPath = $env:PATH
$originalProgramFiles = $env:ProgramFiles
$originalProgramFilesX86 = ${env:ProgramFiles(x86)}

try {
    $fakeWindowsDirectory = Join-Path $testRoot 'Windows\System32'
    $fakeGitRoot = Join-Path $testRoot 'Custom Git'
    $fakeGitCommandDirectory = Join-Path $fakeGitRoot 'cmd'
    $fakeGitBinDirectory = Join-Path $fakeGitRoot 'bin'
    $fakeWslBash = Join-Path $fakeWindowsDirectory 'bash.exe'
    $fakeGit = Join-Path $fakeGitCommandDirectory 'git.exe'
    $fakeGitBash = Join-Path $fakeGitBinDirectory 'bash.exe'

    New-Item -ItemType Directory -Force -Path $fakeWindowsDirectory, $fakeGitCommandDirectory, $fakeGitBinDirectory | Out-Null
    New-Item -ItemType File -Force -Path $fakeWslBash, $fakeGit, $fakeGitBash | Out-Null

    # Reproduce the local failure shape: the WSL launcher is the first bash on PATH, while Git for
    # Windows is installed elsewhere and exposes git.exe through its cmd directory.
    $env:PATH = "$fakeWindowsDirectory;$fakeGitCommandDirectory"
    $env:ProgramFiles = Join-Path $testRoot 'No default Program Files'
    ${env:ProgramFiles(x86)} = Join-Path $testRoot 'No default Program Files x86'

    $resolved = Get-BashPath
    if (-not [string]::Equals($resolved, $fakeGitBash, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Expected Git for Windows bash '$fakeGitBash', got '$resolved'."
    }

    if ([string]::Equals($resolved, $fakeWslBash, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The MSIX runner selected the WSL bash launcher.'
    }

    Write-Host '[msix-bash] passed: Git for Windows bash wins when the WSL launcher is first on PATH'
} finally {
    $env:PATH = $originalPath
    $env:ProgramFiles = $originalProgramFiles
    ${env:ProgramFiles(x86)} = $originalProgramFilesX86
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
