#Requires -Version 7
<#
.SYNOPSIS  Packs + signs the sparse package (AppxManifest.xml) and registers it with an external location.
.PARAMETER ExternalLocation  Folder containing NotifySpike.exe (e.g. out\spikes\NotifySpike, or C:\Program Files\NothingShell.Spikes\NotifySpike).
.PARAMETER Remove  Unregister instead.
Non-admin is enough (Add-AppxPackage with a trusted cert). Output .msix is under out\ (git-ignored).
#>
param([string]$ExternalLocation, [switch]$Remove)
$ErrorActionPreference = 'Stop'
if ($Remove) { Get-AppxPackage NothingShell.NotifySpike | Remove-AppxPackage; return }
$here = $PSScriptRoot; $repo = Split-Path -Parent (Split-Path -Parent $here)
$dotnet = (Get-Command dotnet).Source
& $dotnet restore (Join-Path $repo 'build/tools/tools.csproj') --verbosity quiet
$pkgRoot = ((& $dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', '').Trim()
$tools = Get-ChildItem (Join-Path $pkgRoot 'microsoft.windows.sdk.buildtools') -Recurse -Filter signtool.exe | Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
$makeappx = Join-Path $tools.Directory 'makeappx.exe'
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq 'CN=NothingShell Dev' -and $_.HasPrivateKey } | Sort-Object NotAfter -Descending | Select-Object -First 1
$stage = Join-Path $repo 'out/spikes/NotifySpike-msix'; $msix = Join-Path $repo 'out/spikes/NotifySpike.msix'
Remove-Item $stage, $msix -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $stage, (Join-Path $stage 'Assets') | Out-Null
Copy-Item (Join-Path $here 'AppxManifest.xml') $stage
Copy-Item (Join-Path $here 'Assets/logo.png') (Join-Path $stage 'Assets')
& $makeappx pack /nv /o /d $stage /p $msix; if ($LASTEXITCODE) { throw 'makeappx failed' }
& $tools.FullName sign /fd SHA256 /sha1 $cert.Thumbprint $msix; if ($LASTEXITCODE) { throw 'signtool failed' }
Get-AppxPackage NothingShell.NotifySpike | Remove-AppxPackage
Add-AppxPackage -Path $msix -ExternalLocation $ExternalLocation
Get-AppxPackage NothingShell.NotifySpike | Format-List Name, PackageFullName, InstallLocation, SignatureKind, Status
