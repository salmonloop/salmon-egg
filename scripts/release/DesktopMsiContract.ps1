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

    The reader contract is one method, OpenView($query), returning an object with Execute() and Fetch();
    Fetch() returns a record exposing StringData($oneBasedColumn), or $null when the rows run out. That is
    exactly the surface WindowsInstaller.Installer's database COM object provides.
#>

Set-StrictMode -Version Latest

# Aggregate functions, GROUP BY, JOIN, ORDER BY, DISTINCT and subqueries are all absent from Windows
# Installer's SQL dialect: it fails inside OpenView rather than returning a wrong answer. Rejecting them
# here turns "the release build died on a SQL error" into a named, testable violation.
$script:UnsupportedMsiSqlPatterns = [ordered]@{
    AggregateFunction = '(?i)\b(?:COUNT|SUM|AVG|MIN|MAX)\s*\('
    GroupBy           = '(?i)\bGROUP\s+BY\b'
    OrderBy           = '(?i)\bORDER\s+BY\b'
    Distinct          = '(?i)\bDISTINCT\b'
    Join              = '(?i)\bJOIN\b'
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
    $view.Execute()
    $record = $view.Fetch()
    if ($null -eq $record)
    {
        return $null
    }

    return $record.StringData(1)
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
    $view.Execute()

    $count = 0
    while ($null -ne $view.Fetch())
    {
        $count++
    }

    return $count
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

    $fileCount = Measure-MsiRows -Database $Database -Query 'SELECT `File` FROM `File`'
    if ($fileCount -lt 1)
    {
        return [pscustomobject]@{
            Id     = 'EmptyPackage'
            Detail = "the harvest produced $fileCount file rows"
        }
    }

    $appExe = Get-MsiScalar -Database $Database `
        -Query "SELECT ``FileName`` FROM ``File`` WHERE ``FileName`` LIKE '%SalmonEgg.exe'"
    if ([string]::IsNullOrWhiteSpace($appExe))
    {
        return [pscustomobject]@{ Id = 'MissingAppExe'; Detail = "$fileCount file rows, none of them SalmonEgg.exe" }
    }

    $version = Get-MsiScalar -Database $Database `
        -Query "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='ProductVersion'"
    if ($version -notmatch '^\d+\.\d+\.\d+$')
    {
        return [pscustomobject]@{ Id = 'InvalidVersion'; Detail = "ProductVersion '$version'" }
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

    $violation = Get-DesktopMsiContractViolation -Database $Database
    if ($null -ne $violation)
    {
        throw "The built MSI breaks the desktop package contract ($($violation.Id)): $($violation.Detail)."
    }

    $fileCount = Measure-MsiRows -Database $Database -Query 'SELECT `File` FROM `File`'
    $version = Get-MsiScalar -Database $Database `
        -Query "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='ProductVersion'"
    $appExe = Get-MsiScalar -Database $Database `
        -Query "SELECT ``FileName`` FROM ``File`` WHERE ``FileName`` LIKE '%SalmonEgg.exe'"

    Write-Host "[desktop-msi] verified: $fileCount file row(s), ProductVersion $version, $appExe present"
}
