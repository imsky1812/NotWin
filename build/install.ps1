#Requires -Version 7
<#
.SYNOPSIS
  Installs NotWin from out/ (built and signed by build\build.ps1, run WITHOUT admin rights first). Run this one
  from an elevated PowerShell. uiAccess requires a signed exe under Program Files.
.DESCRIPTION
  Security review 2026-11-21 (#1, #7): nothing is compiled or signed here. The elevated part only
    - trusts the signing certificate machine-wide, straight from the certificate store (no temporary .cer file),
    - copies out/ into an admin-only staging folder under Program Files,
    - checks that every exe and dll there carries a valid signature by that certificate and that NotWin.exe
      asks for uiAccess, and refuses to install otherwise,
    - stops the running copy, mirrors the staging folder into Program Files and registers the package.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

$repo    = Split-Path -Parent $PSScriptRoot
$outApp  = Join-Path $repo 'out/app'
$outMsix = Join-Path $repo 'out/NotWin.msix'
$install = Join-Path $env:ProgramFiles 'NotWin'
$stage   = Join-Path $env:ProgramFiles 'NotWin.staging'
$subject = 'CN=NotWin Dev'

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]$id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'install.ps1 must be run from an elevated (Run as administrator) PowerShell. Build first with build\build.ps1 (not elevated).'
    exit 1
}
if (-not (Test-Path (Join-Path $outApp 'NotWin.exe')) -or -not (Test-Path $outMsix)) {
    Write-Error 'Nothing to install: run build\build.ps1 from a normal (not elevated) terminal first.'
    exit 1
}

# 1. The signing certificate (made by build.ps1 in the user's store) and machine-wide trust, without a temp file.
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date) -and
                   ($_.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3') } |
    Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $cert) { throw "Signing certificate $subject not found: run build\build.ps1 first." }
$thumb = $cert.Thumbprint
foreach ($storeName in 'Root', 'TrustedPublisher') {
    $store = [Security.Cryptography.X509Certificates.X509Store]::new($storeName, 'LocalMachine')
    $store.Open('ReadWrite')
    try {
        if (-not ($store.Certificates | Where-Object Thumbprint -eq $thumb)) {
            # Only the public part goes in (the private key never leaves the user's store).
            $public = [Security.Cryptography.X509Certificates.X509Certificate2]::new($cert.RawData)
            $store.Add($public)
            Write-Host "Trusted in LocalMachine\$storeName"
        }
    } finally { $store.Close() }
}

# 2. Stage into an admin-only folder, then check every signature there (what is checked is what gets installed).
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
robocopy $outApp $stage /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy into staging failed (exit $LASTEXITCODE)" }
$global:LASTEXITCODE = 0
$stageMsix = Join-Path $stage 'NotWin.msix'
Copy-Item $outMsix $stageMsix -Force

$bad = @()
foreach ($f in @(Get-ChildItem $stage -Recurse -Include *.exe, *.dll -File)) { # the msix signature is checked by Add-AppxPackage
    $sig = Get-AuthenticodeSignature -FilePath $f.FullName
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Thumbprint -ne $thumb) { $bad += $f.Name }
}
if (-not ([IO.File]::ReadAllText((Join-Path $stage 'NotWin.exe'), [Text.Encoding]::UTF8) -match 'uiAccess="true"')) {
    $bad += 'NotWin.exe (no uiAccess in its manifest)'
}
if ($bad.Count -gt 0) {
    Remove-Item $stage -Recurse -Force
    throw "Not installing: $($bad.Count) file(s) are not signed by $subject ($thumb): $($bad -join ', '). Rebuild with build\build.ps1."
}
Write-Host "Checked $(@(Get-ChildItem $stage -Recurse -Include *.exe, *.dll -File).Count) signed files in staging."

# 2b. Plan 12: the app used to be called Nothing Shell. Remove that install so the two never run side by side.
# The taskbar is given back with the NEW, staged watchdog (its delete never follows links, security review #2), never
# with the old exe. Each step reports one line; none of them stops the install. %AppData%\NothingShell is kept: NotWin
# copies it over on first start and it stays as a backup.
function Remove-LegacyNothingShell([string]$stagedWatchdog) {
    $oldInstall = Join-Path $env:ProgramFiles 'NothingShell'
    $runKey     = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $hasRun     = $null -ne (Get-ItemProperty -Path $runKey -Name 'NothingShell' -ErrorAction SilentlyContinue)
    $hasPkg     = $null -ne (Get-AppxPackage -Name 'NothingShell' -ErrorAction SilentlyContinue)
    if (-not ((Test-Path $oldInstall) -or $hasRun -or $hasPkg)) { return }
    Write-Host 'Old Nothing Shell install found: removing it.'

    try {
        foreach ($name in 'NothingShell.Watchdog', 'NothingShell') {
            $procs = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
            $procs | Stop-Process -Force -ErrorAction SilentlyContinue
            $procs | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
        }
        Write-Host '  stopped the old shell and watchdog'
    } catch { Write-Warning "  could not stop the old shell: $($_.Exception.Message)" }

    $restored = $false
    try {
        $p = Start-Process -FilePath $stagedWatchdog -ArgumentList '--restore-now', '--state', ('"{0}"' -f (Join-Path $env:APPDATA 'NothingShell')) -Wait -PassThru
        $restored = $p.ExitCode -eq 0
        Write-Host "  taskbar given back for the old install (exit $($p.ExitCode))"
    } catch { Write-Warning "  taskbar restore for the old install failed: $($_.Exception.Message)" }
    if (-not $restored) {
        # Keep the old install in place so this step runs again next time and the saved taskbar setting is not lost.
        Write-Warning '  the old install is left in place until its taskbar setting is given back; run install.ps1 again.'
        return
    }

    try {
        Remove-ItemProperty -Path $runKey -Name 'NothingShell' -ErrorAction SilentlyContinue
        Write-Host '  removed the old start-with-Windows entry'
    } catch { Write-Warning "  could not remove the old start-with-Windows entry: $($_.Exception.Message)" }

    try {
        Get-AppxPackage -Name 'NothingShell' -ErrorAction SilentlyContinue | Remove-AppxPackage
        Write-Host '  removed the old package identity'
    } catch { Write-Warning "  could not remove the old package: $($_.Exception.Message)" }

    try {
        if (Test-Path $oldInstall) { Remove-Item $oldInstall -Recurse -Force }
        Write-Host "  deleted $oldInstall"
    } catch { Write-Warning "  could not delete ${oldInstall}: $($_.Exception.Message)" }
}
Remove-LegacyNothingShell (Join-Path $stage 'NotWin.Watchdog.exe')

# 3. Stop the running copy so files can be replaced (only now, after everything that can throw above).
$watchdogWasRunning = [bool](Get-Process -Name 'NotWin.Watchdog' -ErrorAction SilentlyContinue)
# Only the shell is killed: the watchdog sees it die and restores the taskbar by itself.
Get-Process -Name 'NotWin' -ErrorAction SilentlyContinue | Stop-Process -Force
$deadline = (Get-Date).AddSeconds(10)
while ((Get-Process -Name 'NotWin.Watchdog' -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 250
}
# Watchdog stuck or never ran: restore explicitly. The staged (checked) watchdog is used; it deletes its state file
# without following links (security review #2).
$stagedWatchdog = Join-Path $stage 'NotWin.Watchdog.exe'
if ((-not $watchdogWasRunning) -or (Get-Process -Name 'NotWin.Watchdog' -ErrorAction SilentlyContinue)) {
    $p = Start-Process -FilePath $stagedWatchdog -ArgumentList '--restore-now', '--state', ('"{0}"' -f (Join-Path $env:APPDATA 'NotWin')) -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Watchdog --restore-now failed (exit $($p.ExitCode)). Nothing was replaced in $install." }
    Get-Process -Name 'NotWin.Watchdog' -ErrorAction SilentlyContinue | Stop-Process -Force
}
Start-Sleep -Milliseconds 500

# 4. Mirror the checked staging folder into Program Files.
robocopy $stage $install /MIR /XF NotWin.msix /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed (exit $LASTEXITCODE)" }
$global:LASTEXITCODE = 0
Write-Host "Installed to $install"

# 5. Sparse package identity (notifications, radios). Optional: on failure the shell runs without notification access.
try {
    Get-AppxPackage -Name 'NotWin' -ErrorAction SilentlyContinue | Remove-AppxPackage
    Add-AppxPackage -Path $stageMsix -ExternalLocation $install
    $pkg = Get-AppxPackage -Name 'NotWin'
    Write-Host "Package registered: $($pkg.PackageFullName) ($($pkg.Status))"
} catch {
    Write-Warning "Package identity registration failed: $($_.Exception.Message). NotWin will run without notification access."
}
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue

# 6. Launch once WITHOUT elevation (explorer.exe de-elevates) so the user process writes the HKCU Run value.
Start-Process explorer.exe -ArgumentList "`"$install\NotWin.exe`""
Write-Host "Launched NotWin unelevated. Check %AppData%\NotWin\log.txt for uiAccess=1."
