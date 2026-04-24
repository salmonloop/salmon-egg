param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [switch] $SkipInstall
)

$ErrorActionPreference = 'Stop'

# Improve UTF-8 output when invoked from Windows PowerShell / cmd.
try {
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [Console]::InputEncoding = $utf8NoBom
    [Console]::OutputEncoding = $utf8NoBom
    $OutputEncoding = $utf8NoBom
} catch {
    # Ignore if host does not allow changing encodings.
}

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-SignToolPath {
    $kitsBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (-not (Test-Path $kitsBin)) {
        throw "Windows SDK bin directory not found at '$kitsBin'. Install the Windows 10/11 SDK."
    }

    $versionDirs = Get-ChildItem -Path $kitsBin -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' }

    $preferred = @($versionDirs | Where-Object { $_.Name -eq '10.0.22621.0' } |
        ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
        Where-Object { Test-Path $_ })
    if ($preferred.Count -gt 0) {
        return ($preferred | Select-Object -First 1)
    }

    $latestInstalled = @($versionDirs |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
        Where-Object { Test-Path $_ })
    if ($latestInstalled.Count -gt 0) {
        return ($latestInstalled | Select-Object -First 1)
    }

    $sdkRootTool = Join-Path $kitsBin 'x64\signtool.exe'
    if (Test-Path $sdkRootTool) {
        return $sdkRootTool
    }

    throw "signtool.exe not found under Windows SDK bin directory '$kitsBin'. Install the Windows 10/11 SDK."
}

function Get-MSBuildPath {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) {
        throw "vswhere.exe not found. Install Visual Studio 2022 or Build Tools 2022 with MSBuild."
    }

    $vsInstall = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    if (-not $vsInstall) {
        throw "MSBuild not found. Install Visual Studio 2022 or Build Tools 2022 with MSBuild."
    }

    $msbuild = Join-Path $vsInstall 'MSBuild\Current\Bin\MSBuild.exe'
    if (-not (Test-Path $msbuild)) {
        throw "MSBuild.exe not found at '$msbuild'."
    }

    return $msbuild
}

function Get-CertificateFromStore {
    param(
        [Parameter(Mandatory = $true)] [string] $Subject,
        [Parameter(Mandatory = $true)] [string] $StoreName,
        [Parameter(Mandatory = $true)] [System.Security.Cryptography.X509Certificates.StoreLocation] $StoreLocation
    )

    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new($StoreName, $StoreLocation)
    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
    try {
        return $store.Certificates |
            Where-Object { $_.Subject -eq $Subject } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1
    } finally {
        $store.Close()
    }
}

function Get-CertificatesFromStore {
    param(
        [Parameter(Mandatory = $true)] [string] $Subject,
        [Parameter(Mandatory = $true)] [string] $StoreName,
        [Parameter(Mandatory = $true)] [System.Security.Cryptography.X509Certificates.StoreLocation] $StoreLocation
    )

    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new($StoreName, $StoreLocation)
    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
    try {
        return $store.Certificates |
            Where-Object { $_.Subject -eq $Subject } |
            Sort-Object NotAfter -Descending
    } finally {
        $store.Close()
    }
}

function Add-CertificateToStore {
    param(
        [Parameter(Mandatory = $true)] [System.Security.Cryptography.X509Certificates.X509Certificate2] $Cert,
        [Parameter(Mandatory = $true)] [string] $StoreName,
        [Parameter(Mandatory = $true)] [System.Security.Cryptography.X509Certificates.StoreLocation] $StoreLocation
    )

    $certToStore = $Cert
    if ($StoreName -ne 'My') {
        $certBytes = $Cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
        $certToStore = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($certBytes)
    }

    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new($StoreName, $StoreLocation)
    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    try {
        $exists = $store.Certificates | Where-Object { $_.Thumbprint -eq $certToStore.Thumbprint } | Select-Object -First 1
        if (-not $exists) {
            $store.Add($certToStore)
        }
    } finally {
        $store.Close()
        if (($StoreName -ne 'My') -and ($null -ne $certToStore)) {
            $certToStore.Dispose()
        }
    }
}

function Remove-CertificateFromStore {
    param(
        [Parameter(Mandatory = $true)] [System.Security.Cryptography.X509Certificates.X509Certificate2] $Cert,
        [Parameter(Mandatory = $true)] [string] $StoreName,
        [Parameter(Mandatory = $true)] [System.Security.Cryptography.X509Certificates.StoreLocation] $StoreLocation
    )

    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new($StoreName, $StoreLocation)
    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    try {
        $store.Remove($Cert)
    } finally {
        $store.Close()
    }
}

function Get-ValidCodeSigningCert {
    param(
        [Parameter(Mandatory = $true)] [System.Security.Cryptography.X509Certificates.X509Certificate2] $Cert
    )

    if (-not $Cert.HasPrivateKey) {
        return $false
    }

    try {
        $rsaKey = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($Cert)
    } catch {
        return $false
    }

    return ($null -ne $rsaKey)
}

function Read-ProtectedSecret {
    param([Parameter(Mandatory = $true)] [string] $SecretCachePath)

    $protectedSecret = Get-Content -Path $SecretCachePath -ErrorAction Stop | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($protectedSecret)) {
        throw "Protected secret cache '$SecretCachePath' is empty."
    }

    $secureSecret = ConvertTo-SecureString -String $protectedSecret -ErrorAction Stop
    return [System.Net.NetworkCredential]::new('', $secureSecret).Password
}

function Write-ProtectedSecret {
    param(
        [Parameter(Mandatory = $true)] [string] $SecretCachePath,
        [Parameter(Mandatory = $true)] [Security.SecureString] $Secret
    )

    $protectedSecret = ConvertFrom-SecureString -SecureString $Secret
    Set-Content -Path $SecretCachePath -Value $protectedSecret -Encoding ASCII
}

function Get-OrCreateDevCert {
    param(
        [Parameter(Mandatory = $true)] [string] $Subject,
        [Parameter(Mandatory = $true)] [string] $PfxPath,
        [Parameter(Mandatory = $true)] [string] $SecretCachePath
    )

    $candidates = Get-CertificatesFromStore -Subject $Subject -StoreName 'My' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
    $cert = $candidates | Where-Object { Get-ValidCodeSigningCert -Cert $_ } | Select-Object -First 1
    if ($cert) {
        return $cert
    }

    if ((Test-Path $PfxPath) -and (Test-Path $SecretCachePath)) {
        try {
            $pfxPassword = Read-ProtectedSecret -SecretCachePath $SecretCachePath
            $imported = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
                $PfxPath,
                $pfxPassword,
                [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable -bor
                [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::PersistKeySet -bor
                [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::UserKeySet
            )
            Add-CertificateToStore -Cert $imported -StoreName 'My' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)

            $candidates = Get-CertificatesFromStore -Subject $Subject -StoreName 'My' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
            $cert = $candidates | Where-Object { Get-ValidCodeSigningCert -Cert $_ } | Select-Object -First 1
            if ($cert) {
                return $cert
            }
        } catch {
            Write-Host "WARN: Cached signing certificate could not be imported; recreating cache. $($_.Exception.Message)"
            Remove-Item -Force -Path $PfxPath -ErrorAction SilentlyContinue
            Remove-Item -Force -Path $SecretCachePath -ErrorAction SilentlyContinue
        }
    }

    if ($candidates) {
        Write-Host "Existing dev certs are missing an RSA private key; recreating."
        foreach ($old in $candidates) {
            Remove-CertificateFromStore -Cert $old -StoreName 'My' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
        }
    }

    # Prefer New-SelfSignedCertificate to create a standard code-signing cert compatible with signtool.
    try {
        $created = New-SelfSignedCertificate `
            -Subject $Subject `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -KeySpec Signature `
            -Provider 'Microsoft Enhanced RSA and AES Cryptographic Provider' `
            -KeyExportPolicy Exportable `
            -KeyUsage DigitalSignature `
            -Type CodeSigningCert `
            -NotAfter (Get-Date).AddYears(2)
        if ($created) {
            $password = [Guid]::NewGuid().ToString("N")
            $secure = ConvertTo-SecureString -String $password -AsPlainText -Force
            Export-PfxCertificate -Cert $created -FilePath $PfxPath -Password $secure | Out-Null
            Write-ProtectedSecret -SecretCachePath $SecretCachePath -Secret $secure
            return $created
        }
    } catch {
        Write-Host "New-SelfSignedCertificate failed; falling back to manual certificate creation."
    }

    # Fallback: create a self-signed code signing cert and persist private key in CurrentUser\My
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    $hash = [System.Security.Cryptography.HashAlgorithmName]::SHA256
    $padding = [System.Security.Cryptography.RSASignaturePadding]::Pkcs1
    $req = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new($Subject, $rsa, $hash, $padding)

    $oids = [System.Security.Cryptography.OidCollection]::new()
    [void]$oids.Add([System.Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3')) # Code Signing EKU
    $req.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($oids, $false))
    $req.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature, $true))

    $notBefore = [DateTimeOffset]::UtcNow.AddDays(-1)
    $notAfter = [DateTimeOffset]::UtcNow.AddYears(2)
    $tmpCert = $req.CreateSelfSigned($notBefore, $notAfter)

    # Re-import as PFX to persist the private key in the user key store
    $password = [Guid]::NewGuid().ToString("N")
    $pfxBytes = $tmpCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $password)
    $persisted = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $pfxBytes,
        $password,
        [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable -bor
        [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::PersistKeySet -bor
        [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::UserKeySet
    )

    Add-CertificateToStore -Cert $persisted -StoreName 'My' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
    Export-PfxCertificate -Cert $persisted -FilePath $PfxPath -Password (ConvertTo-SecureString -String $password -AsPlainText -Force) | Out-Null
    Write-ProtectedSecret -SecretCachePath $SecretCachePath -Secret (ConvertTo-SecureString -String $password -AsPlainText -Force)
    return $persisted
}

function Sync-TrustedCertificateStores {
    param(
        [Parameter(Mandatory = $true)] [System.Security.Cryptography.X509Certificates.X509Certificate2] $Cert,
        [Parameter(Mandatory = $true)] [string] $Subject,
        [Parameter(Mandatory = $true)] [bool] $IsAdmin
    )

    Add-CertificateToStore -Cert $Cert -StoreName 'TrustedPeople' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
    Add-CertificateToStore -Cert $Cert -StoreName 'Root' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
    Remove-ExtraCertificates -Subject $Subject -ThumbprintToKeep $Cert.Thumbprint -StoreName 'TrustedPeople' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
    Remove-ExtraCertificates -Subject $Subject -ThumbprintToKeep $Cert.Thumbprint -StoreName 'Root' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
    Remove-ExtraCertificates -Subject $Subject -ThumbprintToKeep $Cert.Thumbprint -StoreName 'My' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)

    if ($IsAdmin) {
        Add-CertificateToStore -Cert $Cert -StoreName 'TrustedPeople' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
        Add-CertificateToStore -Cert $Cert -StoreName 'Root' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
        Remove-ExtraCertificates -Subject $Subject -ThumbprintToKeep $Cert.Thumbprint -StoreName 'TrustedPeople' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
        Remove-ExtraCertificates -Subject $Subject -ThumbprintToKeep $Cert.Thumbprint -StoreName 'Root' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
    } else {
        Write-Host "NOTE: For MSIX install to succeed you may need to run this script from an elevated PowerShell once (Run as Administrator) to trust the dev certificate for LocalMachine."
    }
}

function Test-CertificateThumbprintPresent {
    param(
        [Parameter(Mandatory = $true)] [string] $Thumbprint,
        [Parameter(Mandatory = $true)] [string] $StoreName,
        [Parameter(Mandatory = $true)] [System.Security.Cryptography.X509Certificates.StoreLocation] $StoreLocation
    )

    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new($StoreName, $StoreLocation)
    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
    try {
        return ($null -ne ($store.Certificates | Where-Object { $_.Thumbprint -eq $Thumbprint } | Select-Object -First 1))
    } finally {
        $store.Close()
    }
}

function Assert-TrustedCertificateStores {
    param(
        [Parameter(Mandatory = $true)] [System.Security.Cryptography.X509Certificates.X509Certificate2] $Cert,
        [Parameter(Mandatory = $true)] [bool] $IsAdmin
    )

    if (-not (Test-CertificateThumbprintPresent -Thumbprint $Cert.Thumbprint -StoreName 'TrustedPeople' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser))) {
        throw "CurrentUser\\TrustedPeople does not contain signing certificate $($Cert.Thumbprint)."
    }

    if ($IsAdmin) {
        if (-not (Test-CertificateThumbprintPresent -Thumbprint $Cert.Thumbprint -StoreName 'TrustedPeople' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine))) {
            throw "LocalMachine\\TrustedPeople does not contain signing certificate $($Cert.Thumbprint)."
        }

        if (-not (Test-CertificateThumbprintPresent -Thumbprint $Cert.Thumbprint -StoreName 'Root' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine))) {
            throw "LocalMachine\\Root does not contain signing certificate $($Cert.Thumbprint)."
        }
    }
}

function Remove-ExtraCertificates {
    param(
        [Parameter(Mandatory = $true)] [string] $Subject,
        [Parameter(Mandatory = $true)] [string] $ThumbprintToKeep,
        [Parameter(Mandatory = $true)] [string] $StoreName,
        [Parameter(Mandatory = $true)] [System.Security.Cryptography.X509Certificates.StoreLocation] $StoreLocation
    )

    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new($StoreName, $StoreLocation)
    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    try {
        foreach ($cert in $store.Certificates | Where-Object { $_.Subject -eq $Subject -and $_.Thumbprint -ne $ThumbprintToKeep }) {
            try {
                $store.Remove($cert)
            } catch {
                Write-Host "WARN: Failed to remove duplicate cert from ${StoreLocation}\\${StoreName}: $($_.Exception.Message)"
            }
        }
    } finally {
        $store.Close()
    }
}

function Get-MsixManifestInfo {
    param([Parameter(Mandatory = $true)] [string] $Path)

    $tmp = Join-Path $env:TEMP ("salmonegg-msix-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null

    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($Path, $tmp)

        $manifestPath = Join-Path $tmp 'AppxManifest.xml'
        if (-not (Test-Path $manifestPath)) {
            throw "AppxManifest.xml not found inside MSIX."
        }

        [xml]$manifest = Get-Content $manifestPath
        $identityName = $manifest.Package.Identity.Name
        $appId = $manifest.Package.Applications.Application.Id
        if (-not $identityName -or -not $appId) {
            throw "Unable to determine app identity from manifest (Identity.Name='$identityName', Application.Id='$appId')."
        }

        return [pscustomobject]@{
            IdentityName = $identityName
            AppId        = $appId
        }
    } finally {
        Remove-Item -Recurse -Force -Path $tmp -ErrorAction SilentlyContinue
    }
}

function Get-FileSha256 {
    param([Parameter(Mandatory = $true)] [string] $Path)

    if (-not (Test-Path $Path)) {
        throw "File '$Path' was not found for SHA256 calculation."
    }

    $hash = Get-FileHash -Path $Path -Algorithm SHA256 -ErrorAction Stop
    return $hash.Hash.ToUpperInvariant()
}

function Write-CurrentInstallMarker {
    param(
        [Parameter(Mandatory = $true)] [string] $RepoRoot,
        [Parameter(Mandatory = $true)] [string] $MarkerPath,
        [Parameter(Mandatory = $true)] [string] $Configuration,
        [Parameter(Mandatory = $true)] [string] $PackageIdentity,
        [Parameter(Mandatory = $true)] [string] $InstalledExecutablePath,
        [Parameter(Mandatory = $true)] [string] $MsixPath
    )

    $markerDirectory = Split-Path -Parent $MarkerPath
    if ($markerDirectory) {
        New-Item -ItemType Directory -Force -Path $markerDirectory | Out-Null
    }

    $payload = [ordered]@{
        repoRoot                = $RepoRoot
        configuration           = $Configuration
        packageIdentity         = $PackageIdentity
        installedExecutablePath = $InstalledExecutablePath
        installedExecutableSha256 = (Get-FileSha256 -Path $InstalledExecutablePath)
        writtenAtUtc            = [DateTime]::UtcNow.ToString('o')
        msixPath                = $MsixPath
        msixSha256              = (Get-FileSha256 -Path $MsixPath)
    }

    $json = $payload | ConvertTo-Json -Depth 4
    [System.IO.File]::WriteAllText($MarkerPath, $json, [System.Text.UTF8Encoding]::new($false))
}

function ConvertTo-ProcessArgumentString {
    param([Parameter(Mandatory = $true)] [string[]] $Arguments)

    return ($Arguments | ForEach-Object {
        if ($_ -match '[\s"]') {
            '"' + ($_ -replace '"', '\"') + '"'
        } else {
            $_
        }
    }) -join ' '
}

function Show-LogTail {
    param(
        [Parameter(Mandatory = $true)] [string] $Path,
        [int] $LineCount = 40
    )

    if (-not (Test-Path $Path)) {
        return
    }

    Write-Host "---- Last $LineCount lines from $Path ----"
    Get-Content -Path $Path -Tail $LineCount | ForEach-Object { Write-Host $_ }
    Write-Host "---- End log tail ----"
}

function Invoke-LoggedProcess {
    param(
        [Parameter(Mandatory = $true)] [string] $FilePath,
        [Parameter(Mandatory = $true)] [string[]] $Arguments,
        [Parameter(Mandatory = $true)] [string] $LogPath,
        [Parameter(Mandatory = $true)] [string] $StepName,
        [string] $DisplayCommand
    )

    $logDir = Split-Path -Parent $LogPath
    if ($logDir) {
        New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    }

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $FilePath
    $psi.Arguments = ConvertTo-ProcessArgumentString -Arguments $Arguments
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $commandLabel = if ($DisplayCommand) { $DisplayCommand } else { "$FilePath $($psi.Arguments)" }
    $header = @(
        "# $StepName"
        "# Started: $(Get-Date -Format o)"
        "# Command: $commandLabel"
        ""
    )
    Set-Content -Path $LogPath -Value $header -Encoding UTF8

    Write-Host "$StepName..."
    Write-Host "  log: $LogPath"

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $psi

    try {
        [void]$process.Start()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()

        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()

        if (-not [string]::IsNullOrEmpty($stdout)) {
            Add-Content -Path $LogPath -Value "## STDOUT`r`n$stdout" -Encoding UTF8
        }

        if (-not [string]::IsNullOrEmpty($stderr)) {
            Add-Content -Path $LogPath -Value "## STDERR`r`n$stderr" -Encoding UTF8
        }

        if ($process.ExitCode -ne 0) {
            Show-LogTail -Path $LogPath
            throw "$StepName failed with exit code $($process.ExitCode). See log: $LogPath"
        }
    } finally {
        $process.Dispose()
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$certCacheDir = Join-Path $repoRoot '.tools\certs'
$certPfxPath = Join-Path $certCacheDir 'SalmonEgg-dev.pfx'
$certSecretCachePath = Join-Path $certCacheDir 'SalmonEgg-dev.pfx.secret'
New-Item -ItemType Directory -Force -Path $certCacheDir | Out-Null

# Ensure dotnet first-time setup and tool caches go to a writable location
# (avoids issues when HOME/DOTNET_CLI_HOME points at a restricted directory).
$dotnetCliHome = Join-Path $repoRoot '.dotnet-cli-home'
New-Item -ItemType Directory -Force -Path $dotnetCliHome | Out-Null
$env:DOTNET_CLI_HOME = $dotnetCliHome
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'

$repoNuGetPackages = Join-Path $repoRoot '.dotnet-cli\.nuget\packages'
$userNuGetPackages = Join-Path $HOME '.nuget\packages'
if (-not $env:NUGET_PACKAGES) {
    if (Test-Path $userNuGetPackages) {
        $env:NUGET_PACKAGES = $userNuGetPackages
    } else {
        New-Item -ItemType Directory -Force -Path $repoNuGetPackages | Out-Null
        $env:NUGET_PACKAGES = $repoNuGetPackages
    }
}

function Remove-RestoreArtifacts {
    param([Parameter(Mandatory = $true)] [string] $ProjectPath)

    $projectDirectory = Split-Path -Parent $ProjectPath
    $projectFileName = Split-Path -Leaf $ProjectPath
    $objDirectory = Join-Path $projectDirectory 'obj'
    if (-not (Test-Path $objDirectory)) {
        return
    }

    $pathsToRemove = @(
        (Join-Path $objDirectory 'project.assets.json'),
        (Join-Path $objDirectory 'project.nuget.cache'),
        (Join-Path $objDirectory "$projectFileName.nuget.dgspec.json"),
        (Join-Path $objDirectory "$projectFileName.nuget.g.props"),
        (Join-Path $objDirectory "$projectFileName.nuget.g.targets")
    )

    foreach ($path in $pathsToRemove) {
        if (Test-Path $path) {
            Remove-Item -Force -Path $path -ErrorAction SilentlyContinue
        }
    }
}

$project = Join-Path $repoRoot 'SalmonEgg\SalmonEgg\SalmonEgg.csproj'
$referenceProjects = @(
    (Join-Path $repoRoot 'src\SalmonEgg.Domain\SalmonEgg.Domain.csproj'),
    (Join-Path $repoRoot 'src\SalmonEgg.Application\SalmonEgg.Application.csproj'),
    (Join-Path $repoRoot 'src\SalmonEgg.Infrastructure\SalmonEgg.Infrastructure.csproj'),
    (Join-Path $repoRoot 'src\SalmonEgg.Presentation.Core\SalmonEgg.Presentation.Core.csproj')
)
$tfm = 'net10.0-windows10.0.26100.0'
$publishProfile = 'Properties/PublishProfiles/win-msix-x64.pubxml'
$msixOutDir = Join-Path $repoRoot 'artifacts\msix'
$msixLogDir = Join-Path $repoRoot 'artifacts\logs\msix'
$logStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$restoreLogPath = Join-Path $msixLogDir "$logStamp-restore.log"
$publishLogPath = Join-Path $msixLogDir "$logStamp-publish.log"
$signLogPath = Join-Path $msixLogDir "$logStamp-sign.log"
$restoreBinLogPath = Join-Path $msixLogDir "$logStamp-restore.binlog"
$publishBinLogPath = Join-Path $msixLogDir "$logStamp-publish.binlog"
$currentInstallMarkerPath = Join-Path $msixOutDir 'current-install.json'

Write-Host "Publishing MSIX ($Configuration, $tfm)..."
New-Item -ItemType Directory -Force -Path $msixOutDir | Out-Null
New-Item -ItemType Directory -Force -Path $msixLogDir | Out-Null

$msbuild = Get-MSBuildPath
$isolatedAppObjDir = Join-Path $repoRoot 'SalmonEgg\SalmonEgg\obj\msix-app'
if (Test-Path $isolatedAppObjDir) {
    Remove-Item -Recurse -Force $isolatedAppObjDir -ErrorAction SilentlyContinue
}

Invoke-LoggedProcess `
    -FilePath $msbuild `
    -Arguments @(
        $project,
        '/t:Restore',
        "/p:Configuration=$Configuration",
        "/p:TargetFramework=$tfm",
        '/p:EnableWinUIBuild=true',
        '/p:IsolatedMsixBuild=true',
        '/p:BuildProjectReferences=false',
        "/p:RestorePackagesPath=$($env:NUGET_PACKAGES)",
        '/p:RestoreIgnoreFailedSources=true',
        '/p:NuGetAudit=false',
        "/bl:$restoreBinLogPath",
        '/v:minimal'
    ) `
    -LogPath $restoreLogPath `
    -StepName 'Restoring WinUI app with isolated intermediates' `
    -DisplayCommand "MSBuild Restore (binlog: $restoreBinLogPath)"

foreach ($referenceProject in $referenceProjects) {
    Remove-RestoreArtifacts -ProjectPath $referenceProject
}

foreach ($referenceProject in $referenceProjects) {
    $referenceProjectName = [System.IO.Path]::GetFileNameWithoutExtension($referenceProject)
    $referenceRestoreLogPath = Join-Path $msixLogDir "$logStamp-reference-restore-$referenceProjectName.log"
    $referenceRestoreBinLogPath = Join-Path $msixLogDir "$logStamp-reference-restore-$referenceProjectName.binlog"

    Invoke-LoggedProcess `
        -FilePath $msbuild `
        -Arguments @(
            $referenceProject,
            '/t:Restore',
            "/p:Configuration=$Configuration",
            '/p:TargetFramework=net10.0',
            "/p:RestorePackagesPath=$($env:NUGET_PACKAGES)",
            '/p:RestoreIgnoreFailedSources=true',
            '/p:NuGetAudit=false',
            "-bl:$referenceRestoreBinLogPath",
            '/v:minimal'
        ) `
        -LogPath $referenceRestoreLogPath `
        -StepName "Refreshing net10.0 assets for $referenceProjectName" `
        -DisplayCommand "MSBuild Restore ($referenceProjectName, binlog: $referenceRestoreBinLogPath)"
}

Invoke-LoggedProcess `
    -FilePath $msbuild `
    -Arguments @(
        $project,
        '/t:Publish',
        "/p:Configuration=$Configuration",
        "/p:TargetFramework=$tfm",
        "/p:PublishProfile=$publishProfile",
        '/p:EnableWinUIBuild=true',
        '/p:IsolatedMsixBuild=true',
        '/p:BuildProjectReferences=true',
        '/p:Restore=false',
        "/p:RestorePackagesPath=$($env:NUGET_PACKAGES)",
        '/p:RestoreIgnoreFailedSources=true',
        '/p:NuGetAudit=false',
        "/bl:$publishBinLogPath",
        '/v:minimal'
    ) `
    -LogPath $publishLogPath `
    -StepName 'Publishing MSIX with MSBuild' `
    -DisplayCommand "MSBuild Publish (binlog: $publishBinLogPath)"

$msix = Get-ChildItem -Path $msixOutDir -Recurse -Filter *.msix -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $msix) {
    throw "MSIX not found in '$msixOutDir'. Packaging did not produce an app package."
}

$isAdmin = Test-IsAdmin
$certSubject = 'CN=SalmonEgg'
$cert = Get-OrCreateDevCert -Subject $certSubject -PfxPath $certPfxPath -SecretCachePath $certSecretCachePath
$signTool = Get-SignToolPath

Write-Host "Signing MSIX with cert '$certSubject' ($($cert.Thumbprint))..."
$pfxPath = $null
$pfxPassword = $null
try {
    $pfxPath = Join-Path $env:TEMP ("salmonegg-sign-" + [Guid]::NewGuid().ToString("N") + ".pfx")
    $pfxPassword = [Guid]::NewGuid().ToString("N")
    $securePassword = ConvertTo-SecureString -String $pfxPassword -AsPlainText -Force
    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null

    Invoke-LoggedProcess `
        -FilePath $signTool `
        -Arguments @(
            'sign',
            '/fd', 'SHA256',
            '/f', $pfxPath,
            '/p', $pfxPassword,
            $msix.FullName
        ) `
        -LogPath $signLogPath `
        -StepName 'Signing MSIX package' `
        -DisplayCommand "signtool sign $($msix.Name)"
} finally {
    if ($pfxPath -and (Test-Path $pfxPath)) {
        Remove-Item -Force -Path $pfxPath -ErrorAction SilentlyContinue
    }
}

# Read manifest info (IdentityName/AppId) from MSIX so we can uninstall/reinstall and launch reliably.
$manifestInfo = Get-MsixManifestInfo -Path $msix.FullName
$identityName = $manifestInfo.IdentityName
$appId = $manifestInfo.AppId

# Trust cert. Add-AppxPackage validates against LocalMachine trust in many configurations.
Sync-TrustedCertificateStores -Cert $cert -Subject $certSubject -IsAdmin $isAdmin
Assert-TrustedCertificateStores -Cert $cert -IsAdmin $isAdmin

if ($SkipInstall) {
    Write-Host "SkipInstall set; skipping uninstall/install/launch. MSIX output is under '$msixOutDir'."
    return
}

Write-Host "Checking existing install: $identityName"
$existing = Get-AppxPackage -Name $identityName -ErrorAction SilentlyContinue
if ($existing) {
    foreach ($pkg in $existing) {
        Write-Host "Removing existing package: $($pkg.PackageFullName)"
        try {
            Remove-AppxPackage -Package $pkg.PackageFullName -ErrorAction Stop
        } catch {
            Write-Host "WARN: Failed to remove existing package (will attempt install anyway): $($_.Exception.Message)"
        }
    }
}

if ($isAdmin) {
    $removeCmd = Get-Command Remove-AppxPackage -ErrorAction SilentlyContinue
    if ($removeCmd -and $removeCmd.Parameters.ContainsKey('AllUsers')) {
        $existingAll = Get-AppxPackage -AllUsers -Name $identityName -ErrorAction SilentlyContinue
        if ($existingAll) {
            foreach ($pkg in $existingAll) {
                Write-Host "Removing existing package (AllUsers): $($pkg.PackageFullName)"
                try {
                    Remove-AppxPackage -AllUsers -Package $pkg.PackageFullName -ErrorAction Stop
                } catch {
                    Write-Host "WARN: Failed to remove AllUsers package: $($_.Exception.Message)"
                }
            }
        }
    }
}

Write-Host "Installing: $($msix.FullName)"
try {
    Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown
} catch {
    $msg = $_.Exception.Message
    if ($msg -match '0x800B0109|0x800B010A|0x80073CF0') {
        throw "MSIX install failed due to certificate trust. Re-run in an elevated PowerShell once to install the cert into LocalMachine stores, then re-run. Original error: $msg"
    }
    if ($msg -match '0x80073CFB') {
        throw "MSIX install failed because an older version is still installed (0x80073CFB). Try re-running from an elevated PowerShell so the script can remove the package for all users, or bump the MSIX version. Original error: $msg"
    }
    throw
}

$pkg = Get-AppxPackage -Name $identityName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $pkg) {
    throw "Package '$identityName' not found after install; cannot determine PackageFamilyName for launch."
}

$installedExecutablePath = Join-Path $pkg.InstallLocation 'SalmonEgg.exe'
if (-not (Test-Path $installedExecutablePath)) {
    throw "Installed SalmonEgg executable '$installedExecutablePath' was not found after install."
}

Write-CurrentInstallMarker `
    -RepoRoot $repoRoot `
    -MarkerPath $currentInstallMarkerPath `
    -Configuration $Configuration `
    -PackageIdentity $identityName `
    -InstalledExecutablePath $installedExecutablePath `
    -MsixPath $msix.FullName

Write-Host "Wrote install provenance marker: $currentInstallMarkerPath"

$aumid = "$($pkg.PackageFamilyName)!$appId"
Write-Host "Launching: $aumid"
# Use a single '\' after AppsFolder. If the argument is malformed, Explorer often falls back to opening Documents.
Start-Process -FilePath "explorer.exe" -ArgumentList "shell:AppsFolder\$aumid"
