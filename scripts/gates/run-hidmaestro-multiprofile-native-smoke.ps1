param(
    [string]$Configuration = "Debug",
    [string[]]$ProfileIds = @(),
    [int]$Retries = 2
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Multi-profile Windows OS-path skeleton for confirmed HIDMaestro catalogs.
# For each profile: set SALMONEGG_HIDMAESTRO_PROFILE_ID, then re-run the native-device
# Diagnostics GUI smoke against the current MSIX/WinUI build.
# Requires Windows + HIDMaestro driver + installed profile packs + bridge path.
# Does NOT replace physical PS/Xbox/Switch MSIX matrix evidence.

if (-not $IsWindows) {
    throw "HIDMaestro multi-profile native smoke requires Windows. Current platform is not Windows."
}

function Resolve-RepoRoot {
    $scriptDir = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $scriptDir "../..")).Path
}

function Stop-StaleSalmonEggProcesses {
    $names = @("SalmonEgg", "SalmonEgg.GuiTests.Windows", "SalmonEgg.GamepadBridge.Windows")
    foreach ($name in $names) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

function Resolve-ProfileIds {
    param([string[]]$Requested)

    if ($Requested -and $Requested.Count -gt 0) {
        return @($Requested | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }

    $fromEnv = $env:SALMONEGG_HIDMAESTRO_PROFILE_IDS
    if (-not [string]::IsNullOrWhiteSpace($fromEnv)) {
        return @(
            $fromEnv.Split(@(',', ';', ' '), [System.StringSplitOptions]::RemoveEmptyEntries) |
                ForEach-Object { $_.Trim() } |
                Where-Object { $_ }
        )
    }

    # Core-owned checked-in manifest (FormatMultiProfileGateManifest). Do not invent ids here.
    return @(Get-MultiProfileManifestRows | ForEach-Object { $_.ProfileId })
}


function Get-MultiProfileManifestPath {
    $scriptDir = Split-Path -Parent $PSCommandPath
    $path = Join-Path $scriptDir "hidmaestro-multiprofile-manifest.txt"
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Core multiprofile manifest missing: $path. Keep scripts/gates/hidmaestro-multiprofile-manifest.txt aligned with GamepadHidMaestroProfileCatalog.FormatMultiProfileGateManifest()."
    }
    return (Resolve-Path -LiteralPath $path).Path
}

function Get-MultiProfileManifestRows {
    $path = Get-MultiProfileManifestPath
    $rows = @()
    foreach ($line in Get-Content -LiteralPath $path) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith('#')) {
            continue
        }

        $parts = $trimmed.Split('|')
        if ($parts.Count -ne 6) {
            throw "Invalid multiprofile manifest line (expected profile|family|activate|back|west|voice): $trimmed"
        }

        $rows += [pscustomobject]@{
            ProfileId = $parts[0]
            FamilyToken = $parts[1]
            PreferredActivateKey = $parts[2]
            PreferredBackKey = $parts[3]
            PreferredWestKey = $parts[4]
            PreferredVoiceKey = $parts[5]
        }
    }

    if ($rows.Count -eq 0) {
        throw "Multiprofile manifest is empty: $path"
    }

    return $rows
}

function Get-ManifestRow {
    param([string]$ProfileId)
    $match = Get-MultiProfileManifestRows | Where-Object {
        $_.ProfileId.Equals($ProfileId.Trim(), [System.StringComparison]::OrdinalIgnoreCase)
    } | Select-Object -First 1
    if ($null -eq $match) {
        # Unconfirmed profile: family Unknown; preferred keys fall back to Xbox letters for inject notes only.
        return [pscustomobject]@{
            ProfileId = $ProfileId.Trim()
            FamilyToken = 'Unknown'
            PreferredActivateKey = 'A'
            PreferredBackKey = 'B'
            PreferredWestKey = 'X'
            PreferredVoiceKey = 'Y'
        }
    }
    return $match
}

function Get-ExpectedFamilyToken {
    param([string]$ProfileId)
    return (Get-ManifestRow -ProfileId $ProfileId).FamilyToken
}

function Get-ExpectedPreferredFaceKey {
    param(
        [string]$ProfileId,
        [ValidateSet('Activate','Back','West','Voice')]
        [string]$Semantic
    )
    $row = Get-ManifestRow -ProfileId $ProfileId
    switch ($Semantic) {
        'Activate' { return $row.PreferredActivateKey }
        'Back' { return $row.PreferredBackKey }
        'West' { return $row.PreferredWestKey }
        'Voice' { return $row.PreferredVoiceKey }
    }
}

function Resolve-BridgeExecutable {
    param([string]$RepoRoot, [string]$Configuration)

    $configured = $env:SALMONEGG_GUI_GAMEPAD_NATIVE_BRIDGE
    if (-not [string]::IsNullOrWhiteSpace($configured)) {
        if (-not (Test-Path -LiteralPath $configured)) {
            throw "SALMONEGG_GUI_GAMEPAD_NATIVE_BRIDGE points to a missing file: $configured"
        }

        return (Resolve-Path -LiteralPath $configured).Path
    }

    $candidates = @(
        (Join-Path $RepoRoot "tests/SalmonEgg.GamepadBridge.Windows/bin/$Configuration/net10.0-windows10.0.26100.0/SalmonEgg.GamepadBridge.Windows.exe"),
        (Join-Path $RepoRoot "tests/SalmonEgg.GamepadBridge.Windows/bin/$Configuration/net10.0-windows10.0.26100.0/win-x64/SalmonEgg.GamepadBridge.Windows.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "Native-device bridge executable not found. Build tests/SalmonEgg.GamepadBridge.Windows and set SALMONEGG_GUI_GAMEPAD_NATIVE_BRIDGE, or place HIDMaestro.Core.dll so the bridge can start."
}

function Invoke-NativeDiagnosticsSmoke {
    param(
        [string]$ProfileId,
        [string]$BridgePath,
        [string]$Configuration,
        [int]$Retries
    )

    $env:SALMONEGG_GUI_GAMEPAD_INPUT_BACKEND = "native-device"
    $env:SALMONEGG_GUI_GAMEPAD_NATIVE_BRIDGE = $BridgePath
    $env:SALMONEGG_HIDMAESTRO_PROFILE_ID = $ProfileId
    $env:SALMONEGG_GUI = "1"

    $method = "SalmonEgg.GuiTests.Windows.DiagnosticsSettingsSmokeTests.GamepadDiagnosticsMonitor_NativeDeviceBackend_ReflectsVirtualControllerInput"

    for ($attempt = 1; $attempt -le $Retries; $attempt++) {
        $expectedFamily = Get-ExpectedFamilyToken -ProfileId $ProfileId
        $activateKey = Get-ExpectedPreferredFaceKey -ProfileId $ProfileId -Semantic Activate
        $backKey = Get-ExpectedPreferredFaceKey -ProfileId $ProfileId -Semantic Back
        $westKey = Get-ExpectedPreferredFaceKey -ProfileId $ProfileId -Semantic West
        $voiceKey = Get-ExpectedPreferredFaceKey -ProfileId $ProfileId -Semantic Voice
        Write-Host "[multi-profile] profile=$ProfileId family=$expectedFamily face=Activate:$activateKey,Back:$backKey,West:$westKey,Voice:$voiceKey attempt=$attempt/$Retries bridge=$BridgePath"
        try {
            dotnet test `
              --project tests/SalmonEgg.GuiTests.Windows/SalmonEgg.GuiTests.Windows.csproj `
              --configuration $Configuration `
              --no-build `
              --filter-method $method `
              --timeout 8m `
              --output Normal
            if ($LASTEXITCODE -ne 0) {
                throw "Native Diagnostics smoke failed for profile '$ProfileId' with exit code $LASTEXITCODE."
            }

            Write-Host "[multi-profile] PASS profile=$ProfileId"
            return
        }
        catch {
            if ($attempt -eq $Retries) {
                throw
            }

            Write-Warning "[multi-profile] profile=$ProfileId failed on attempt $attempt. Cleaning up and retrying."
            Stop-StaleSalmonEggProcesses
            Start-Sleep -Seconds (2 * $attempt)
        }
    }
}

$repoRoot = Resolve-RepoRoot
Set-Location $repoRoot

$profileIds = Resolve-ProfileIds -Requested $ProfileIds
if ($profileIds.Count -eq 0) {
    throw "No HIDMaestro profile ids resolved for multi-profile native smoke."
}

Write-Host "[multi-profile] Build GamepadBridge.Windows"
dotnet build tests/SalmonEgg.GamepadBridge.Windows/SalmonEgg.GamepadBridge.Windows.csproj -c $Configuration -v minimal
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "[multi-profile] Build GuiTests.Windows"
dotnet build tests/SalmonEgg.GuiTests.Windows/SalmonEgg.GuiTests.Windows.csproj -c $Configuration -v minimal
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$bridgePath = Resolve-BridgeExecutable -RepoRoot $repoRoot -Configuration $Configuration
Write-Host "[multi-profile] Using bridge: $bridgePath"
Write-Host "[multi-profile] Profiles: $($profileIds -join ', ')"

if ([string]::IsNullOrWhiteSpace($env:SALMONEGG_HIDMAESTRO_CORE_PATH)) {
    Write-Warning "SALMONEGG_HIDMAESTRO_CORE_PATH is not set. Bridge will look for HIDMaestro.Core.dll beside the bridge executable."
}

$failed = @()
foreach ($profileId in $profileIds) {
    Stop-StaleSalmonEggProcesses
    try {
        Invoke-NativeDiagnosticsSmoke `
            -ProfileId $profileId `
            -BridgePath $bridgePath `
            -Configuration $Configuration `
            -Retries $Retries
    }
    catch {
        Write-Error $_
        $failed += $profileId
    }
}

Stop-StaleSalmonEggProcesses

if ($failed.Count -gt 0) {
    throw "HIDMaestro multi-profile native smoke failed for: $($failed -join ', ')"
}

Write-Host "[multi-profile] All confirmed-profile native Diagnostics smokes passed ($($profileIds.Count) profiles)."
Write-Host "[multi-profile] Reminder: this is virtual HID OS-path evidence only; physical MSIX matrix still required for multi-brand completion."
