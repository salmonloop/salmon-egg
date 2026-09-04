#requires -Version 7.0
<#
.SYNOPSIS
    The contract the SalmonEgg desktop MSI must satisfy, expressed against a database reader.

.DESCRIPTION
    release-packaging.yml used to carry this rule as inline PowerShell, which made it unrehearsable: the
    step only runs on a `v*` tag with WiX and a real MSI present, so nobody could find out the rule was
    wrong until a release was already half-published. v1.3.0 found out the hard way -- the row count was
    written as `SELECT COUNT(*) FROM `File``, and Windows Installer's SQL dialect has no aggregate
    functions, so OpenView rejected the query before any assertion ran.

    Splitting the rule from the COM plumbing lets run-desktop-msi-contract-gate.ps1 drive it with fake
    databases on any platform, so a weakened or mis-written query fails on the pushing commit.

    Two release builds have died inside OpenView on SQL this file once used: `SELECT COUNT(*)` (no
    aggregates in the dialect) and `WHERE ... LIKE ...` (string comparison is = or <> only). Every query
    below is therefore checked against the supported-grammar list before OpenView ever sees it, and the
    selection work happens client-side: fetch the column, filter with PowerShell.

    The reader contract is one method, OpenView($query), returning an object with Execute() and Fetch();
    Fetch() returns a record exposing StringData($oneBasedColumn), or $null when the rows run out. That is
    exactly the surface WindowsInstaller.Installer's database COM object provides.
#>

Set-StrictMode -Version Latest

# The PATH-row encoding is the same contract the CLI-only MSI enforced, so it stays in one place rather
# than being restated here. Only the directory differs: this package installs the app into INSTALLFOLDER
# and the command into a `cli` subdirectory, so the row must name that subdirectory.
. (Join-Path $PSScriptRoot 'MsiPathContract.ps1')

$script:DesktopMsiCommandDirectoryToken = '[CLIFOLDER]'
$script:DesktopMsiAppExecutableName = 'SalmonEgg.exe'
$script:DesktopMsiCommandExecutableName = 'salmon-egg.exe'

# Aggregate functions, GROUP BY, JOIN, ORDER BY, DISTINCT and LIKE are all absent from Windows Installer's
# SQL dialect: it fails inside OpenView rather than returning a wrong answer. The grammar's WHERE clause
# allows only column-to-column comparison, {column} {comparator} {constant}, IS [NOT] NULL -- and for
# strings only = or <>. Rejecting the rest here turns "the release build died on a SQL error" into a
# named, testable violation.
$script:UnsupportedMsiSqlPatterns = [ordered]@{
    AggregateFunction = '(?i)\b(?:COUNT|SUM|AVG|MIN|MAX)\s*\('
    GroupBy           = '(?i)\bGROUP\s+BY\b'
    OrderBy           = '(?i)\bORDER\s+BY\b'
    Distinct          = '(?i)\bDISTINCT\b'
    Join              = '(?i)\bJOIN\b'
    Like              = '(?i)\bLIKE\b'
}

function Get-MsiQueryViolation
{
    <#
    .SYNOPSIS
        Names the unsupported construct in an MSI query, or $null when the query is expressible.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Query
    )

    foreach ($entry in $script:UnsupportedMsiSqlPatterns.GetEnumerator())
    {
        if ($Query -match $entry.Value)
        {
            return [pscustomobject]@{ Id = $entry.Key; Query = $Query }
        }
    }

    return $null
}

function Assert-MsiQuerySupported
{
    <#
    .SYNOPSIS
        Throws when a query uses SQL that Windows Installer cannot parse.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Query
    )

    $violation = Get-MsiQueryViolation -Query $Query
    if ($null -ne $violation)
    {
        throw ("Windows Installer's SQL dialect does not support $($violation.Id): '$Query'. " +
               'OpenView rejects the query itself, so the assertion behind it never runs.')
    }
}

function Get-MsiScalar
{
    <#
    .SYNOPSIS
        Returns the first column of the first row a query yields, or $null when it yields none.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        $Database,

        [Parameter(Mandatory = $true)]
        [string]$Query
    )

    Assert-MsiQuerySupported -Query $Query

    $view = $Database.OpenView($Query)
    [void]$view.Execute()
    $record = $view.Fetch()
    if ($null -eq $record)
    {
        return $null
    }

    return $record.StringData(1)
}

function Get-MsiColumn
{
    <#
    .SYNOPSIS
        Returns the first column of every row a query yields, as a string array.

    .DESCRIPTION
        Filtering by suffix is not expressible in MSI SQL -- there is no LIKE, and string comparison is
        limited to = and <>. So the rows come back whole and the caller matches them in PowerShell.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        $Database,

        [Parameter(Mandatory = $true)]
        [string]$Query
    )

    Assert-MsiQuerySupported -Query $Query

    $view = $Database.OpenView($Query)
    [void]$view.Execute()

    $values = [System.Collections.Generic.List[string]]::new()
    while ($null -ne ($record = $view.Fetch()))
    {
        $values.Add($record.StringData(1))
    }

    # The unary comma keeps the array intact on return. Without it PowerShell unrolls it, and an empty
    # result -- the EmptyPackage case this contract exists to catch -- would arrive as $null.
    return , $values.ToArray()
}

function Get-MsiRowPair
{
    <#
    .SYNOPSIS
        Returns every row of a two-column query as Name/Value pairs.

    .DESCRIPTION
        The Environment table has to be read as pairs, not as two independent columns: which value belongs
        to which variable is the whole point, and two Get-MsiColumn calls would silently pair row 1's name
        with row 1's value only for as long as the table has one row.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        $Database,

        [Parameter(Mandatory = $true)]
        [string]$Query
    )

    Assert-MsiQuerySupported -Query $Query

    $view = $Database.OpenView($Query)
    [void]$view.Execute()

    $rows = [System.Collections.Generic.List[psobject]]::new()
    while ($null -ne ($record = $view.Fetch()))
    {
        $rows.Add([pscustomobject]@{ Name = $record.StringData(1); Value = $record.StringData(2) })
    }

    # Unary comma for the same reason as Get-MsiColumn: an empty result is the violation this exists to
    # catch, and PowerShell would unroll it to $null.
    return , $rows.ToArray()
}

function Measure-MsiRows
{
    <#
    .SYNOPSIS
        Counts the rows a query yields by fetching them.

    .DESCRIPTION
        The obvious `SELECT COUNT(*)` is not available (see Assert-MsiQuerySupported), so counting means
        walking the result set.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        $Database,

        [Parameter(Mandatory = $true)]
        [string]$Query
    )

    Assert-MsiQuerySupported -Query $Query

    $view = $Database.OpenView($Query)
    [void]$view.Execute()

    $count = 0
    while ($null -ne $view.Fetch())
    {
        $count++
    }

    return $count
}

function Find-MsiFileName
{
    <#
    .SYNOPSIS
        Returns the harvested cell naming the wanted file, or $null when the package does not carry it.

    .DESCRIPTION
        The File table's FileName column holds either a long name or the pair 'SHORTNA~1.EXE|LongName.exe'.
        Both halves are checked, because which form heat emits depends on whether the name needs a 8.3
        alias -- and a rule that only understood one form would pass or fail for the wrong reason.

        Two files matter to this package: the app executable it exists to deliver, and the command it puts
        on PATH. Taking the wanted name as a parameter is what lets one rule cover both.
    #>
    [CmdletBinding()]
    param(
        # AllowEmptyString is not decoration: a real File table carries rows whose FileName is empty, and
        # PowerShell's default binding rejects a whole array that contains one. Without it the v1.3.0
        # release build died on 'Cannot bind argument ... because it is an empty string' -- the third
        # failure in this step, and one no fake ever produced because no fake had an empty cell.
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]]$FileNames,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedName
    )

    foreach ($fileName in $FileNames)
    {
        if ([string]::IsNullOrWhiteSpace($fileName)) { continue }

        foreach ($candidate in $fileName.Split('|'))
        {
            if ($candidate -eq $ExpectedName) { return $fileName }
        }
    }

    return $null
}

function Write-MsiFileTableShape
{
    <#
    .SYNOPSIS
        Reports the shape of the File table the contract is about to judge.

    .DESCRIPTION
        Three v1.3.0 release builds died in this step, each on a property of the real File table that no
        fake reproduced: no aggregate functions, no LIKE, and cells that are empty strings. Every one cost
        a full tag-and-build cycle to discover because the step asserted without ever saying what it read.
        Printing the shape first means the next surprise is diagnosable from the failed run's log.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]]$FileNames,

        [int]$SampleSize = 12
    )

    $empty = @($FileNames | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count
    $paired = @($FileNames | Where-Object { $_ -and $_.Contains('|') }).Count
    Write-Host ("[desktop-msi] File table: $($FileNames.Count) row(s), $empty empty cell(s), " +
                "$paired short|long pair(s)")

    $sample = @($FileNames | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First $SampleSize)
    if ($sample.Count -gt 0)
    {
        Write-Host "[desktop-msi] first $($sample.Count) name(s): $($sample -join ', ')"
    }
}

function Write-MsiEnvironmentTableShape
{
    <#
    .SYNOPSIS
        Reports the PATH rows the contract is about to judge.

    .DESCRIPTION
        Same reason as Write-MsiFileTableShape: the encoding of an Environment row is dense enough that
        "the contract was violated" is not actionable without seeing the row. Prefix characters and the
        null marker are invisible in a WiX source file, because WiX composes both columns itself.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [psobject[]]$Rows
    )

    Write-Host "[desktop-msi] Environment table: $($Rows.Count) row(s)"
    foreach ($row in $Rows)
    {
        Write-Host "[desktop-msi]   Name='$($row.Name)' Value='$($row.Value)'"
    }
}

function Get-DesktopMsiContractViolation
{
    <#
    .SYNOPSIS
        Returns the first way the package breaks the desktop MSI contract, or $null when it conforms.

    .DESCRIPTION
        Three failure modes a green candle/light run cannot show:
        EmptyPackage    -- the heat harvest picked up nothing, so the MSI installs no files at all.
        MissingAppExe   -- the package ships files but not the executable it exists to deliver.
        InvalidVersion  -- ProductVersion is absent or not the three-part version the release identity
                           produces, which breaks MajorUpgrade's version comparison.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        $Database
    )

    $fileNames = Get-MsiColumn -Database $Database -Query 'SELECT `FileName` FROM `File`'
    $fileCount = $fileNames.Count
    if ($fileCount -lt 1)
    {
        return [pscustomobject]@{
            Id     = 'EmptyPackage'
            Detail = "the harvest produced $fileCount file rows"
        }
    }

    $appExe = Find-MsiFileName -FileNames $fileNames -ExpectedName $script:DesktopMsiAppExecutableName
    if ([string]::IsNullOrWhiteSpace($appExe))
    {
        return [pscustomobject]@{
            Id     = 'MissingAppExe'
            Detail = "$fileCount file rows, none of them $($script:DesktopMsiAppExecutableName)"
        }
    }

    # Installing the app installs the command. A package that lost the CLI still installs a working app,
    # and the PATH entry below would then point at a directory with nothing in it.
    $commandExe = Find-MsiFileName -FileNames $fileNames -ExpectedName $script:DesktopMsiCommandExecutableName
    if ([string]::IsNullOrWhiteSpace($commandExe))
    {
        return [pscustomobject]@{
            Id     = 'MissingCommandExe'
            Detail = "$fileCount file rows, none of them $($script:DesktopMsiCommandExecutableName)"
        }
    }

    $version = Get-MsiScalar -Database $Database `
        -Query "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='ProductVersion'"
    if ($version -notmatch '^\d+\.\d+\.\d+$')
    {
        return [pscustomobject]@{ Id = 'InvalidVersion'; Detail = "ProductVersion '$version'" }
    }

    # Shipping the command without registering it leaves the user with a file they cannot invoke, which is
    # indistinguishable from a working install until they type the command.
    $environmentRows = Get-MsiRowPair -Database $Database -Query 'SELECT `Name`, `Value` FROM `Environment`'
    if ($environmentRows.Count -eq 0)
    {
        return [pscustomobject]@{
            Id     = 'NoPathRegistration'
            Detail = 'the Environment table is empty, so nothing puts the command on PATH'
        }
    }

    # More than one row means a second, unreviewed environment write reached the package -- a machine PATH
    # entry, say. Windows Installer would apply both.
    if ($environmentRows.Count -ne 1)
    {
        $described = ($environmentRows | ForEach-Object { "Name='$($_.Name)' Value='$($_.Value)'" }) -join '; '
        return [pscustomobject]@{ Id = 'MultiplePathRegistrations'; Detail = $described }
    }

    # The row's own encoding is judged by the shared rule, and its identifiers are passed through so a
    # failure names the specific defect (prepending, machine scope, permanence) rather than "bad PATH row".
    $pathViolation = Get-MsiPathContractViolation `
        -Name $environmentRows[0].Name `
        -Value $environmentRows[0].Value `
        -DirectoryToken $script:DesktopMsiCommandDirectoryToken
    if ($null -ne $pathViolation)
    {
        return [pscustomobject]@{ Id = $pathViolation.Id; Detail = $pathViolation.Message }
    }

    return $null
}

function Assert-DesktopMsiContract
{
    <#
    .SYNOPSIS
        Throws when the package breaks the contract; reports what it verified when it conforms.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        $Database
    )

    # Read and report before judging. The three release builds this step took down all failed inside the
    # reading, so a log that only ever shows the verdict leaves the next surprise undiagnosable.
    $fileNames = Get-MsiColumn -Database $Database -Query 'SELECT `FileName` FROM `File`'
    Write-MsiFileTableShape -FileNames $fileNames
    Write-MsiEnvironmentTableShape -Rows (Get-MsiRowPair -Database $Database -Query 'SELECT `Name`, `Value` FROM `Environment`')

    $violation = Get-DesktopMsiContractViolation -Database $Database
    if ($null -ne $violation)
    {
        throw "The built MSI breaks the desktop package contract ($($violation.Id)): $($violation.Detail)."
    }

    $version = Get-MsiScalar -Database $Database `
        -Query "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='ProductVersion'"
    $appExe = Find-MsiFileName -FileNames $fileNames -ExpectedName $script:DesktopMsiAppExecutableName
    $commandExe = Find-MsiFileName -FileNames $fileNames -ExpectedName $script:DesktopMsiCommandExecutableName

    Write-Host ("[desktop-msi] verified: $($fileNames.Count) file row(s), ProductVersion $version, " +
                "$appExe and $commandExe present, one conforming PATH row for " +
                "$($script:DesktopMsiCommandDirectoryToken)")
}
