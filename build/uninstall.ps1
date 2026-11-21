#Requires -Version 7
<# Removes the installed NotWin. Leaves the dev cert and %AppData%\NotWin in place. Run elevated. #>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]$id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'uninstall.ps1 must be run from an elevated (Run as administrator) PowerShell.'
    exit 1
}

$install = Join-Path $env:ProgramFiles 'NotWin'

# 1. Stop shell and watchdog
foreach ($name in 'NotWin', 'NotWin.Watchdog') {
    $procs = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
    if ($procs.Count -gt 0) {
        $procs | Stop-Process -Force
        $procs | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
    }
}

# 2. Restore the taskbar/shell state
$watchdog = Join-Path $install 'NotWin.Watchdog.exe'
if (Test-Path $watchdog) {
    # WinExe: '&' would not wait, so use Start-Process -Wait to get a real exit code.
    $p = Start-Process -FilePath $watchdog -ArgumentList '--restore-now', '--state', ('"{0}"' -f (Join-Path $env:APPDATA 'NotWin')) -Wait -PassThru
    if ($p.ExitCode -ne 0) {
        throw "Watchdog --restore-now failed (exit $($p.ExitCode)). Aborting before deleting $install so the watchdog stays available to retry the taskbar/shell restore."
    }
} else {
    Write-Host 'Watchdog not found; skipping restore.'
}

# 3. Autostart entry
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'NotWin' -ErrorAction SilentlyContinue

# 3b. Sparse package identity (registered by install.ps1)
Get-AppxPackage -Name 'NotWin' -ErrorAction SilentlyContinue | Remove-AppxPackage

# 4. Remove install dir
if (Test-Path $install) { Remove-Item $install -Recurse -Force }

# 5. Cert and data folder intentionally left in place.
Write-Host 'Uninstalled. The NotWin Dev cert and %AppData%\NotWin were left in place.'
