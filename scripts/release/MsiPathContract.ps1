#requires -Version 7.0
<#
.SYNOPSIS
    The PATH-registration contract a built SalmonEgg MSI must satisfy.

.DESCRIPTION
    This lives apart from the scripts that build MSIs because reading an MSI needs Windows and WiX, while
    the rule being enforced is pure string logic over one Environment-table row. Splitting them lets
    scripts/gates/run-msi-path-contract-gate.ps1 exercise the rule — including every failure case — on
    any platform, instead of the rule only ever running inside a release build nobody can rehearse.

    The rule is not specific to one package: the directory whose token must appear in the value is a
    parameter, because the desktop MSI installs the command into a `cli` subdirectory of the app while the
    CLI-only MSI made its install folder the command directory itself. Everything else — which variable,
    which prefix characters, where the null marker sits — is the same contract either way.

    Encoding per the MSI Environment table reference:
    https://learn.microsoft.com/windows/win32/msi/environment-table
      - Name carries prefix characters, and the reference states there is "no effect in the ordering of
        the symbols used in a prefix": "=" set on install, "+" create but leave an existing value alone,
        "-" remove when the component is removed, "!" remove during installation, "*" target the machine
        environment instead of the user's. "=-" is documented as "the usual behavior".
      - Value must lead with the "[~]" null marker plus the separator to append to an existing variable
        ("[~];Value"); the trailing form ("Value;[~]") prepends instead. With no "[~]" at all the
        reference is explicit that "the existing path information is lost and installing the .msi file
        may prevent the computer from booting".

    WiX v3 composes both columns itself (wix3 src/tools/wix/Compiler.cs, ParseEnvironmentElement):
    Name = Action prefix + uninstall marker + System marker + Name, and Part="last" rewrites Value to
    "[~]" + Separator + Value. So the authored element must not spell "[~]" itself, and Environment is a
    core WiX element parsed by the main compiler — it needs no WixUtilExtension.
#>

Set-StrictMode -Version Latest

# Prefix characters Windows Installer recognises on an Environment row's Name.
$script:MsiPathNamePrefixCharacters = [char[]]@('=', '+', '-', '!', '*')

$script:MsiPathVariable = 'PATH'
$script:MsiPathNullMarker = '[~]'

function Split-MsiEnvironmentName
{
    <#
    .SYNOPSIS
        Splits an Environment-table Name into its prefix characters and the variable name.
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Name
    )

    $prefixLength = 0
    while ($prefixLength -lt $Name.Length -and
           $script:MsiPathNamePrefixCharacters -contains $Name[$prefixLength])
    {
        $prefixLength++
    }

    return @{
        Prefix   = $Name.Substring(0, $prefixLength)
        Variable = $Name.Substring($prefixLength)
    }
}

function New-MsiPathContractViolation
{
    [CmdletBinding()]
    [OutputType([psobject])]
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Message
    )

    return [pscustomobject]@{ Id = $Id; Message = $Message }
}

function Get-MsiPathContractViolation
{
    <#
    .SYNOPSIS
        Returns the first contract violation for one Environment-table row, or $null when it conforms.

    .DESCRIPTION
        Checks are ordered most-specific-first so each non-conforming row maps to exactly one stable Id,
        which is what lets the gate assert a precise diagnosis instead of merely "something threw".
    #>
    [CmdletBinding()]
    [OutputType([psobject])]
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Name,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value,
        # The directory the command lives in, as the MSI formats it: '[INSTALLFOLDER]' when the install
        # folder is itself the command directory, '[CLIFOLDER]' when the command sits in a subdirectory of
        # a larger package. Passing it in is what keeps this rule usable by more than one package.
        [Parameter(Mandatory = $true)][string]$DirectoryToken
    )

    $parsed = Split-MsiEnvironmentName -Name $Name
    $prefix = $parsed.Prefix
    $variable = $parsed.Variable

    # Windows environment variable names are case-insensitive, so 'Path' is the same variable as 'PATH'.
    if ($variable -ine $script:MsiPathVariable)
    {
        return New-MsiPathContractViolation 'UnexpectedVariable' (
            "the row targets '$variable' rather than $($script:MsiPathVariable), so the CLI would not " +
            'become discoverable.')
    }

    if ($prefix.Contains('*'))
    {
        return New-MsiPathContractViolation 'MachineEnvironment' (
            'the row targets the machine environment, but this is a per-user package installed with ' +
            'limited privileges; the write would fail or affect every account on the machine.')
    }

    if ($prefix.Contains('!'))
    {
        return New-MsiPathContractViolation 'RemovedOnInstall' (
            'the row removes the variable during installation instead of setting it.')
    }

    if (-not $prefix.Contains('='))
    {
        return New-MsiPathContractViolation 'NotSetOnInstall' (
            'the row is not set on install, so the install folder would never reach PATH.')
    }

    if (-not $prefix.Contains('-'))
    {
        return New-MsiPathContractViolation 'NotRemovedOnUninstall' (
            'the row is permanent, so uninstalling would leave the install folder on PATH forever.')
    }

    if (-not $Value.Contains($script:MsiPathNullMarker))
    {
        return New-MsiPathContractViolation 'ReplacesExistingValue' (
            "the value carries no $($script:MsiPathNullMarker) marker, so it replaces PATH wholesale " +
            'rather than extending it — the reference warns this can leave a machine unbootable.')
    }

    # Leading marker plus separator is the documented append form. The trailing form prepends, which would
    # let this install folder shadow every tool the user already has on PATH.
    if (-not $Value.StartsWith($script:MsiPathNullMarker, [System.StringComparison]::Ordinal) -or
        $Value.Length -le $script:MsiPathNullMarker.Length)
    {
        return New-MsiPathContractViolation 'NotAppended' (
            "the value does not lead with $($script:MsiPathNullMarker) plus a separator, so it does " +
            'not append the install folder to the end of the existing PATH.')
    }

    if (-not $Value.Contains($DirectoryToken))
    {
        return New-MsiPathContractViolation 'MissingDirectoryToken' (
            "the value does not reference $DirectoryToken, so whatever it adds to PATH is not the " +
            'directory this package installs the command into.')
    }

    return $null
}

function Assert-MsiPathContract
{
    <#
    .SYNOPSIS
        Throws unless the Environment-table row registers PATH the way this package requires.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Name,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value,
        [Parameter(Mandatory = $true)][string]$DirectoryToken
    )

    $violation = Get-MsiPathContractViolation -Name $Name -Value $Value -DirectoryToken $DirectoryToken
    if ($null -ne $violation)
    {
        throw ("MSI PATH registration contract violated [$($violation.Id)]: $($violation.Message) " +
               "Name='$Name' Value='$Value'")
    }
}
