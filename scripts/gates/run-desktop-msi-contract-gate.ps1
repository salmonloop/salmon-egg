#requires -Version 7.0
<#
.SYNOPSIS
    Exercises the desktop MSI package contract, including every failure case, on any platform.

.DESCRIPTION
    The real check reads a built MSI through WindowsInstaller.Installer COM, which needs Windows, WiX and
    a `v*` tag. That made the rule unrehearsable, and it shipped wrong: the row count was written as
    `SELECT COUNT(*)`, which Windows Installer's SQL dialect cannot parse, so the v1.3.0 release build
    died inside OpenView instead of asserting anything.

    This gate drives DesktopMsiContract.ps1 against fake databases that reproduce the two behaviours that
    matter -- Fetch walks rows then returns $null, and OpenView rejects unsupported SQL the way Windows
    Installer does -- and hard-asserts the violation identifier each case produces. No COM, no WiX, no MSI.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
. (Join-Path $repoRoot 'scripts/release/DesktopMsiContract.ps1')

class FakeMsiRecord
{
    [string[]]$Fields
    FakeMsiRecord([string[]]$fields) { $this.Fields = $fields }
    [string] StringData([int]$oneBasedColumn) { return $this.Fields[$oneBasedColumn - 1] }
}

class FakeMsiView
{
    [object[]]$Rows
    [int]$Cursor = 0
    [bool]$Executed = $false
    FakeMsiView([object[]]$rows) { $this.Rows = $rows }
    [void] Execute() { $this.Executed = $true; $this.Cursor = 0 }
    [object] Fetch()
    {
        if (-not $this.Executed) { throw 'Fetch before Execute' }
        if ($this.Cursor -ge $this.Rows.Count) { return $null }
        $row = $this.Rows[$this.Cursor]
        $this.Cursor++
        return $row
    }
}

# Stands in for the database COM object. Notably it rejects aggregate SQL inside OpenView, exactly as
# Windows Installer does -- that is the behaviour the shipped rule was wrong about.
class FakeMsiDatabase
{
    [string[]]$FileNames
    [string]$ProductVersion
    [System.Collections.Generic.List[string]]$Queries

    FakeMsiDatabase([string[]]$fileNames, [string]$productVersion)
    {
        $this.FileNames = $fileNames
        $this.ProductVersion = $productVersion
        $this.Queries = [System.Collections.Generic.List[string]]::new()
    }

    [object] OpenView([string]$query)
    {
        $this.Queries.Add($query)

        if ($query -match '(?i)\b(?:COUNT|SUM|AVG|MIN|MAX)\s*\(' -or
            $query -match '(?i)\b(?:GROUP\s+BY|ORDER\s+BY|DISTINCT|JOIN)\b')
        {
            throw 'OpenView,Sql'
        }

        if ($query -match '(?i)FROM\s+`File`')
        {
            $rows = @()
            foreach ($name in $this.FileNames)
            {
                if ($query -match "(?i)LIKE\s+'%(?<suffix>[^']+)'")
                {
                    if (-not $name.EndsWith($Matches.suffix, [StringComparison]::Ordinal)) { continue }
                }

                $rows += [FakeMsiRecord]::new(@($name))
            }

            return [FakeMsiView]::new($rows)
        }

        if ($query -match '(?i)FROM\s+`Property`')
        {
            if ([string]::IsNullOrEmpty($this.ProductVersion)) { return [FakeMsiView]::new(@()) }
            return [FakeMsiView]::new(@([FakeMsiRecord]::new(@($this.ProductVersion))))
        }

        return [FakeMsiView]::new(@())
    }
}

$failures = @()

function Add-Failure { param([string]$Message) $script:failures += $Message }

$cases = @(
    @{
        Description = 'the package a successful harvest produces'
        FileNames   = @('SalmonEgg.exe', 'SalmonEgg.dll', 'SkiaSharp.dll')
        Version     = '1.3.0'
        Expected    = $null
    }
    @{
        # The reason this gate exists: candle and light both succeed on an empty package.
        Description = 'a package whose harvest picked up nothing'
        FileNames   = @()
        Version     = '1.3.0'
        Expected    = 'EmptyPackage'
    }
    @{
        Description = 'a package with files but without the app executable'
        FileNames   = @('SalmonEgg.dll', 'SkiaSharp.dll')
        Version     = '1.3.0'
        Expected    = 'MissingAppExe'
    }
    @{
        Description = 'a package whose ProductVersion was never substituted'
        FileNames   = @('SalmonEgg.exe')
        Version     = '$(SalmonEggDisplayVersion)'
        Expected    = 'InvalidVersion'
    }
    @{
        Description = 'a package carrying a four-part version MajorUpgrade cannot compare'
        FileNames   = @('SalmonEgg.exe')
        Version     = '1.3.0.0'
        Expected    = 'InvalidVersion'
    }
    @{
        Description = 'a package with no ProductVersion row at all'
        FileNames   = @('SalmonEgg.exe')
        Version     = ''
        Expected    = 'InvalidVersion'
    }
)

foreach ($case in $cases)
{
    $database = [FakeMsiDatabase]::new($case.FileNames, $case.Version)
    $violation = Get-DesktopMsiContractViolation -Database $database
    $actual = if ($null -eq $violation) { $null } else { $violation.Id }

    if ($actual -ne $case.Expected)
    {
        $expectedLabel = if ($null -eq $case.Expected) { '(conforming)' } else { $case.Expected }
        $actualLabel = if ($null -eq $actual) { '(conforming)' } else { $actual }
        Add-Failure "$($case.Description): expected $expectedLabel but got $actualLabel"
        continue
    }

    $outcome = if ($null -eq $actual) { 'conforms' } else { "rejected as $actual" }
    Write-Host "[desktop-msi-gate] $($case.Description): $outcome"
}

# Every query the contract issues must be one Windows Installer can parse. Asserting on the queries the
# conforming run actually made is what catches a future `SELECT COUNT(*)` on the pushing commit.
$database = [FakeMsiDatabase]::new(@('SalmonEgg.exe'), '1.3.0')
[void](Get-DesktopMsiContractViolation -Database $database)
if ($database.Queries.Count -lt 1)
{
    Add-Failure 'the contract issued no queries at all'
}

foreach ($query in $database.Queries)
{
    $queryViolation = Get-MsiQueryViolation -Query $query
    if ($null -ne $queryViolation)
    {
        Add-Failure "the contract issues SQL Windows Installer cannot parse ($($queryViolation.Id)): '$query'"
    }
}

Write-Host "[desktop-msi-gate] every one of $($database.Queries.Count) issued queries is expressible in MSI SQL"

# Reverse verification: the query shape that broke the v1.3.0 release must still be rejected, and it must
# be rejected before reaching OpenView so the failure names the cause instead of surfacing 'OpenView,Sql'.
$unsupported = @(
    @{ Query = 'SELECT COUNT(*) FROM `File`'; Expected = 'AggregateFunction' }
    @{ Query = 'SELECT MAX(`Version`) FROM `File`'; Expected = 'AggregateFunction' }
    @{ Query = 'SELECT `File` FROM `File` ORDER BY `Sequence`'; Expected = 'OrderBy' }
    @{ Query = 'SELECT DISTINCT `Component_` FROM `File`'; Expected = 'Distinct' }
    @{ Query = 'SELECT `FileName` FROM `File` GROUP BY `Component_`'; Expected = 'GroupBy' }
    @{ Query = 'SELECT `FileName` FROM `File` JOIN `Component`'; Expected = 'Join' }
)

foreach ($case in $unsupported)
{
    $queryViolation = Get-MsiQueryViolation -Query $case.Query
    $actual = if ($null -eq $queryViolation) { $null } else { $queryViolation.Id }
    if ($actual -ne $case.Expected)
    {
        Add-Failure "'$($case.Query)' should be rejected as $($case.Expected) but got $(if ($null -eq $actual) { '(accepted)' } else { $actual })"
        continue
    }

    Write-Host "[desktop-msi-gate] '$($case.Query)': rejected as $actual"
}

# Measure-MsiRows is the replacement for the aggregate query, so prove it counts what is really there and
# that it refuses the aggregate form rather than silently returning something.
foreach ($count in 0, 1, 7, 250)
{
    $names = @(1..$count | ForEach-Object { "file$_.dll" })
    if ($count -eq 0) { $names = @() }
    $database = [FakeMsiDatabase]::new($names, '1.3.0')
    $measured = Measure-MsiRows -Database $database -Query 'SELECT `File` FROM `File`'
    if ($measured -ne $count)
    {
        Add-Failure "Measure-MsiRows counted $measured of $count file rows"
    }
}

Write-Host '[desktop-msi-gate] Measure-MsiRows counted 0, 1, 7 and 250 rows correctly'

$aggregateThrew = $false
try
{
    $database = [FakeMsiDatabase]::new(@('SalmonEgg.exe'), '1.3.0')
    [void](Measure-MsiRows -Database $database -Query 'SELECT COUNT(*) FROM `File`')
}
catch
{
    $aggregateThrew = $true
    if ($_.Exception.Message -notlike '*AggregateFunction*')
    {
        Add-Failure "the aggregate query threw without naming the cause: $($_.Exception.Message)"
    }

    if ($database.Queries.Count -ne 0)
    {
        Add-Failure 'the aggregate query reached OpenView instead of being rejected first'
    }
}

if (-not $aggregateThrew)
{
    Add-Failure 'Measure-MsiRows accepted the aggregate query that broke the v1.3.0 release'
}

Write-Host '[desktop-msi-gate] the aggregate query that broke v1.3.0 is rejected before reaching OpenView'

# Assert-DesktopMsiContract is what the release step calls, so verify it throws on a violation and lets a
# conforming package through -- a silent Get-* consumer would defeat the whole gate.
try
{
    Assert-DesktopMsiContract -Database ([FakeMsiDatabase]::new(@('SalmonEgg.exe', 'SalmonEgg.dll'), '1.3.0'))
}
catch
{
    Add-Failure "Assert-DesktopMsiContract threw for a conforming package: $($_.Exception.Message)"
}

$assertThrew = $false
try
{
    Assert-DesktopMsiContract -Database ([FakeMsiDatabase]::new(@(), '1.3.0'))
}
catch
{
    $assertThrew = $true
    if ($_.Exception.Message -notlike '*EmptyPackage*')
    {
        Add-Failure "Assert-DesktopMsiContract threw without naming the violation: $($_.Exception.Message)"
    }
}

if (-not $assertThrew)
{
    Add-Failure 'Assert-DesktopMsiContract accepted a package that installs no files.'
}

if ($failures.Count -gt 0)
{
    Write-Host ''
    foreach ($failure in $failures)
    {
        Write-Host "[desktop-msi-gate] FAIL $failure"
    }

    throw "Desktop MSI contract gate failed with $($failures.Count) violation(s)."
}

Write-Host ("[desktop-msi-gate] passed: $($cases.Count) package cases, $($unsupported.Count) unsupported-SQL " +
            'cases, query-expressibility and row-counting checks, plus 2 assertion-surface checks')
