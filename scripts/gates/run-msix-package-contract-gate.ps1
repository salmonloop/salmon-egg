#requires -Version 7.0
<#
.SYNOPSIS
    Asserts that a produced MSIX package actually contains everything its manifest promises.

.DESCRIPTION
    A successful `dotnet msbuild /t:Publish` is not evidence that the package is usable. The WinUI 3
    target sits in a gap between two default-item owners (see the comment above the Assets ItemGroup in
    SalmonEgg.csproj): when the explicit asset items regress, the build still succeeds and still emits a
    valid-looking .msix — the shell logos declared in Package.appxmanifest are simply missing, and
    Windows silently falls back to the generic placeholder icon. That defect is invisible to every
    existing gate and only shows up after someone installs the release.

    This gate opens the package as a zip, reads AppxManifest.xml, and hard-asserts that every asset path
    the manifest references is present as a package entry. It also asserts identity and a three-part
    numeric version so a manifest whose version token was never substituted cannot ship.

    Pure zip + XML inspection: no Windows APIs, no MSIX tooling, runs on any platform with pwsh. That is
    deliberate — the rule must be rehearsable off Windows, or it becomes another assertion nobody can
    test until a tag build.

.PARAMETER Package
    Path to the .msix to inspect.

.PARAMETER ExpectedIdentityName
    Package identity Name that must match exactly.

.PARAMETER ExpectedPublisher
    Package identity Publisher (certificate subject) that must match exactly.

.PARAMETER SelfTest
    Run the gate's own reverse-verification cases against synthesized packages instead of inspecting a
    real one. Proves each assertion actually rejects the defect it claims to catch.
#>
[CmdletBinding(DefaultParameterSetName = 'Inspect')]
param(
    [Parameter(ParameterSetName = 'Inspect', Mandatory = $true, Position = 0)]
    [string]$Package,

    [Parameter(ParameterSetName = 'Inspect')]
    [string]$ExpectedIdentityName = 'SalmonEgg.SalmonEgg',

    [Parameter(ParameterSetName = 'Inspect')]
    [string]$ExpectedPublisher = 'CN=0B694F0E-510C-433A-A6F7-1484D6A39E19',

    [Parameter(ParameterSetName = 'SelfTest', Mandatory = $true)]
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

# The manifest addresses assets with Windows separators; package entries use forward slashes. Normalize
# both to a single comparable form so a backslash/forward-slash mismatch cannot be mistaken for a
# missing asset (or, worse, hide a genuinely missing one).
function ConvertTo-PackagePath
{
    param([Parameter(Mandatory = $true)][string]$Path)

    return ($Path -replace '\\', '/').TrimStart('/')
}

# Every manifest attribute that names a file inside the package. Logo/Square*/Wide*/SplashScreen are the
# shell-facing ones whose absence produces the placeholder-icon defect; collecting them by attribute name
# rather than by a fixed element list means a newly authored tile is covered without editing this gate.
$script:AssetAttributeNames = @(
    'Logo'
    'Square44x44Logo'
    'Square71x71Logo'
    'Square150x150Logo'
    'Square310x310Logo'
    'Wide310x150Logo'
    'Image'
)

# An asset path can be an attribute value (uap:VisualElements Square44x44Logo="...") or element text
# (<Logo>Assets\...\iconLogo.png</Logo> under Properties, which is how the real manifest declares the
# package logo). An attribute-only scan silently passes a package missing that logo — the self-test for
# this gate proves it, so both shapes are collected.
function Get-ManifestAssetReference
{
    param([Parameter(Mandatory = $true)][xml]$Manifest)

    $references = [System.Collections.Generic.List[string]]::new()

    function Add-Candidate
    {
        param([string]$Value)

        if ([string]::IsNullOrWhiteSpace($Value))
        {
            return
        }

        $candidate = $Value.Trim()

        # Manifest tokens such as $targetnametoken$ are substituted by the packaging tool, and
        # non-image payloads are out of scope for a shell-asset check.
        if ($candidate.Contains('$') -or $candidate -notmatch '\.(png|ico|jpg|jpeg|gif)$')
        {
            return
        }

        $references.Add((ConvertTo-PackagePath $candidate))
    }

    foreach ($node in $Manifest.SelectNodes('//*'))
    {
        foreach ($attributeName in $script:AssetAttributeNames)
        {
            Add-Candidate -Value $node.GetAttribute($attributeName)
        }

        # Only leaf elements carry a path as text; an element with children would yield concatenated
        # descendant text.
        if ($node.ChildNodes.Count -eq 1 -and $node.FirstChild.NodeType -eq [System.Xml.XmlNodeType]::Text)
        {
            Add-Candidate -Value $node.FirstChild.Value
        }
    }

    return ($references | Sort-Object -Unique)
}

function Get-MsixContractViolation
{
    param(
        [Parameter(Mandatory = $true)][string]$PackagePath,
        [Parameter(Mandatory = $true)][string]$IdentityName,
        [Parameter(Mandatory = $true)][string]$Publisher
    )

    if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf))
    {
        return [pscustomobject]@{ Id = 'PackageMissing'; Detail = $PackagePath }
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
    try
    {
        $entries = @($archive.Entries | ForEach-Object { ConvertTo-PackagePath $_.FullName })
        $manifestEntry = $archive.Entries |
            Where-Object { (ConvertTo-PackagePath $_.FullName) -ieq 'AppxManifest.xml' } |
            Select-Object -First 1
        if ($null -eq $manifestEntry)
        {
            return [pscustomobject]@{ Id = 'ManifestMissing'; Detail = 'AppxManifest.xml' }
        }

        $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
        try
        {
            $manifestText = $reader.ReadToEnd()
        }
        finally
        {
            $reader.Dispose()
        }

        try
        {
            $manifest = [xml]$manifestText
        }
        catch
        {
            return [pscustomobject]@{ Id = 'ManifestUnparsable'; Detail = $_.Exception.Message }
        }

        $identity = $manifest.SelectSingleNode('/*[local-name()="Package"]/*[local-name()="Identity"]')
        if ($null -eq $identity)
        {
            return [pscustomobject]@{ Id = 'IdentityMissing'; Detail = 'no Identity element' }
        }

        if ($identity.GetAttribute('Name') -cne $IdentityName)
        {
            return [pscustomobject]@{ Id = 'IdentityNameMismatch'; Detail = $identity.GetAttribute('Name') }
        }

        if ($identity.GetAttribute('Publisher') -cne $Publisher)
        {
            return [pscustomobject]@{ Id = 'PublisherMismatch'; Detail = $identity.GetAttribute('Publisher') }
        }

        # A package whose version token was never substituted, or which carries the 0.0.0.0 placeholder,
        # cannot be upgraded over by a later release.
        $version = $identity.GetAttribute('Version')
        if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$')
        {
            return [pscustomobject]@{ Id = 'VersionNotSubstituted'; Detail = $version }
        }

        if ($version -eq '0.0.0.0')
        {
            return [pscustomobject]@{ Id = 'VersionPlaceholder'; Detail = $version }
        }

        $assetReferences = Get-ManifestAssetReference -Manifest $manifest
        if ($assetReferences.Count -eq 0)
        {
            return [pscustomobject]@{ Id = 'NoAssetReferences'; Detail = 'manifest declares no shell assets' }
        }

        foreach ($reference in $assetReferences)
        {
            $present = $entries | Where-Object { $_ -ieq $reference } | Select-Object -First 1
            if ($null -eq $present)
            {
                return [pscustomobject]@{ Id = 'DeclaredAssetMissing'; Detail = $reference }
            }
        }

        return $null
    }
    finally
    {
        $archive.Dispose()
    }
}

function New-TestPackage
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][hashtable]$Entries
    )

    $archive = [System.IO.Compression.ZipFile]::Open($Path, [System.IO.Compression.ZipArchiveMode]::Create)
    try
    {
        foreach ($name in $Entries.Keys)
        {
            $entry = $archive.CreateEntry($name)
            $writer = [System.IO.StreamWriter]::new($entry.Open())
            try
            {
                $writer.Write([string]$Entries[$name])
            }
            finally
            {
                $writer.Dispose()
            }
        }
    }
    finally
    {
        $archive.Dispose()
    }
}

function New-TestManifest
{
    param(
        [string]$IdentityName = 'SalmonEgg.SalmonEgg',
        [string]$Publisher = 'CN=0B694F0E-510C-433A-A6F7-1484D6A39E19',
        [string]$Version = '1.2.0.0',
        [string]$Logo = 'Assets\Icons\Windows\iconLogo.png'
    )

    return @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10">
  <Identity Name="$IdentityName" Publisher="$Publisher" Version="$Version" />
  <Properties>
    <Logo>$Logo</Logo>
  </Properties>
  <Applications>
    <Application Id="App" Executable="`$targetnametoken`$.exe">
      <uap:VisualElements Square44x44Logo="Assets\Icons\Windows\iconLogo44.png"
                          Square150x150Logo="Assets\Icons\Windows\iconLogo150.png" />
    </Application>
  </Applications>
</Package>
"@
}

# `+` on hashtables throws when a key exists in both operands, which is exactly the case here: every
# variant case overrides AppxManifest.xml. Merge by assignment instead.
function Merge-TestEntries
{
    param(
        [Parameter(Mandatory = $true)][hashtable]$Base,
        [Parameter(Mandatory = $true)][hashtable]$Override
    )

    $merged = @{}
    foreach ($key in $Base.Keys)
    {
        $merged[$key] = $Base[$key]
    }

    foreach ($key in $Override.Keys)
    {
        $merged[$key] = $Override[$key]
    }

    return $merged
}

function Invoke-SelfTest
{
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("msix-gate-selftest-" + [guid]::NewGuid().ToString('n'))
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    try
    {
        $allAssets = @{
            'AppxManifest.xml'                             = New-TestManifest
            'Assets/Icons/Windows/iconLogo.png'            = 'png'
            'Assets/Icons/Windows/iconLogo44.png'          = 'png'
            'Assets/Icons/Windows/iconLogo150.png'         = 'png'
        }

        $cases = @(
            @{
                Description = 'a package carrying every asset its manifest declares'
                Entries     = $allAssets
                Expected    = $null
            }
            @{
                # The exact defect the Assets ItemGroup in SalmonEgg.csproj exists to prevent: build
                # succeeds, package is well-formed, shell logo is simply absent.
                Description = 'a package whose declared shell logo was never included'
                Entries     = @{
                    'AppxManifest.xml'                     = New-TestManifest
                    'Assets/Icons/Windows/iconLogo44.png'   = 'png'
                    'Assets/Icons/Windows/iconLogo150.png'  = 'png'
                }
                Expected    = 'DeclaredAssetMissing'
            }
            @{
                Description = 'a package whose VisualElements tile logo was never included'
                Entries     = @{
                    'AppxManifest.xml'                     = New-TestManifest
                    'Assets/Icons/Windows/iconLogo.png'     = 'png'
                    'Assets/Icons/Windows/iconLogo44.png'   = 'png'
                }
                Expected    = 'DeclaredAssetMissing'
            }
            @{
                Description = 'a package whose version token was never substituted'
                Entries     = (Merge-TestEntries -Base $allAssets -Override @{ 'AppxManifest.xml' = New-TestManifest -Version '__SALMONEGG_PACKAGE_VERSION__' })
                Expected    = 'VersionNotSubstituted'
            }
            @{
                Description = 'a package left at the placeholder version'
                Entries     = (Merge-TestEntries -Base $allAssets -Override @{ 'AppxManifest.xml' = New-TestManifest -Version '0.0.0.0' })
                Expected    = 'VersionPlaceholder'
            }
            @{
                Description = 'a package signed under a different publisher subject'
                Entries     = (Merge-TestEntries -Base $allAssets -Override @{ 'AppxManifest.xml' = New-TestManifest -Publisher 'CN=Someone Else' })
                Expected    = 'PublisherMismatch'
            }
            @{
                Description = 'a package whose identity name drifted'
                Entries     = (Merge-TestEntries -Base $allAssets -Override @{ 'AppxManifest.xml' = New-TestManifest -IdentityName 'Other.App' })
                Expected    = 'IdentityNameMismatch'
            }
            @{
                Description = 'an archive with no AppxManifest.xml at all'
                Entries     = @{ 'Assets/Icons/Windows/iconLogo.png' = 'png' }
                Expected    = 'ManifestMissing'
            }
            @{
                Description = 'a package whose manifest is not well-formed XML'
                Entries     = @{ 'AppxManifest.xml' = '<Package><Identity' }
                Expected    = 'ManifestUnparsable'
            }
        )

        $failures = @()
        $index = 0
        foreach ($case in $cases)
        {
            $index++
            $packagePath = Join-Path $root "case-$index.msix"
            New-TestPackage -Path $packagePath -Entries $case.Entries

            $violation = Get-MsixContractViolation `
                -PackagePath $packagePath `
                -IdentityName 'SalmonEgg.SalmonEgg' `
                -Publisher 'CN=0B694F0E-510C-433A-A6F7-1484D6A39E19'
            $actual = if ($null -eq $violation) { $null } else { $violation.Id }

            if ($actual -ne $case.Expected)
            {
                $expectedLabel = if ($null -eq $case.Expected) { '(conforming)' } else { $case.Expected }
                $actualLabel = if ($null -eq $actual) { '(conforming)' } else { $actual }
                $failures += "$($case.Description): expected $expectedLabel but got $actualLabel"
                continue
            }

            $outcome = if ($null -eq $actual) { 'conforms' } else { "rejected as $actual" }
            Write-Host "[msix-gate] $($case.Description): $outcome"
        }

        # The gate must also reject a path that does not exist, or a missing publish output would read as
        # a pass.
        $absent = Get-MsixContractViolation `
            -PackagePath (Join-Path $root 'does-not-exist.msix') `
            -IdentityName 'SalmonEgg.SalmonEgg' `
            -Publisher 'CN=0B694F0E-510C-433A-A6F7-1484D6A39E19'
        if ($null -eq $absent -or $absent.Id -ne 'PackageMissing')
        {
            $failures += 'a nonexistent package path was not reported as PackageMissing'
        }

        if ($failures.Count -gt 0)
        {
            Write-Host ''
            foreach ($failure in $failures)
            {
                Write-Host "[msix-gate] FAIL $failure"
            }

            throw "MSIX package contract self-test failed with $($failures.Count) violation(s)."
        }

        Write-Host "[msix-gate] self-test passed: $($cases.Count) package cases plus 1 missing-file check"
    }
    finally
    {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($SelfTest)
{
    Invoke-SelfTest
    return
}

$violation = Get-MsixContractViolation `
    -PackagePath $Package `
    -IdentityName $ExpectedIdentityName `
    -Publisher $ExpectedPublisher

if ($null -ne $violation)
{
    throw "MSIX package contract violated [$($violation.Id)]: $($violation.Detail) (package: $Package)"
}

Write-Host "[msix-gate] passed: $Package satisfies identity, version, and declared-asset presence"
