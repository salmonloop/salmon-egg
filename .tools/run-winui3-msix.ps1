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
    $candidates = @()

    if (Test-Path $kitsBin) {
        $candidates += Get-ChildItem -Path $kitsBin -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
            Where-Object { Test-Path $_ }
    }

    if ($candidates.Count -gt 0) {
        return $candidates[0]
    }

    throw "signtool.exe not found. Install Windows 10/11 SDK (includes Signing Tools)."
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

function Add-CertificateToStore {
    param(
        [Parameter(Mandatory = $true)] [System.Security.Cryptography.X509Certificates.X509Certificate2] $Cert,
        [Parameter(Mandatory = $true)] [string] $StoreName,
        [Parameter(Mandatory = $true)] [System.Security.Cryptography.X509Certificates.StoreLocation] $StoreLocation
    )

    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new($StoreName, $StoreLocation)
    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    try {
        $store.Add($Cert)
    } finally {
        $store.Close()
    }
}

function Get-OrCreateDevCert {
    param([string] $Subject)

    $cert = Get-CertificateFromStore -Subject $Subject -StoreName 'My' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
    if ($cert) {
        return $cert
    }

    # Create a self-signed code signing cert and persist private key in CurrentUser\My
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
    $pfxBytes = $tmpCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, '')
    $persisted = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $pfxBytes,
        '',
        [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable -bor
        [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::PersistKeySet -bor
        [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::UserKeySet
    )

    Add-CertificateToStore -Cert $persisted -StoreName 'My' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
    return $persisted
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

$repoRoot = Split-Path -Parent $PSScriptRoot

# Ensure dotnet first-time setup and tool caches go to a writable location
# (avoids issues when HOME/DOTNET_CLI_HOME points at a restricted directory).
$dotnetCliHome = Join-Path $repoRoot '.dotnet-cli-home'
New-Item -ItemType Directory -Force -Path $dotnetCliHome | Out-Null
$env:DOTNET_CLI_HOME = $dotnetCliHome
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'

$project = Join-Path $repoRoot 'SalmonEgg\SalmonEgg\SalmonEgg.csproj'
$tfm = 'net10.0-windows10.0.26100.0'
$profile = 'Properties/PublishProfiles/win-msix-x64.pubxml'
$msixOutDir = Join-Path $repoRoot 'artifacts\msix'

Write-Host "Publishing MSIX ($Configuration, $tfm)..."
New-Item -ItemType Directory -Force -Path $msixOutDir | Out-Null

dotnet publish $project -c $Configuration -f $tfm -p:PublishProfile=$profile -v:m | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$msix = Get-ChildItem -Path $msixOutDir -Recurse -Filter *.msix -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $msix) {
    throw "MSIX not found in '$msixOutDir'. Packaging did not produce an app package."
}

$isAdmin = Test-IsAdmin
$certSubject = 'O=SalmonEgg'
$cert = Get-OrCreateDevCert -Subject $certSubject
$signTool = Get-SignToolPath

Write-Host "Signing MSIX with cert '$certSubject' ($($cert.Thumbprint))..."
& $signTool sign /fd SHA256 /sha1 $cert.Thumbprint /s My $msix.FullName | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "signtool failed with exit code $LASTEXITCODE"
}

# Read manifest info (IdentityName/AppId) from MSIX so we can uninstall/reinstall and launch reliably.
$manifestInfo = Get-MsixManifestInfo -Path $msix.FullName
$identityName = $manifestInfo.IdentityName
$appId = $manifestInfo.AppId

# Trust cert. Add-AppxPackage validates against LocalMachine trust in many configurations.
Add-CertificateToStore -Cert $cert -StoreName 'TrustedPeople' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
Add-CertificateToStore -Cert $cert -StoreName 'Root' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)

if ($isAdmin) {
    Add-CertificateToStore -Cert $cert -StoreName 'TrustedPeople' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
    Add-CertificateToStore -Cert $cert -StoreName 'Root' -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
} else {
    Write-Host "NOTE: For MSIX install to succeed you may need to run this script from an elevated PowerShell once (Run as Administrator) to trust the dev certificate for LocalMachine."
}

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

$aumid = "$($pkg.PackageFamilyName)!$appId"
Write-Host "Launching: $aumid"
# Use a single '\' after AppsFolder. If the argument is malformed, Explorer often falls back to opening Documents.
Start-Process -FilePath "explorer.exe" -ArgumentList "shell:AppsFolder\$aumid"
