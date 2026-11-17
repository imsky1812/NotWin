#Requires -Version 7
<#
.SYNOPSIS
  Publishes, signs and installs every spike that has a spike-install.txt marker into
  C:\Program Files\NothingShell.Spikes\<SpikeName>\ (uiAccess needs a signed exe under Program Files).
  Run from an elevated terminal. Spikes are throwaway and are NOT part of NotWin.sln.
  Uses the existing CN=NothingShell Dev cert (build/install.ps1 creates it) and the signtool from
  the Microsoft.Windows.SDK.BuildTools NuGet package (build/tools/tools.csproj).
.PARAMETER NoTimestamp
  Skip the RFC3161 timestamp (offline runs).
.PARAMETER Only
  Install just the named spike(s).
#>
[CmdletBinding()]
param(
    [switch]$NoTimestamp,
    [string[]]$Only
)
$ErrorActionPreference = 'Stop'

$spikes  = $PSScriptRoot
$repo    = Split-Path -Parent $spikes
$install = Join-Path $env:ProgramFiles 'NothingShell.Spikes'
$subject = 'CN=NothingShell Dev'

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]$id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'install-spikes.ps1 must be run from an elevated (Run as administrator) PowerShell.'
    exit 1
}

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
if (-not $dotnet) { $dotnet = Join-Path $env:ProgramFiles 'dotnet/dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw 'dotnet not found.' }

function Invoke-Native([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe failed (exit $LASTEXITCODE): $($arguments -join ' ')" }
}

# 1. Cert must already exist (run build/install.ps1 once first); make sure it is trusted machine-wide.
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) -and
                   ($_.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3') } |
    Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $cert) { throw "Code-signing cert '$subject' not found in Cert:\CurrentUser\My. Run build/install.ps1 first." }
$thumb = $cert.Thumbprint
foreach ($store in 'Root', 'TrustedPublisher') {
    if (-not (Test-Path "Cert:\LocalMachine\$store\$thumb")) { throw "Cert $thumb is not in LocalMachine\$store. Run build/install.ps1 first." }
}
Write-Host "Using certificate $thumb"

# 2. signtool from NuGet
Invoke-Native $dotnet @('restore', (Join-Path $repo 'build/tools/tools.csproj'), '--verbosity', 'quiet')
$pkgRoot = ((& $dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', '').Trim()
$signtool = Get-ChildItem (Join-Path $pkgRoot 'microsoft.windows.sdk.buildtools') -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Name -eq 'x64' } |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signtool) { throw 'signtool.exe (x64) not found in the NuGet cache.' }
$signArgs = @('sign', '/fd', 'SHA256', '/sha1', $thumb)
if (-not $NoTimestamp) { $signArgs += @('/tr', 'http://timestamp.digicert.com', '/td', 'SHA256') }

# 3. Every spike dir with a spike-install.txt marker
$dirs = Get-ChildItem $spikes -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'spike-install.txt') }
if ($Only) { $dirs = $dirs | Where-Object { $Only -contains $_.Name } }
if (-not $dirs) { throw 'No spikes with a spike-install.txt marker found.' }

foreach ($d in $dirs) {
    $name = $d.Name
    $csproj = Get-ChildItem $d.FullName -Filter *.csproj | Select-Object -First 1
    $out = Join-Path $repo "out/spikes/$name"
    Write-Host "== $name"
    foreach ($p in Get-Process -Name $name -ErrorAction SilentlyContinue) { $p | Stop-Process -Force }
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    # UiAccess is harmless for spikes whose csproj ignores it
    Invoke-Native $dotnet @('publish', $csproj.FullName, '-c', 'Release', '-p:UiAccess=true', '-o', $out)
    $files = Get-ChildItem $out -Recurse -Include *.exe, *.dll -File | Where-Object { $_.Name -like "$name*" }
    foreach ($f in $files) { Invoke-Native $signtool.FullName ($signArgs + $f.FullName) }
    Write-Host "Signed $($files.Count) files."
    $dest = Join-Path $install $name
    robocopy $out $dest /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed (exit $LASTEXITCODE)" }
    $global:LASTEXITCODE = 0
    Write-Host "Installed to $dest"
}
Write-Host "Done. Run e.g. '$install\KeySpike\KeySpike.exe' (launch from Explorer / a normal terminal, NOT elevated) and follow the findings doc's manual checks."
