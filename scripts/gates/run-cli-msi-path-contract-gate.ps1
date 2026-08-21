#requires -Version 7.0
<#
.SYNOPSIS
    Exercises the SalmonEgg CLI MSI PATH-registration contract, including every failure case.

.DESCRIPTION
    build-cli-msi.ps1 can only run its assertion while building a real MSI, which needs Windows and WiX.
    That makes the assertion itself unrehearsable: nobody finds out it is wrong until a release build
    either fails for the wrong reason or, worse, passes a package that overwrites the user's PATH.

    This gate drives the same rule directly with hand-written Environment-table rows and hard-asserts the
    exact violation identifier each one produces, so a check that gets weakened or dropped fails here
    rather than in a tag build. Pure string logic — no COM, no WiX, runs on any platform with pwsh.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
. (Join-Path $repoRoot 'scripts/release/CliMsiPathContract.ps1')

# Name/Value pairs exactly as WiX v3 emits them: Name = Action + uninstall + System + variable, and
# Part="last" rewrites Value to "[~]" + Separator + Value. The conforming row is what
# Action="set" Part="last" System="no" Permanent="no" on Name="PATH" Value="[INSTALLFOLDER]" produces.
$conformingName = '=-PATH'
$conformingValue = '[~];[INSTALLFOLDER]'

$cases = @(
    @{
        Description = 'the row WiX emits for the authored Environment element'
        Name        = $conformingName
        Value       = $conformingValue
        Expected    = $null
    }
    @{
        # The reference states there is "no effect in the ordering of the symbols used in a prefix", so a
        # reordered prefix must not be reported as a violation.
        Description = 'a conforming row whose prefix characters are reordered'
        Name        = '-=PATH'
        Value       = $conformingValue
        Expected    = $null
    }
    @{
        Description = 'a value that replaces PATH instead of extending it'
        Name        = $conformingName
        Value       = '[INSTALLFOLDER]'
        Expected    = 'ReplacesExistingValue'
    }
    @{
        Description = 'a value that prepends, letting the install folder shadow the user tools'
        Name        = $conformingName
        Value       = '[INSTALLFOLDER];[~]'
        Expected    = 'NotAppended'
    }
    @{
        Description = 'a row written to the machine environment'
        Name        = '=-*PATH'
        Value       = $conformingValue
        Expected    = 'MachineEnvironment'
    }
    @{
        Description = 'a permanent row that uninstall would leave behind'
        Name        = '=PATH'
        Value       = $conformingValue
        Expected    = 'NotRemovedOnUninstall'
    }
    @{
        Description = 'a row targeting a different variable'
        Name        = '=-PATHEXT'
        Value       = $conformingValue
        Expected    = 'UnexpectedVariable'
    }
    @{
        Description = 'a row that removes the variable during installation'
        Name        = '!-PATH'
        Value       = $conformingValue
        Expected    = 'RemovedOnInstall'
    }
    @{
        Description = 'a create-only row that never sets the value'
        Name        = '+-PATH'
        Value       = $conformingValue
        Expected    = 'NotSetOnInstall'
    }
    @{
        Description = 'a row appending some directory other than the install folder'
        Name        = $conformingName
        Value       = '[~];C:\Tools'
        Expected    = 'MissingInstallFolder'
    }
)

$failures = @()
foreach ($case in $cases) {
    $violation = Get-CliMsiPathContractViolation -Name $case.Name -Value $case.Value
    $actual = if ($null -eq $violation) { $null } else { $violation.Id }

    if ($actual -ne $case.Expected) {
        $expectedLabel = if ($null -eq $case.Expected) { '(conforming)' } else { $case.Expected }
        $actualLabel = if ($null -eq $actual) { '(conforming)' } else { $actual }
        $failures += ("$($case.Description): expected $expectedLabel but got $actualLabel " +
                      "(Name='$($case.Name)' Value='$($case.Value)')")
        continue
    }

    $outcome = if ($null -eq $actual) { 'conforms' } else { "rejected as $actual" }
    Write-Host "[cli-msi-gate] $($case.Description): $outcome"
}

# Assert-CliMsiPathContract is what build-cli-msi.ps1 actually calls, so verify it converts a violation
# into a throw and lets a conforming row through — a silent Get-* consumer would defeat the whole gate.
try {
    Assert-CliMsiPathContract -Name $conformingName -Value $conformingValue
}
catch {
    $failures += "Assert-CliMsiPathContract threw for the conforming row: $($_.Exception.Message)"
}

$assertThrew = $false
try {
    Assert-CliMsiPathContract -Name $conformingName -Value '[INSTALLFOLDER]'
}
catch {
    $assertThrew = $true
    if ($_.Exception.Message -notlike '*ReplacesExistingValue*') {
        $failures += ("Assert-CliMsiPathContract threw without naming the violation: " +
                      "$($_.Exception.Message)")
    }
}

if (-not $assertThrew) {
    $failures += 'Assert-CliMsiPathContract accepted a row that replaces PATH wholesale.'
}

if ($failures.Count -gt 0) {
    Write-Host ''
    foreach ($failure in $failures) {
        Write-Host "[cli-msi-gate] FAIL $failure"
    }

    throw "CLI MSI PATH contract gate failed with $($failures.Count) violation(s)."
}

Write-Host "[cli-msi-gate] passed: $($cases.Count) contract cases plus 2 assertion-surface checks"
