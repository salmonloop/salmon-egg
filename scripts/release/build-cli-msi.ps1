#requires -Version 7.0
<#
.SYNOPSIS
    Builds a per-user Windows MSI that installs the SalmonEgg CLI and registers it on PATH.

.DESCRIPTION
    Windows has no /usr/bin equivalent, so the command is made discoverable by appending the install
    folder to the *user* PATH through a WiX Environment element. Windows Installer owns that value: it is
    written on install and removed on uninstall, which a script editing the registry or calling setx
    cannot guarantee. Nothing here touches the machine PATH, and the GUI installers are untouched.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [string]$Version,
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path

if (-not (Test-Path -LiteralPath $Executable)) {
    throw "Executable not found: $Executable"
}
$executablePath = (Resolve-Path -LiteralPath $Executable).Path

if ([string]::IsNullOrWhiteSpace($Version)) {
    $cliProject = Join-Path $repoRoot 'src/SalmonEgg.Cli/SalmonEgg.Cli.csproj'
    $Version = (dotnet msbuild $cliProject -getProperty:SalmonEggDisplayVersion -nologo).Trim()
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Package version must be a three-part numeric version, got: $Version"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts/cli'
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$outputDirectoryPath = (Resolve-Path -LiteralPath $OutputDirectory).Path

$installerDir = Join-Path $repoRoot 'artifacts/cli-msi'
if (Test-Path -LiteralPath $installerDir) {
    Remove-Item -LiteralPath $installerDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $installerDir | Out-Null

# A stable UpgradeCode is what lets a new version replace the old one instead of installing beside it and
# leaving two salmon-egg.exe entries competing on PATH.
$upgradeCode = '4B4D0B4E-9E0A-4C3C-9E64-3D0B69B3A0F1'

$productPath = Join-Path $installerDir 'Product.wxs'
$productXml = @(
    '<?xml version="1.0" encoding="UTF-8"?>'
    '<Wix xmlns="http://schemas.microsoft.com/wix/2006/wi">'
    "  <Product Id=`"*`" Name=`"SalmonEgg CLI`" Language=`"1033`" Version=`"$Version`" Manufacturer=`"SalmonLoop`" UpgradeCode=`"$upgradeCode`">"
    '    <Package InstallerVersion="500" Compressed="yes" InstallScope="perUser" InstallPrivileges="limited" Description="SalmonEgg configuration management CLI" />'
    '    <MajorUpgrade DowngradeErrorMessage="A newer version of SalmonEgg CLI is already installed." />'
    '    <MediaTemplate EmbedCab="yes" />'
    '    <Feature Id="CliFeature" Title="SalmonEgg CLI" Level="1">'
    '      <ComponentRef Id="CliExecutableComponent" />'
    '    </Feature>'
    '  </Product>'
    '  <Fragment>'
    '    <Directory Id="TARGETDIR" Name="SourceDir">'
    '      <Directory Id="LocalAppDataFolder">'
    '        <Directory Id="SalmonEggFolder" Name="SalmonEgg">'
    '          <Directory Id="INSTALLFOLDER" Name="cli" />'
    '        </Directory>'
    '      </Directory>'
    '    </Directory>'
    '    <ComponentGroup Id="CliComponents" Directory="INSTALLFOLDER">'
    '      <Component Id="CliExecutableComponent" Guid="*">'
    "        <File Id=`"CliExecutable`" Name=`"salmon-egg.exe`" Source=`"$executablePath`" KeyPath=`"yes`" />"
    '        <!-- Windows Installer owns this PATH entry: added on install, removed on uninstall. -->'
    '        <Environment Id="CliPathEntry"'
    '                     Name="PATH"'
    '                     Value="[INSTALLFOLDER]"'
    '                     Action="set"'
    '                     Part="last"'
    '                     System="no"'
    '                     Permanent="no" />'
    '      </Component>'
    '    </ComponentGroup>'
    '  </Fragment>'
    '</Wix>'
)
Set-Content -Path $productPath -Encoding UTF8 -Value $productXml

$wixObjDir = Join-Path $installerDir 'obj'
New-Item -ItemType Directory -Force -Path $wixObjDir | Out-Null

& candle -out (Join-Path $wixObjDir '') $productPath
if ($LASTEXITCODE -ne 0) { throw "candle failed with exit code $LASTEXITCODE." }

$msiPath = Join-Path $outputDirectoryPath "salmon-egg-cli-$Version-win-x64.msi"
if (Test-Path -LiteralPath $msiPath) {
    Remove-Item -LiteralPath $msiPath -Force
}

& light -cultures:en-us -sice:ICE38 -sice:ICE64 -sice:ICE91 -out $msiPath (Join-Path $wixObjDir 'Product.wixobj')
if ($LASTEXITCODE -ne 0) { throw "light failed with exit code $LASTEXITCODE." }

if (-not (Test-Path -LiteralPath $msiPath)) {
    throw "MSI was not produced: $msiPath"
}

# Verify the PATH registration landed in the built package rather than trusting the authoring above. A
# real install/uninstall check needs an interactive Windows session and stays a manual release step (see
# docs/release-guide.md); what can be asserted here is the package's own Environment table.
#
# The rule itself lives in CliMsiPathContract.ps1, which documents the MSI encoding it enforces, so that
# scripts/gates/run-cli-msi-path-contract-gate.ps1 can exercise the rule — and each of its failure
# cases — without WiX or a Windows session.
. (Join-Path $PSScriptRoot 'CliMsiPathContract.ps1')

$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.GetType().InvokeMember(
    'OpenDatabase', 'InvokeMethod', $null, $installer, @($msiPath, 0))
try {
    $view = $database.GetType().InvokeMember(
        'OpenView', 'InvokeMethod', $null, $database,
        @('SELECT `Name`, `Value` FROM `Environment`'))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null)

    # Every row is read rather than just the first: a second row introduced later — a machine PATH entry,
    # say — would ship unchecked if the read stopped after one.
    $rows = @()
    while ($true) {
        $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
        if ($null -eq $record) {
            break
        }

        $rows += [pscustomobject]@{
            Name  = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(1))
            Value = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(2))
        }
    }

    if ($rows.Count -eq 0) {
        throw 'The built MSI has no Environment table row: the CLI would not be registered on PATH.'
    }

    if ($rows.Count -ne 1) {
        $described = ($rows | ForEach-Object { "Name='$($_.Name)' Value='$($_.Value)'" }) -join '; '
        throw ("The built MSI has $($rows.Count) Environment table rows, but this package registers " +
               "exactly one PATH entry. Rows: $described")
    }

    Assert-CliMsiPathContract -Name $rows[0].Name -Value $rows[0].Value
    Write-Host "[cli-msi] verified PATH registration: Name='$($rows[0].Name)' Value='$($rows[0].Value)'"
}
finally {
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}

$hash = (Get-FileHash -LiteralPath $msiPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path -Leaf $msiPath)" | Set-Content -Path "$msiPath.sha256" -Encoding ascii

Write-Host "[cli-msi] package:  $msiPath"
Write-Host "[cli-msi] checksum: $msiPath.sha256"

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    "msi-path=$msiPath" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
}
