#Requires -Version 7
<#
.SYNOPSIS
  Builds, signs and installs Nothing Shell (uiAccess requires a signed exe under Program Files).
  Run from an elevated terminal.
.PARAMETER NoTimestamp
  Skip the RFC3161 timestamp (offline runs).
.PARAMETER SkipAdminSteps
  Dev/test only: skip Root/TrustedPublisher import, process stop, the Program Files copy and the final launch.
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
        -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable `
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
# A copy started straight from the build output (for example by double-clicking it) locks $outApp: stop it first.
$fromOut = @(Get-Process -Name 'NothingShell', 'NothingShell.Watchdog' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith([IO.Path]::GetFullPath($outApp), [StringComparison]::OrdinalIgnoreCase) })
if ($fromOut.Count -gt 0) {
    Write-Host "Stopping $($fromOut.Count) process(es) running from $outApp"
    $fromOut | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}
if (Test-Path $outApp) { Remove-Item $outApp -Recurse -Force }
Invoke-Native $dotnet @('publish', (Join-Path $repo 'src/NothingShell'), '-c', 'Release', '-p:UiAccess=true', '-o', $outApp)
try {
    Invoke-Native $dotnet @('publish', (Join-Path $repo 'src/NothingShell.Watchdog'), '-c', 'Release', '-o', $outApp)
} catch {
    Write-Warning "Native AOT publish of the watchdog failed ($($_.Exception.Message)). Falling back to a non-AOT publish; install the 'Desktop development with C++' workload for AOT."
    Invoke-Native $dotnet @('publish', (Join-Path $repo 'src/NothingShell.Watchdog'), '-c', 'Release', '-p:PublishAot=false', '-o', $outApp)
}

# 4. Sign every exe/dll (signtool from the Microsoft.Windows.SDK.BuildTools NuGet package)
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

# 5. Stop the running copy so files can be replaced. Done only now, after everything that can throw (publish, signing),
#    so a failed run never leaves the user's taskbar hidden with the shell and its watchdog gone.
if ($SkipAdminSteps) {
    Write-Host "SkipAdminSteps: not copying to $install. Output is in $outApp"
    return
}
$watchdogWasRunning = [bool](Get-Process -Name 'NothingShell.Watchdog' -ErrorAction SilentlyContinue)
# Only the shell is killed: the watchdog sees it die and restores the taskbar by itself.
Get-Process -Name 'NothingShell' -ErrorAction SilentlyContinue | Stop-Process -Force
$deadline = (Get-Date).AddSeconds(10)
while ((Get-Process -Name 'NothingShell.Watchdog' -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 250
}
# Watchdog still running (stuck) or never ran: restore explicitly with the installed copy before replacing files.
$installedWatchdog = Join-Path $install 'NothingShell.Watchdog.exe'
if ((-not $watchdogWasRunning) -or (Get-Process -Name 'NothingShell.Watchdog' -ErrorAction SilentlyContinue)) {
    if (Test-Path $installedWatchdog) {
        # WinExe: '&' would not wait, so use Start-Process -Wait to get a real exit code.
        $p = Start-Process -FilePath $installedWatchdog -ArgumentList '--restore-now', '--state', ('"{0}"' -f (Join-Path $env:APPDATA 'NothingShell')) -Wait -PassThru
        if ($p.ExitCode -ne 0) {
            throw "Watchdog --restore-now failed (exit $($p.ExitCode)). Aborting before replacing files in $install so the installed watchdog stays available to retry the taskbar restore."
        }
    }
    Get-Process -Name 'NothingShell.Watchdog' -ErrorAction SilentlyContinue | Stop-Process -Force
}
Start-Sleep -Milliseconds 500

# 6. Mirror to Program Files
# The four Nothing wallpapers (spec §5) and their previews, for the Settings wallpaper picker (Plan 5).
$wallOut = Join-Path $outApp 'Wallpapers'
New-Item -ItemType Directory -Force -Path $wallOut | Out-Null
Copy-Item (Join-Path $repo 'wallpapers/*.png'), (Join-Path $repo 'wallpapers/*_preview.jpg') $wallOut -Force
robocopy $outApp $install /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed (exit $LASTEXITCODE)" }
$global:LASTEXITCODE = 0
Write-Host "Installed to $install"

# 6b. Sparse package identity (notifications, radios). Registered for the installing user with the Program Files
#     folder as external location. Optional: on failure the shell still runs, just without notification access.
try {
    $makeappx = Join-Path $signtool.Directory 'makeappx.exe'
    $stage = Join-Path $repo 'out/package'
    $msix = Join-Path $repo 'out/NothingShell.msix'
    Remove-Item $stage, $msix -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory (Join-Path $stage 'Assets') -Force | Out-Null
    Copy-Item (Join-Path $repo 'src/NothingShell/Package/AppxManifest.xml') $stage
    Copy-Item (Join-Path $repo 'src/NothingShell/Package/Assets/logo.png') (Join-Path $stage 'Assets')
    Invoke-Native $makeappx @('pack', '/nv', '/o', '/d', $stage, '/p', $msix)
    Invoke-Native $signtool.FullName @('sign', '/fd', 'SHA256', '/sha1', $thumb, $msix)
    Get-AppxPackage -Name 'NothingShell' -ErrorAction SilentlyContinue | Remove-AppxPackage
    Add-AppxPackage -Path $msix -ExternalLocation $install
    $pkg = Get-AppxPackage -Name 'NothingShell'
    Write-Host "Package registered: $($pkg.PackageFullName) ($($pkg.Status))"
} catch {
    Write-Warning "Package identity registration failed: $($_.Exception.Message). Nothing Shell will run without notification access."
}

# 7. Launch once WITHOUT elevation (explorer.exe de-elevates) so the user process writes the HKCU Run value.
Start-Process explorer.exe -ArgumentList "`"$install\NothingShell.exe`""
Write-Host "Launched NothingShell unelevated. Check %AppData%\NothingShell\log.txt for uiAccess=1."
