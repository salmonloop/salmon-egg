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

    It asserts the same thing about the bundled CLI: the package registers exactly one app execution
    alias, under the product's command name, entered as a full-trust process, pointing at an executable
    the package really carries. That is the whole of "installing SalmonEgg puts salmon-egg on PATH" on
    Windows, and each half of it fails silently — a package with the alias but no payload, or the payload
    but no alias, installs and looks correct.

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

# The command the package puts on PATH. A packaged app cannot append to PATH -- the OS is the installer
# and writes no environment variables on the app's behalf -- so Windows exposes the command through an
# app execution alias, a stub it materializes under %LOCALAPPDATA%\Microsoft\WindowsApps, a directory
# already on the per-user PATH. Three independent things have to hold for `salmon-egg` to work, and each
# fails silently on its own: the alias name is what the user types, the entry point is what makes the
# launch a classic full-trust process (a UWP activation loses the caller's console, arguments and exit
# code), and the executable is a package-relative path that has to resolve to a file that is really there.
$script:ExpectedExecutionAlias = 'salmon-egg.exe'
$script:ExpectedAliasEntryPoint = 'Windows.FullTrustApplication'

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

# A manifest names an asset by its unqualified path (Assets\Icons\Windows\iconLogo44.png) while MakePRI
# stores the generated scale/targetsize/theme variants under qualified names
# (assets/icons/windows/iconlogo44.scale-200.png) and records the mapping in resources.pri. Dissecting the
# shipped v1.2.0 package confirmed this: all seven manifest references resolve to zero exact entries and to
# 10-52 qualified variants each. An exact-name check would therefore reject every correct package.
#
# So the assertion is "the package carries at least one file that can satisfy this reference": an exact
# entry, or any entry whose name is the reference's stem followed by a qualifier segment. That still fails
# when the asset items regress -- the defect this gate exists for removes the whole family, not one
# variant -- while accepting the naming the packaging tool actually produces.
function Test-PackageCarriesAsset
{
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Entries,
        [Parameter(Mandatory = $true)][string]$Reference
    )

    foreach ($entry in $Entries)
    {
        if ($entry -ieq $Reference)
        {
            return $true
        }
    }

    $extension = [System.IO.Path]::GetExtension($Reference)
    if ([string]::IsNullOrEmpty($extension))
    {
        return $false
    }

    # "assets/icons/windows/iconlogo44." -- the qualifier follows the stem and precedes the extension.
    $stem = $Reference.Substring(0, $Reference.Length - $extension.Length) + '.'
    foreach ($entry in $Entries)
    {
        if (-not $entry.StartsWith($stem, [System.StringComparison]::OrdinalIgnoreCase))
        {
            continue
        }

        if ($entry.EndsWith($extension, [System.StringComparison]::OrdinalIgnoreCase))
        {
            return $true
        }
    }

    return $false
}

# Asserts the package both registers the bundled CLI as an app execution alias and carries the executable
# that alias names. Windows validates only that the referenced path exists in the package; the alias name,
# the entry point and whether the extension is declared at all are ours to check, and every one of them
# fails as "salmon-egg: command not found" on a user's machine rather than at packaging time.
function Get-ExecutionAliasViolation
{
    param(
        [Parameter(Mandatory = $true)][xml]$Manifest,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Entries
    )

    # Matched by local name: the manifest authors this as uap3:Extension, but uap5 and uap10 declare the
    # same category, and a package moved to either of those must still satisfy the contract.
    $extensions = @($Manifest.SelectNodes("//*[local-name()='Extension' and @Category='windows.appExecutionAlias']"))
    if ($extensions.Count -eq 0)
    {
        return [pscustomobject]@{ Id = 'AppExecutionAliasMissing'; Detail = 'no windows.appExecutionAlias extension' }
    }

    # Windows allows one alias extension per Application. With more than one, which command the user ends
    # up with is decided by registration order instead of by this manifest.
    if ($extensions.Count -ne 1)
    {
        return [pscustomobject]@{ Id = 'AppExecutionAliasAmbiguous'; Detail = "$($extensions.Count) alias extensions" }
    }

    $extension = $extensions[0]
    $entryPoint = $extension.GetAttribute('EntryPoint')
    if ($entryPoint -cne $script:ExpectedAliasEntryPoint)
    {
        return [pscustomobject]@{ Id = 'AppExecutionAliasEntryPointMismatch'; Detail = $entryPoint }
    }

    $aliases = @($extension.SelectNodes(".//*[local-name()='ExecutionAlias']") | ForEach-Object { $_.GetAttribute('Alias') })
    if ($aliases.Count -ne 1 -or $aliases[0] -ine $script:ExpectedExecutionAlias)
    {
        return [pscustomobject]@{ Id = 'AppExecutionAliasNameMismatch'; Detail = ($aliases -join ', ') }
    }

    $executable = $extension.GetAttribute('Executable')
    if ([string]::IsNullOrWhiteSpace($executable))
    {
        return [pscustomobject]@{ Id = 'AppExecutionAliasTargetMissing'; Detail = '(no Executable attribute)' }
    }

    # The alias names a package-relative path with Windows separators while package entries use forward
    # slashes. Exact match only: unlike a shell asset, an executable carries no resource qualifiers, so a
    # name that merely resembles the target is a command that does not start.
    $target = ConvertTo-PackagePath $executable
    foreach ($entry in $Entries)
    {
        if ($entry -ieq $target)
        {
            return $null
        }
    }

    return [pscustomobject]@{ Id = 'AppExecutionAliasTargetMissing'; Detail = $target }
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

        $aliasViolation = Get-ExecutionAliasViolation -Manifest $manifest -Entries $entries
        if ($null -ne $aliasViolation)
        {
            return $aliasViolation
        }

        $assetReferences = Get-ManifestAssetReference -Manifest $manifest
        if ($assetReferences.Count -eq 0)
        {
            return [pscustomobject]@{ Id = 'NoAssetReferences'; Detail = 'manifest declares no shell assets' }
        }

        foreach ($reference in $assetReferences)
        {
            if (-not (Test-PackageCarriesAsset -Entries $entries -Reference $reference))
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
        [string]$Logo = 'Assets\Icons\Windows\iconLogo.png',
        [string]$AliasExecutable = 'cli\salmon-egg.exe',
        [string]$Alias = 'salmon-egg.exe',
        [string]$AliasEntryPoint = 'Windows.FullTrustApplication',
        [switch]$OmitAlias,
        [switch]$DuplicateAlias
    )

    $extensionsXml = ''
    if (-not $OmitAlias)
    {
        $aliasBlock = @"
        <uap3:Extension Category="windows.appExecutionAlias" Executable="$AliasExecutable" EntryPoint="$AliasEntryPoint">
          <uap3:AppExecutionAlias>
            <desktop:ExecutionAlias Alias="$Alias" />
          </uap3:AppExecutionAlias>
        </uap3:Extension>
"@
        $aliasBlocks = if ($DuplicateAlias) { "$aliasBlock`n$aliasBlock" } else { $aliasBlock }
        $extensionsXml = @"
      <Extensions>
$aliasBlocks
      </Extensions>
"@
    }

    return @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
         xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
         xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10">
  <Identity Name="$IdentityName" Publisher="$Publisher" Version="$Version" />
  <Properties>
    <Logo>$Logo</Logo>
  </Properties>
  <Applications>
    <Application Id="App" Executable="`$targetnametoken`$.exe">
      <uap:VisualElements Square44x44Logo="Assets\Icons\Windows\iconLogo44.png"
                          Square150x150Logo="Assets\Icons\Windows\iconLogo150.png" />
$extensionsXml
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
            'cli/salmon-egg.exe'                           = 'MZ'
            'Assets/Icons/Windows/iconLogo.png'            = 'png'
            'Assets/Icons/Windows/iconLogo44.png'          = 'png'
            'Assets/Icons/Windows/iconLogo150.png'         = 'png'
        }

        # What the packaging tool actually produces, taken from dissecting the shipped v1.2.0 package: no
        # unqualified file exists at all, only scale/targetsize/theme variants. The first version of this
        # gate matched names exactly and would have rejected every real package. These cases keep that
        # regression from returning.
        $mrtAssets = @{
            'AppxManifest.xml'                                                   = New-TestManifest
            'cli/salmon-egg.exe'                                                 = 'MZ'
            'Assets/Icons/Windows/iconLogo.scale-200.png'                         = 'png'
            'Assets/Icons/Windows/iconLogo.targetsize-32.png'                     = 'png'
            'Assets/Icons/Windows/iconLogo44.scale-200.png'                       = 'png'
            'Assets/Icons/Windows/iconLogo44.altform-unplated_targetsize-16.png'  = 'png'
            'Assets/Icons/Windows/iconLogo150.scale-400.png'                      = 'png'
        }

        $cases = @(
            @{
                Description = 'a package carrying every asset its manifest declares'
                Entries     = $allAssets
                Expected    = $null
            }
            @{
                Description = 'a package carrying only MRT-qualified variants, as the tool emits them'
                Entries     = $mrtAssets
                Expected    = $null
            }
            @{
                # One variant missing is not a defect: the resource index resolves another. Rejecting it
                # would make the gate fail on packages that work.
                Description = 'a family missing one variant but otherwise complete'
                Entries     = @{
                    'AppxManifest.xml'                                = New-TestManifest
                    'cli/salmon-egg.exe'                              = 'MZ'
                    'Assets/Icons/Windows/iconLogo.scale-200.png'      = 'png'
                    'Assets/Icons/Windows/iconLogo44.scale-100.png'    = 'png'
                    'Assets/Icons/Windows/iconLogo150.scale-400.png'   = 'png'
                }
                Expected    = $null
            }
            @{
                # An asset-item regression removes the whole family, which is the defect that ships a
                # package showing the generic placeholder icon.
                Description = 'a package whose entire qualified logo family was dropped'
                Entries     = @{
                    'AppxManifest.xml'                                = New-TestManifest
                    'cli/salmon-egg.exe'                              = 'MZ'
                    'Assets/Icons/Windows/iconLogo.scale-200.png'      = 'png'
                    'Assets/Icons/Windows/iconLogo150.scale-400.png'   = 'png'
                }
                Expected    = 'DeclaredAssetMissing'
            }
            @{
                # A qualifier must not be able to satisfy a different asset: iconLogo150 is not a variant
                # of iconLogo, even though one name is a prefix of the other.
                Description = 'a package where a similarly-named asset stands in for a missing one'
                Entries     = @{
                    'AppxManifest.xml'                                = New-TestManifest
                    'cli/salmon-egg.exe'                              = 'MZ'
                    'Assets/Icons/Windows/iconLogo150.scale-200.png'   = 'png'
                    'Assets/Icons/Windows/iconLogo44.scale-200.png'    = 'png'
                }
                Expected    = 'DeclaredAssetMissing'
            }
            @{
                # The exact defect the Assets ItemGroup in SalmonEgg.csproj exists to prevent: build
                # succeeds, package is well-formed, shell logo is simply absent.
                Description = 'a package whose declared shell logo was never included'
                Entries     = @{
                    'AppxManifest.xml'                     = New-TestManifest
                    'cli/salmon-egg.exe'                   = 'MZ'
                    'Assets/Icons/Windows/iconLogo44.png'   = 'png'
                    'Assets/Icons/Windows/iconLogo150.png'  = 'png'
                }
                Expected    = 'DeclaredAssetMissing'
            }
            @{
                Description = 'a package whose VisualElements tile logo was never included'
                Entries     = @{
                    'AppxManifest.xml'                     = New-TestManifest
                    'cli/salmon-egg.exe'                   = 'MZ'
                    'Assets/Icons/Windows/iconLogo.png'     = 'png'
                    'Assets/Icons/Windows/iconLogo44.png'   = 'png'
                }
                Expected    = 'DeclaredAssetMissing'
            }
            @{
                # The alias resolves a package-relative path, so registering the command while the publish
                # never carried the binary produces an alias that starts nothing.
                Description = 'a package registering the alias whose CLI payload was never included'
                Entries     = @{
                    'AppxManifest.xml'                     = New-TestManifest
                    'Assets/Icons/Windows/iconLogo.png'     = 'png'
                    'Assets/Icons/Windows/iconLogo44.png'   = 'png'
                    'Assets/Icons/Windows/iconLogo150.png'  = 'png'
                }
                Expected    = 'AppExecutionAliasTargetMissing'
            }
            @{
                # Shipping the binary without the alias is the other half of the same defect: the package
                # is complete and the command is still not on PATH.
                Description = 'a package carrying the CLI but registering no alias for it'
                Entries     = (Merge-TestEntries -Base $allAssets -Override @{ 'AppxManifest.xml' = New-TestManifest -OmitAlias })
                Expected    = 'AppExecutionAliasMissing'
            }
            @{
                Description = 'a package whose alias name drifted from the product command'
                Entries     = (Merge-TestEntries -Base $allAssets -Override @{ 'AppxManifest.xml' = New-TestManifest -Alias 'salmonegg.exe' })
                Expected    = 'AppExecutionAliasNameMismatch'
            }
            @{
                # Without Windows.FullTrustApplication the alias becomes a UWP activation, which drops the
                # caller's console, arguments and exit code: a command that appears to do nothing.
                Description = 'a package whose alias activates the app instead of launching the command'
                Entries     = (Merge-TestEntries -Base $allAssets -Override @{ 'AppxManifest.xml' = New-TestManifest -AliasEntryPoint 'SalmonEgg.App' })
                Expected    = 'AppExecutionAliasEntryPointMismatch'
            }
            @{
                # Two extensions mean registration order, not this manifest, decides which command the
                # user ends up with.
                Description = 'a package registering two alias extensions'
                Entries     = (Merge-TestEntries -Base $allAssets -Override @{ 'AppxManifest.xml' = New-TestManifest -DuplicateAlias })
                Expected    = 'AppExecutionAliasAmbiguous'
            }
            @{
                # The content item's TargetPath and the manifest's Executable have to agree; a payload
                # present under some other path packages cleanly and then fails to launch.
                Description = 'a package whose alias path disagrees with where the CLI was placed'
                Entries     = (Merge-TestEntries -Base $allAssets -Override @{ 'AppxManifest.xml' = New-TestManifest -AliasExecutable 'salmon-egg.exe' })
                Expected    = 'AppExecutionAliasTargetMissing'
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

Write-Host "[msix-gate] passed: $Package satisfies identity, version, declared-asset presence, and the bundled CLI's execution alias"
