#Requires -Version 7
<#
.SYNOPSIS
  Builds, signs and installs Nothing Shell (uiAccess requires a signed exe under Program Files).
  Run from an elevated terminal.
.PARAMETER NoTimestamp
  Skip the RFC3161 timestamp (offline runs).
.PARAMETER SkipAdminSteps
  Dev/test only: skip Root/TrustedPublisher import, process stop and the Program Files copy.
  Everything else (cert in CurrentUser\My, publish, signing into out/app) still runs without elevation.
#>
[CmdletBinding()]
param(
    [switch]$NoTimestamp,
    [switch]$SkipAdminSteps
)
$ErrorActionPreference = 'Stop'

$repo    = Split-Path -Parent $PSScriptRoot
$outApp  = Join-Path $repo 'out/app'
$install = Join-Path $env:ProgramFiles 'NothingShell'
$subject = 'CN=NothingShell Dev'

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    ([Security.Principal.WindowsPrincipal]$id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not $SkipAdminSteps -and -not (Test-Admin)) {
    Write-Error 'install.ps1 must be run from an elevated (Run as administrator) PowerShell. Re-run elevated, or pass -SkipAdminSteps for a non-admin dry run.'
    exit 1
}

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
if (-not $dotnet) { $dotnet = Join-Path $env:ProgramFiles 'dotnet/dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw 'dotnet not found.' }

function Invoke-Native([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe failed (exit $LASTEXITCODE): $($arguments -join ' ')" }
}

# 1. Code-signing cert (reuse if present)
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) -and
                   ($_.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3') } |
    Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $cert) {
    Write-Host "Creating code-signing certificate $subject"
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
        -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
        -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(5)
}
$thumb = $cert.Thumbprint
Write-Host "Using certificate $thumb"

# 2. Trust it machine-wide (uiAccess requires a chain to a trusted root)
if (-not $SkipAdminSteps) {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) "nothingshell-$thumb.cer"
    Export-Certificate -Cert $cert -FilePath $tmp | Out-Null
    foreach ($store in 'Root', 'TrustedPublisher') {
        if (-not (Test-Path "Cert:\LocalMachine\$store\$thumb")) {
            Import-Certificate -FilePath $tmp -CertStoreLocation "Cert:\LocalMachine\$store" | Out-Null
            Write-Host "Imported into LocalMachine\$store"
        }
    }
    Remove-Item $tmp -Force
}

# 3. Publish
if (Test-Path $outApp) { Remove-Item $outApp -Recurse -Force }
Invoke-Native $dotnet @('publish', (Join-Path $repo 'src/NothingShell'), '-c', 'Release', '-p:UiAccess=true', '-o', $outApp)
try {
    Invoke-Native $dotnet @('publish', (Join-Path $repo 'src/NothingShell.Watchdog'), '-c', 'Release', '-o', $outApp)
} catch {
    Write-Warning "Native AOT publish of the watchdog failed ($($_.Exception.Message)). Falling back to a non-AOT publish; install the 'Desktop development with C++' workload for AOT."
    Invoke-Native $dotnet @('publish', (Join-Path $repo 'src/NothingShell.Watchdog'), '-c', 'Release', '-p:PublishAot=false', '-o', $outApp)
}

# 4. Stop running instances so files can be replaced
if (-not $SkipAdminSteps) {
    foreach ($name in 'NothingShell', 'NothingShell.Watchdog') {
        Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force
    }
    Start-Sleep -Milliseconds 500
}

# 5. Sign every exe/dll (signtool from the Microsoft.Windows.SDK.BuildTools NuGet package)
Invoke-Native $dotnet @('restore', (Join-Path $repo 'build/tools/tools.csproj'), '--verbosity', 'quiet')
$pkgRoot = ((& $dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', '').Trim()
$signtool = Get-ChildItem (Join-Path $pkgRoot 'microsoft.windows.sdk.buildtools') -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Name -eq 'x64' } |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signtool) { throw 'signtool.exe (x64) not found in the NuGet cache after restoring build/tools/tools.csproj.' }

$signArgs = @('sign', '/fd', 'SHA256', '/sha1', $thumb)
if (-not $NoTimestamp) { $signArgs += @('/tr', 'http://timestamp.digicert.com', '/td', 'SHA256') }
$files = Get-ChildItem $outApp -Recurse -Include *.exe, *.dll -File
foreach ($f in $files) {
    Invoke-Native $signtool.FullName ($signArgs + $f.FullName)
}
Write-Host "Signed $($files.Count) files."

# 6. Mirror to Program Files
if ($SkipAdminSteps) {
    Write-Host "SkipAdminSteps: not copying to $install. Output is in $outApp"
    return
}
robocopy $outApp $install /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed (exit $LASTEXITCODE)" }
$global:LASTEXITCODE = 0
Write-Host "Installed to $install"
Write-Host "Start $install\NothingShell.exe and check %AppData%\NothingShell\log.txt for uiAccess=1."
