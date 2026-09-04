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

    # Returns a value, like the real COM View.Execute does, instead of being [void]. That difference is
    # what let the v1.3.0 rehearsal fail: a bare $view.Execute() inside a function emits its return into
    # the function's output stream, so Get-MsiColumn returned [Execute's value, the array] and the
    # caller's [string[]] cast flattened 400+ names into one space-joined cell. A [void] fake cannot
    # reproduce that, so it certified a rule the real database breaks.
    [object] Execute() { $this.Executed = $true; $this.Cursor = 0; return $null }
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
    [object[]]$EnvironmentRows
    [System.Collections.Generic.List[string]]$Queries

    # The two-argument form carries the PATH row a correct package has, so a case only spells the
    # Environment table out when the row itself is what it is testing.
    FakeMsiDatabase([string[]]$fileNames, [string]$productVersion)
    {
        $this.FileNames = $fileNames
        $this.ProductVersion = $productVersion
        $this.EnvironmentRows = @([pscustomobject]@{ Name = '=-PATH'; Value = '[~];[CLIFOLDER]' })
        $this.Queries = [System.Collections.Generic.List[string]]::new()
    }

    FakeMsiDatabase([string[]]$fileNames, [string]$productVersion, [object[]]$environmentRows)
    {
        $this.FileNames = $fileNames
        $this.ProductVersion = $productVersion
        $this.EnvironmentRows = $environmentRows
        $this.Queries = [System.Collections.Generic.List[string]]::new()
    }

    [object] OpenView([string]$query)
    {
        $this.Queries.Add($query)

        # Windows Installer parses the query here and rejects anything outside its grammar. LIKE is in that
        # rejected set -- the fake used to implement it, which is exactly how the v1.3.0 retag still shipped
        # a query the real database refuses.
        if ($query -match '(?i)\b(?:COUNT|SUM|AVG|MIN|MAX)\s*\(' -or
            $query -match '(?i)\b(?:GROUP\s+BY|ORDER\s+BY|DISTINCT|JOIN|LIKE)\b')
        {
            throw 'OpenView,Sql'
        }

        if ($query -match '(?i)FROM\s+`File`')
        {
            $rows = @()
            foreach ($name in $this.FileNames)
            {
                $rows += [FakeMsiRecord]::new(@($name))
            }

            return [FakeMsiView]::new($rows)
        }

        if ($query -match '(?i)FROM\s+`Environment`')
        {
            $rows = @()
            foreach ($pair in $this.EnvironmentRows)
            {
                $rows += [FakeMsiRecord]::new(@($pair.Name, $pair.Value))
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
        FileNames   = @('SalmonEgg.exe', 'salmon-egg.exe', 'SalmonEgg.dll', 'SkiaSharp.dll')
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
        # heat emits 'SHORT~1.EXE|Long.exe' whenever a name needs an 8.3 alias, so the rule has to read
        # both halves. Matching the whole cell would miss the executable and fail for the wrong reason.
        Description = 'a package whose File rows carry short|long name pairs'
        FileNames   = @('SALMON~1.EXE|SalmonEgg.exe', 'SALMON~2.EXE|salmon-egg.exe', 'SKIASH~1.DLL|SkiaSharp.dll')
        Version     = '1.3.0'
        Expected    = $null
    }
    @{
        Description = 'a package whose short|long pairs never name the app executable'
        FileNames   = @('SKIASH~1.DLL|SkiaSharp.dll', 'SALMON~2.DLL|SalmonEgg.dll')
        Version     = '1.3.0'
        Expected    = 'MissingAppExe'
    }
    @{
        # Substring matching would accept this; the executable is SalmonEgg.exe, not a name ending in it.
        Description = 'a package shipping a lookalike whose name merely ends with the executable name'
        FileNames   = @('NotSalmonEgg.exe', 'SkiaSharp.dll')
        Version     = '1.3.0'
        Expected    = 'MissingAppExe'
    }
    @{
        # A real File table has rows whose FileName cell is empty. Reading them is what broke the third
        # v1.3.0 attempt: PowerShell refuses to bind a [string[]] containing an empty string unless the
        # parameter declares AllowEmptyString. No fake produced one, so no gate could have caught it.
        Description = 'a package whose File table carries empty FileName cells alongside real ones'
        FileNames   = @('', 'SalmonEgg.exe', '', 'salmon-egg.exe', 'SkiaSharp.dll')
        Version     = '1.3.0'
        Expected    = $null
    }
    @{
        Description = 'a package of nothing but empty FileName cells'
        FileNames   = @('', '', '')
        Version     = '1.3.0'
        Expected    = 'MissingAppExe'
    }
    @{
        Description = 'a package whose ProductVersion was never substituted'
        FileNames   = @('SalmonEgg.exe', 'salmon-egg.exe')
        Version     = '$(SalmonEggDisplayVersion)'
        Expected    = 'InvalidVersion'
    }
    @{
        Description = 'a package carrying a four-part version MajorUpgrade cannot compare'
        FileNames   = @('SalmonEgg.exe', 'salmon-egg.exe')
        Version     = '1.3.0.0'
        Expected    = 'InvalidVersion'
    }
    @{
        Description = 'a package with no ProductVersion row at all'
        FileNames   = @('SalmonEgg.exe', 'salmon-egg.exe')
        Version     = ''
        Expected    = 'InvalidVersion'
    }
    @{
        # The defect the bundled-CLI publish exists to prevent: the app installs, the PATH row names a cli
        # directory, and there is nothing in it to run.
        Description = 'a package that installs the app but never carried the command'
        FileNames   = @('SalmonEgg.exe', 'SkiaSharp.dll')
        Version     = '1.3.0'
        Expected    = 'MissingCommandExe'
    }
    @{
        Description = 'a package shipping the command with no PATH row at all'
        FileNames   = @('SalmonEgg.exe', 'salmon-egg.exe')
        Version     = '1.3.0'
        Environment = @()
        Expected    = 'NoPathRegistration'
    }
    @{
        # A second environment write reaching the package would be applied too, unreviewed.
        Description = 'a package carrying a second, unreviewed environment row'
        FileNames   = @('SalmonEgg.exe', 'salmon-egg.exe')
        Version     = '1.3.0'
        Environment = @(
            [pscustomobject]@{ Name = '=-PATH'; Value = '[~];[CLIFOLDER]' },
            [pscustomobject]@{ Name = '=-*PATH'; Value = '[~];[CLIFOLDER]' }
        )
        Expected    = 'MultiplePathRegistrations'
    }
    @{
        # Identifiers from the shared PATH rule pass through, so the diagnosis names the encoding defect
        # rather than a generic "bad PATH row".
        Description = 'a package whose PATH row prepends the command directory'
        FileNames   = @('SalmonEgg.exe', 'salmon-egg.exe')
        Version     = '1.3.0'
        Environment = @([pscustomobject]@{ Name = '=-PATH'; Value = '[CLIFOLDER];[~]' })
        Expected    = 'NotAppended'
    }
    @{
        Description = 'a package whose PATH row survives uninstall'
        FileNames   = @('SalmonEgg.exe', 'salmon-egg.exe')
        Version     = '1.3.0'
        Environment = @([pscustomobject]@{ Name = '=PATH'; Value = '[~];[CLIFOLDER]' })
        Expected    = 'NotRemovedOnUninstall'
    }
    @{
        # Registering the app folder rather than the cli subdirectory puts every shipped DLL on PATH and
        # still leaves the command unresolvable.
        Description = 'a package registering the app folder instead of the command folder'
        FileNames   = @('SalmonEgg.exe', 'salmon-egg.exe')
        Version     = '1.3.0'
        Environment = @([pscustomobject]@{ Name = '=-PATH'; Value = '[~];[INSTALLFOLDER]' })
        Expected    = 'MissingDirectoryToken'
    }
)

foreach ($case in $cases)
{
    # ContainsKey rather than a null check: Set-StrictMode rejects reading a key a hashtable does not have.
    $database = if ($case.ContainsKey('Environment'))
    {
        [FakeMsiDatabase]::new($case.FileNames, $case.Version, $case.Environment)
    }
    else
    {
        [FakeMsiDatabase]::new($case.FileNames, $case.Version)
    }

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
# A conforming package, so the contract runs to the end and issues every query it has -- including the
# Environment one. A package rejected early would leave the later queries unchecked.
$database = [FakeMsiDatabase]::new(@('SalmonEgg.exe', 'salmon-egg.exe'), '1.3.0')
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

# The check above trusts Get-MsiQueryViolation. This one does not: it replays each issued query through the
# database whose OpenView rejects out-of-grammar SQL the way Windows Installer's does. A query the pattern
# list happens to miss still fails here.
foreach ($query in $database.Queries)
{
    try
    {
        [void]([FakeMsiDatabase]::new(@('SalmonEgg.exe', 'salmon-egg.exe'), '1.3.0')).OpenView($query)
    }
    catch
    {
        Add-Failure "OpenView rejected a query the contract issues: '$query' -> $($_.Exception.Message)"
    }
}

Write-Host '[desktop-msi-gate] OpenView accepted every issued query'

# Reverse verification: the query shape that broke the v1.3.0 release must still be rejected, and it must
# be rejected before reaching OpenView so the failure names the cause instead of surfacing 'OpenView,Sql'.
$unsupported = @(
    @{ Query = 'SELECT COUNT(*) FROM `File`'; Expected = 'AggregateFunction' }
    @{ Query = 'SELECT MAX(`Version`) FROM `File`'; Expected = 'AggregateFunction' }
    @{ Query = 'SELECT `File` FROM `File` ORDER BY `Sequence`'; Expected = 'OrderBy' }
    @{ Query = 'SELECT DISTINCT `Component_` FROM `File`'; Expected = 'Distinct' }
    @{ Query = 'SELECT `FileName` FROM `File` GROUP BY `Component_`'; Expected = 'GroupBy' }
    @{ Query = 'SELECT `FileName` FROM `File` JOIN `Component`'; Expected = 'Join' }
    # The second query shape to take down a v1.3.0 release build. The grammar allows only = and <> for
    # string comparison, so suffix matching has to happen in PowerShell after the rows come back.
    @{ Query = "SELECT ``FileName`` FROM ``File`` WHERE ``FileName`` LIKE '%SalmonEgg.exe'"; Expected = 'Like' }
    @{ Query = 'SELECT `Value` FROM `Property` WHERE `Property` like ?'; Expected = 'Like' }
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

# The diagnostic runs before the verdict on every real build, so it must survive every cell shape the
# File table produces -- including the empty ones that broke the third attempt. A diagnostic that throws
# would replace a named violation with a crash.
foreach ($shape in @(, @()), @(, @('')), @(, @('', 'SalmonEgg.exe')), @(, @('SALMON~1.EXE|SalmonEgg.exe')))
{
    try
    {
        Write-MsiFileTableShape -FileNames $shape[0] -SampleSize 2 6>$null
    }
    catch
    {
        Add-Failure "the File-table diagnostic threw on a $($shape[0].Count)-row shape: $($_.Exception.Message)"
    }
}

Write-Host '[desktop-msi-gate] the File-table diagnostic survives empty, paired and absent cells'

# Assert-DesktopMsiContract is what the release step calls, so verify it throws on a violation and lets a
# conforming package through -- a silent Get-* consumer would defeat the whole gate.
try
{
    Assert-DesktopMsiContract -Database ([FakeMsiDatabase]::new(@('SalmonEgg.exe', 'salmon-egg.exe', 'SalmonEgg.dll'), '1.3.0'))
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
            'cases, query-expressibility through both the pattern list and OpenView, row-counting checks, ' +
            'plus 2 assertion-surface checks')
