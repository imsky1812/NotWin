#Requires -Version 7
<#
.SYNOPSIS
  Builds out\NotWin-Setup.exe: one file that installs NotWin on any Windows 11 PC with a double-click.
  Run from a normal (NOT elevated) PowerShell 7. Needs the .NET 10 SDK.
.DESCRIPTION
  1. Publishes NotWin, its drag helper and its watchdog self-contained for win-x64 (no .NET needed on the PC).
  2. Packs the sparse package (notifications) UNSIGNED.
  3. Zips both and appends the zip to NotWin.Setup.exe (one trimmed single file), see src\NotWin.Setup\Payload.cs.
  Nothing is signed here: setup signs NotWin on each PC with a certificate made for that PC (PerPcSigner.cs).
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
if (([Security.Principal.WindowsPrincipal]$id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'make-setup.ps1 must run WITHOUT administrator rights (a normal terminal).'
    exit 1
}

$repo    = Split-Path -Parent $PSScriptRoot
$work    = Join-Path $repo 'out/setup'
$payload = Join-Path $work 'payload'
$app     = Join-Path $payload 'app'
$zip     = Join-Path $work 'payload.zip'
$output  = Join-Path $repo 'out/NotWin-Setup.exe'

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
if (-not $dotnet) { $dotnet = Join-Path $env:ProgramFiles 'dotnet/dotnet.exe' }
function Invoke-Native([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe failed (exit $LASTEXITCODE): $($arguments -join ' ')" }
}

if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force $app | Out-Null

# 1. Self-contained app. The drag helper goes first so the shell's own publish wins any shared file.
$rid = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $app)
Invoke-Native $dotnet (@('publish', (Join-Path $repo 'src/NotWin.DragHelper')) + $rid)
foreach ($stale in 'src/NotWin/obj/Release', 'src/NotWin/bin/Release') {
    $path = Join-Path $repo $stale
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}
Invoke-Native $dotnet (@('publish', (Join-Path $repo 'src/NotWin'), '-p:UiAccess=true') + $rid)
if (-not ([IO.File]::ReadAllText((Join-Path $app 'NotWin.exe'), [Text.Encoding]::UTF8) -match 'uiAccess="true"')) {
    throw 'NotWin.exe was built without uiAccess="true" in its manifest.'
}
try {
    Invoke-Native $dotnet (@('publish', (Join-Path $repo 'src/NotWin.Watchdog')) + $rid)
} catch {
    Write-Warning "Native AOT publish of the watchdog failed (no C++ build tools?). Publishing it self-contained instead."
    Invoke-Native $dotnet (@('publish', (Join-Path $repo 'src/NotWin.Watchdog'), '-p:PublishAot=false') + $rid)
}
$wallOut = Join-Path $app 'Wallpapers'
New-Item -ItemType Directory -Force $wallOut | Out-Null
Copy-Item (Join-Path $repo 'wallpapers/*.png'), (Join-Path $repo 'wallpapers/*_preview.jpg') $wallOut -Force
Copy-Item (Join-Path $repo 'src/NotWin/Theme/Fonts/OFL-*.txt') $app -Force
Get-ChildItem $app -Recurse -Filter *.pdb | Remove-Item -Force

# 2. Unsigned sparse package (makeappx from the Microsoft.Windows.SDK.BuildTools NuGet package).
Invoke-Native $dotnet @('restore', (Join-Path $repo 'build/tools/tools.csproj'), '--verbosity', 'quiet')
$pkgRoot = ((& $dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', '').Trim()
$makeappx = Get-ChildItem (Join-Path $pkgRoot 'microsoft.windows.sdk.buildtools') -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeappx) { throw 'makeappx.exe (x64) not found in the NuGet cache.' }
$pkg = Join-Path $work 'package'
New-Item -ItemType Directory (Join-Path $pkg 'Assets') -Force | Out-Null
Copy-Item (Join-Path $repo 'src/NotWin/Package/AppxManifest.xml') $pkg
Copy-Item (Join-Path $repo 'src/NotWin/Package/Assets/logo.png') (Join-Path $pkg 'Assets')
Invoke-Native $makeappx.FullName @('pack', '/nv', '/o', '/d', $pkg, '/p', (Join-Path $payload 'NotWin.msix'))

# 3. Zip and append to the setup exe.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
$setupBin = Join-Path $work 'bin'
# One trimmed, compressed single file: runs on a PC without .NET. Its bundle is found from the exe's start, so the zip
# appended after it does not disturb it.
Invoke-Native $dotnet @('publish', (Join-Path $repo 'src/NotWin.Setup'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-p:PublishSingleFile=true', '-p:PublishTrimmed=true', '-p:EnableCompressionInSingleFile=true', '-o', $setupBin)
$stub = Join-Path $setupBin 'NotWin.Setup.exe'

$out = [IO.File]::Create($output)
try {
    foreach ($part in $stub, $zip) { $in = [IO.File]::OpenRead($part); try { $in.CopyTo($out) } finally { $in.Dispose() } }
    $footer = [byte[]]::new(16)
    [BitConverter]::GetBytes([int64](Get-Item $zip).Length).CopyTo($footer, 0)   # little-endian on x64
    [Text.Encoding]::ASCII.GetBytes('NWSETUP1').CopyTo($footer, 8)
    $out.Write($footer, 0, 16)
} finally { $out.Dispose() }

$mb = [math]::Round((Get-Item $output).Length / 1MB, 1)
Write-Host "Built $output ($mb MB). Double-click it on any Windows 11 PC to install NotWin."
