#Requires -Version 7
<#
.SYNOPSIS
  Builds, signs and packages NotWin into out/ WITHOUT administrator rights. Run this first, from a normal
  (not elevated) terminal; then run build\install.ps1 elevated.
.DESCRIPTION
  Security review 2026-11-21 (#1): the compiler, NuGet analyzers / source generators, signtool and makeappx run here
  as the normal user, never as admin. install.ps1 (elevated) only trusts the certificate, copies out/ into an
  admin-only staging folder, checks every signature there and installs.
.PARAMETER NoTimestamp
  Skip the RFC3161 timestamp (offline runs).
#>
[CmdletBinding()]
param([switch]$NoTimestamp)
$ErrorActionPreference = 'Stop'

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
if (([Security.Principal.WindowsPrincipal]$id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'build.ps1 must run WITHOUT administrator rights (a normal terminal). The elevated part is install.ps1.'
    exit 1
}

$repo    = Split-Path -Parent $PSScriptRoot
$outApp  = Join-Path $repo 'out/app'
$msix    = Join-Path $repo 'out/NotWin.msix'
$subject = 'CN=NotWin Dev'

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
if (-not $dotnet) { $dotnet = Join-Path $env:ProgramFiles 'dotnet/dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw 'dotnet not found.' }

function Invoke-Native([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe failed (exit $LASTEXITCODE): $($arguments -join ' ')" }
}

# 1. Code-signing cert in CurrentUser\My (reused if present)
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

# 2. Publish
# A copy started straight from the build output locks $outApp: stop it first (our own user's processes only).
$fromOut = @(Get-Process -Name 'NotWin', 'NotWin.Watchdog' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith([IO.Path]::GetFullPath($outApp), [StringComparison]::OrdinalIgnoreCase) })
if ($fromOut.Count -gt 0) {
    Write-Host "Stopping $($fromOut.Count) process(es) running from $outApp"
    $fromOut | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}
if (Test-Path $outApp) { Remove-Item $outApp -Recurse -Force }
# The shelf's drag helper runs at normal level (no uiAccess). Published first: if the two share a file, the shell's
# own publish below is the copy that stays.
Invoke-Native $dotnet @('publish', (Join-Path $repo 'src/NotWin.DragHelper'), '-c', 'Release', '-o', $outApp)
# A plain Release build leaves obj/bin output with the normal manifest that publish would reuse (2026-11-20).
foreach ($stale in 'src/NotWin/obj/Release', 'src/NotWin/bin/Release') {
    $path = Join-Path $repo $stale
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}
Invoke-Native $dotnet @('publish', (Join-Path $repo 'src/NotWin'), '-c', 'Release', '-p:UiAccess=true', '-o', $outApp)
$builtExe = Join-Path $outApp 'NotWin.exe'
if (-not ([IO.File]::ReadAllText($builtExe, [Text.Encoding]::UTF8) -match 'uiAccess="true"')) {
    throw "NotWin.exe was built without uiAccess=""true"" in its manifest."
}
try {
    Invoke-Native $dotnet @('publish', (Join-Path $repo 'src/NotWin.Watchdog'), '-c', 'Release', '-o', $outApp)
} catch {
    Write-Warning "Native AOT publish of the watchdog failed ($($_.Exception.Message)). Falling back to a non-AOT publish."
    Invoke-Native $dotnet @('publish', (Join-Path $repo 'src/NotWin.Watchdog'), '-c', 'Release', '-p:PublishAot=false', '-o', $outApp)
}
# The Lucent wallpapers (light / dark, Plan 13) used by the Dark mode tile, Settings › Theme and the photo tint.
$wallOut = Join-Path $outApp 'Wallpapers'
New-Item -ItemType Directory -Force -Path $wallOut | Out-Null
Copy-Item (Join-Path $repo 'wallpapers/*.png'), (Join-Path $repo 'wallpapers/*_preview.jpg') $wallOut -Force

# 3. Sign every exe/dll (signtool from the Microsoft.Windows.SDK.BuildTools NuGet package; runs as the normal user)
Invoke-Native $dotnet @('restore', (Join-Path $repo 'build/tools/tools.csproj'), '--verbosity', 'quiet')
$pkgRoot = ((& $dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', '').Trim()
$signtool = Get-ChildItem (Join-Path $pkgRoot 'microsoft.windows.sdk.buildtools') -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Name -eq 'x64' } |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signtool) { throw 'signtool.exe (x64) not found in the NuGet cache after restoring build/tools/tools.csproj.' }
$signArgs = @('sign', '/fd', 'SHA256', '/sha1', $thumb)
if (-not $NoTimestamp) { $signArgs += @('/tr', 'http://timestamp.digicert.com', '/td', 'SHA256') }
$files = Get-ChildItem $outApp -Recurse -Include *.exe, *.dll -File
foreach ($f in $files) { Invoke-Native $signtool.FullName ($signArgs + $f.FullName) }
Write-Host "Signed $($files.Count) files."

# 4. Sparse package (notifications, radios): packed and signed here, registered by install.ps1.
$makeappx = Join-Path $signtool.Directory 'makeappx.exe'
$stage = Join-Path $repo 'out/package'
Remove-Item $stage, $msix -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory (Join-Path $stage 'Assets') -Force | Out-Null
Copy-Item (Join-Path $repo 'src/NotWin/Package/AppxManifest.xml') $stage
Copy-Item (Join-Path $repo 'src/NotWin/Package/Assets/logo.png') (Join-Path $stage 'Assets')
Invoke-Native $makeappx @('pack', '/nv', '/o', '/d', $stage, '/p', $msix)
Invoke-Native $signtool.FullName @('sign', '/fd', 'SHA256', '/sha1', $thumb, $msix)

Write-Host "Built and signed into $outApp. Next: run build\install.ps1 from an elevated PowerShell."
