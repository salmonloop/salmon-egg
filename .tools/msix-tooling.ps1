# The bundled CLI is published by scripts/release/publish-cli-binary.sh, the same script the release
# workflow runs, so the local MSIX needs a POSIX shell. Only Git for Windows Bash is compatible with
# the native Windows paths passed by the packaging script; bash.exe on PATH may be the WSL launcher.
function Get-BashPath {
    $gitOnPath = Get-Command git.exe -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($gitOnPath) {
        $gitRoot = Split-Path -Parent (Split-Path -Parent $gitOnPath.Source)
        $gitBash = Join-Path $gitRoot 'bin\bash.exe'
        if (Test-Path -LiteralPath $gitBash -PathType Leaf) {
            return $gitBash
        }
    }

    $candidates = @(
        (Join-Path $env:ProgramFiles 'Git\bin\bash.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Git\bin\bash.exe')
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return $candidate
        }
    }

    throw "Git for Windows bash.exe not found. The MSIX package embeds the salmon-egg CLI, published by scripts/release/publish-cli-binary.sh; install Git for Windows so this script can run it."
}
