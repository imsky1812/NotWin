#Requires -Version 7
<#
.SYNOPSIS
  Interactive hands-on verification for Nothing Shell plan 1 (taskbar control + spike manual checks).
  Run ONCE from an ELEVATED PowerShell 7 window. Follow the on-screen prompts.
  Always puts the Windows taskbar back when it ends (also on errors and Ctrl+C).
.NOTES
  Log:     D:\NothingLauncher\.superpowers\sdd\2026-11-17-plan1-foundation-and-dock\handson-<stamp>.log
  Results: D:\NothingLauncher\.superpowers\sdd\2026-11-17-plan1-foundation-and-dock\handson-results-<stamp>.json
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

# =====================================================================================
# HELPERS (everything above the "END HELPERS" marker is safe to dot-source for testing)
# =====================================================================================
$Repo        = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$SddDir      = 'D:\NothingLauncher\.superpowers\sdd\2026-11-17-plan1-foundation-and-dock'
$AppDir      = Join-Path $env:ProgramFiles 'NothingShell'
$AppExe      = Join-Path $AppDir 'NothingShell.exe'
$Watchdog    = Join-Path $AppDir 'NothingShell.Watchdog.exe'
$StateDir    = Join-Path $env:APPDATA 'NothingShell'
$StateFile   = Join-Path $StateDir 'taskbar-restore.json'
$AppLog      = Join-Path $StateDir 'log.txt'
$SpikeRoot   = Join-Path $env:ProgramFiles 'NothingShell.Spikes'
$SpikeNames  = 'KeySpike', 'OverflowSpike', 'DesktopLayerSpike', 'NotifySpike'
$Stamp       = Get-Date -Format 'yyyyMMdd-HHmmss'

$script:Results        = [System.Collections.Generic.List[object]]::new()
$script:ResultsPath    = Join-Path $SddDir "handson-results-$Stamp.json"
$script:OrigAutoHide   = $null     # auto-hide recorded at the start (Fenn closed)
$script:OrigTray       = $null
$script:StartedProcs   = [System.Collections.Generic.List[object]]::new()   # Process objects we started
$script:PreexistingIds = @{}       # name -> pids that existed before we started anything
$script:CleanedUp      = $false
$script:BannersOff     = $false
$script:LastSpikeMode  = ''
$script:npFound        = $null
$script:TempFiles      = [System.Collections.Generic.List[string]]::new()

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class NsWin
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct APPBARDATA { public uint cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags; }

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowExW(IntPtr p, IntPtr a, string c, string t);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
    [DllImport("user32.dll")] static extern bool GetMonitorInfoW(IntPtr m, ref MONITORINFO mi);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int sz);
    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint m, ref APPBARDATA d);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint a, bool i, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageNameW(IntPtr h, uint f, StringBuilder sb, ref uint n);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    public static void Init() { try { SetProcessDPIAware(); } catch { } }

    static bool IsExplorerOwned(IntPtr h)
    {
        uint pid; GetWindowThreadProcessId(h, out pid);
        if (pid == 0) return false;
        IntPtr p = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
        if (p == IntPtr.Zero) return false;
        try
        {
            var sb = new StringBuilder(1024); uint n = 1024;
            if (!QueryFullProcessImageNameW(p, 0, sb, ref n)) return false;
            return sb.ToString().EndsWith("\\explorer.exe", StringComparison.OrdinalIgnoreCase);
        }
        finally { CloseHandle(p); }
    }

    static List<IntPtr> ExplorerWindows(string cls)
    {
        var res = new List<IntPtr>(); IntPtr prev = IntPtr.Zero;
        while (true)
        {
            prev = FindWindowExW(IntPtr.Zero, prev, cls, null);
            if (prev == IntPtr.Zero) break;
            if (IsExplorerOwned(prev)) res.Add(prev);
        }
        return res;
    }

    static IntPtr Primary() { foreach (var h in ExplorerWindows("Shell_TrayWnd")) return h; return IntPtr.Zero; }

    // -1 = no explorer-owned tray, 0 = hidden, 1 = visible
    public static int TrayState() { var h = Primary(); if (h == IntPtr.Zero) return -1; return IsWindowVisible(h) ? 1 : 0; }
    public static bool HasTray() { return Primary() != IntPtr.Zero; }

    public static bool GetAutoHide()
    {
        var d = new APPBARDATA { cbSize = (uint)Marshal.SizeOf(typeof(APPBARDATA)), hWnd = Primary() };
        uint s = (uint)(long)SHAppBarMessage(4, ref d);
        return (s & 1) != 0;
    }

    public static void SetAutoHide(bool on)
    {
        var d = new APPBARDATA { cbSize = (uint)Marshal.SizeOf(typeof(APPBARDATA)), hWnd = Primary() };
        uint s = (uint)(long)SHAppBarMessage(4, ref d);
        d.lParam = (IntPtr)(on ? (s | 1u) : (s & ~1u));
        SHAppBarMessage(10, ref d);
    }

    public static bool ShowTrays()
    {
        var p = ExplorerWindows("Shell_TrayWnd");
        foreach (var h in p) ShowWindow(h, 5);
        foreach (var h in ExplorerWindows("Shell_SecondaryTrayWnd")) ShowWindow(h, 5);
        return p.Count > 0;
    }

    public static bool WindowExists(IntPtr h) { return IsWindow(h); }

    public static IntPtr[] TopLevelOfProcess(uint pid)
    {
        var res = new List<IntPtr>();
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) res.Add(h); return true; }, IntPtr.Zero);
        return res.ToArray();
    }

    public static int CloseProcessWindows(uint pid)
    {
        var w = TopLevelOfProcess(pid);
        foreach (var h in w) PostMessageW(h, 0x10, IntPtr.Zero, IntPtr.Zero);   // WM_CLOSE
        return w.Length;
    }

    public static void CloseWindow(IntPtr h) { PostMessageW(h, 0x10, IntPtr.Zero, IntPtr.Zero); }
    public static void Maximize(IntPtr h) { ShowWindow(h, 3); }

    public static IntPtr[] VisibleByClass(string cls)
    {
        var res = new List<IntPtr>();
        EnumWindows((h, l) =>
        {
            var sb = new StringBuilder(256); GetClassNameW(h, sb, 256);
            if (sb.ToString() == cls && IsWindowVisible(h)) res.Add(h);
            return true;
        }, IntPtr.Zero);
        return res.ToArray();
    }

    // visible frame (without the invisible resize border): left, top, right, bottom
    public static int[] FrameBounds(IntPtr h)
    {
        RECT r; if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))) != 0) return null;
        return new[] { r.L, r.T, r.R, r.B };
    }

    // primary monitor: full rect (4 ints) + work area (4 ints)
    public static int[] PrimaryMonitor()
    {
        IntPtr m = MonitorFromWindow(IntPtr.Zero, 1);   // MONITOR_DEFAULTTOPRIMARY
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
        if (!GetMonitorInfoW(m, ref mi)) return null;
        return new[] { mi.rcMonitor.L, mi.rcMonitor.T, mi.rcMonitor.R, mi.rcMonitor.B, mi.rcWork.L, mi.rcWork.T, mi.rcWork.R, mi.rcWork.B };
    }
}
'@
[NsWin]::Init()

# ---------- console output ----------
function Write-Step([string]$Title) {
    Write-Host ''
    Write-Host ('=' * 70) -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host ('=' * 70) -ForegroundColor Cyan
}
function Write-Info([string]$Text)  { Write-Host "  $Text" }
function Write-Do([string]$Text)    { Write-Host "  >> $Text" -ForegroundColor Yellow }
function Write-Good([string]$Text)  { Write-Host "  $Text" -ForegroundColor Green }
function Write-Bad([string]$Text)   { Write-Host "  $Text" -ForegroundColor Red }

function Ask-YN([string]$Question) {
    while ($true) {
        $a = (Read-Host "  ? $Question  [y = yes, n = no, s = skip]").Trim().ToLowerInvariant()
        switch -Regex ($a) {
            '^(y|yes)$'  { return 'y' }
            '^(n|no)$'   { return 'n' }
            '^(s|skip)$' { return 's' }
        }
        Write-Host '    Please type y, n or s and press Enter.' -ForegroundColor DarkYellow
    }
}
function Wait-Enter([string]$Text = 'Press Enter when ready') { [void](Read-Host "  ... $Text") }

# ---------- results ----------
function Save-Results {
    try {
        $dir = Split-Path $script:ResultsPath
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
        ($script:Results | ConvertTo-Json -Depth 8) | Set-Content -Path $script:ResultsPath -Encoding UTF8
    } catch { Write-Host "  (could not write results file: $($_.Exception.Message))" -ForegroundColor DarkYellow }
}

# Decide a verdict and record it.
#  MeasuredPass: $true/$false/$null (null = nothing measured)
#  Expect: the answer ('y'/'n') that means PASS, or $null when no question was asked
function Complete-Check {
    param(
        [string]$Id, [string]$Description,
        $Measured = @{}, $MeasuredPass = $null,
        $Answer = $null, $Expect = $null,
        [string]$Notes = '', [switch]$Info, [bool]$AnswerRequired = $true
    )
    $skipped = $false; $answerOk = $null
    if ($Expect) {
        if ($Answer -eq 'y' -or $Answer -eq 'n') { $answerOk = ($Answer -eq $Expect) } else { $skipped = $true }
    }
    if ($Info) { $verdict = 'INFO' }
    elseif ($MeasuredPass -eq $false -or $answerOk -eq $false) { $verdict = 'FAIL' }
    elseif ($skipped -and ($AnswerRequired -or $MeasuredPass -ne $true)) { $verdict = 'SKIP' }
    elseif ($MeasuredPass -eq $true -or $answerOk -eq $true) { $verdict = 'PASS' }
    else { $verdict = 'INFO' }
    if ($skipped -and $verdict -eq 'PASS') { $Notes = ("$Notes (user skipped the visual question)").Trim() }
    $entry = [ordered]@{
        id = $Id; description = $Description; measured = $Measured
        userAnswer = $Answer; verdict = $verdict; notes = $Notes
    }
    $script:Results.Add($entry)
    Save-Results
    $color = switch ($verdict) { 'PASS' { 'Green' } 'FAIL' { 'Red' } 'SKIP' { 'DarkYellow' } default { 'Gray' } }
    Write-Host ("  [{0}] {1} - {2}" -f $verdict, $Id, $Description) -ForegroundColor $color
    if ($Notes) { Write-Host "         $Notes" -ForegroundColor DarkGray }
    return $verdict
}

# ---------- generic ----------
function Wait-Until([scriptblock]$Condition, [int]$TimeoutMs, [int]$PollMs = 100) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        $ok = $false
        try { $ok = [bool](& $Condition) } catch { $ok = $false }
        if ($ok) { return [int]$sw.ElapsedMilliseconds }
        if ($sw.ElapsedMilliseconds -ge $TimeoutMs) { return -1 }
        Start-Sleep -Milliseconds $PollMs
    }
}

function Read-SharedText([string]$Path) {
    try {
        if (-not (Test-Path -LiteralPath $Path)) { return '' }
        $fs = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
        try { $sr = [System.IO.StreamReader]::new($fs); return $sr.ReadToEnd() } finally { $fs.Dispose() }
    } catch { return '' }
}
function Get-LogLength([string]$Path) { (Read-SharedText $Path).Length }
function Get-LogSince([string]$Path, [int]$Offset) {
    $t = Read-SharedText $Path
    if ($t.Length -lt $Offset) { return $t }
    return $t.Substring($Offset)
}
function Count-Matches([string]$Text, [string]$Pattern) { ([regex]::Matches($Text, $Pattern)).Count }

function Get-WatchdogInfo {
    @(Get-CimInstance Win32_Process -Filter "Name='NothingShell.Watchdog.exe'" -ErrorAction SilentlyContinue |
        Select-Object ProcessId, CommandLine)
}
function Get-AppProcess { @(Get-Process -Name 'NothingShell' -ErrorAction SilentlyContinue) }

function Read-StateFile {
    if (-not (Test-Path -LiteralPath $StateFile)) { return $null }
    try { return (Get-Content -LiteralPath $StateFile -Raw | ConvertFrom-Json) } catch { return 'unreadable' }
}

function Record-Preexisting {
    foreach ($n in ($SpikeNames + 'NothingShell' + 'NothingShell.Watchdog')) {
        $script:PreexistingIds[$n] = @(Get-Process -Name $n -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
    }
}

# Stops processes of this name that we did not find at the beginning.
function Stop-OursByName([string]$Name) {
    $pre = @($script:PreexistingIds[$Name])
    foreach ($p in @(Get-Process -Name $Name -ErrorAction SilentlyContinue)) {
        if ($pre -notcontains $p.Id) { try { Stop-Process -Id $p.Id -Force -ErrorAction Stop } catch { } }
    }
}

# ---------- taskbar safety ----------
function Invoke-RestoreNow {
    if (-not (Test-Path -LiteralPath $Watchdog)) { [void][NsWin]::ShowTrays(); return -1 }
    try {
        $p = Start-Process -FilePath $Watchdog -ArgumentList @('--restore-now', '--state', ('"{0}"' -f $StateDir)) -Wait -PassThru -WindowStyle Hidden
        return $p.ExitCode
    } catch { Write-Bad "restore-now failed to run: $($_.Exception.Message)"; return -2 }
}

function Invoke-Unelevated([string]$CommandLine, [string]$LogFile, [switch]$Wait, [int]$TimeoutSec = 60) {
    # Runs a command with a normal (non-elevated) token through runas /trustlevel; output goes to $LogFile.
    $id = [guid]::NewGuid().ToString('N')
    $cmd = Join-Path $env:TEMP "nsho-$id.cmd"
    $done = Join-Path $env:TEMP "nsho-$id.done"
    $script:TempFiles.Add($cmd); $script:TempFiles.Add($done)
    $lines = @('@echo off', "$CommandLine > `"$LogFile`" 2>&1", "echo done> `"$done`"")
    Set-Content -Path $cmd -Value $lines -Encoding ASCII
    Start-Process -FilePath 'runas.exe' -ArgumentList ('/trustlevel:0x20000 "\"{0}\""' -f $cmd) -WindowStyle Hidden | Out-Null
    if ($Wait) {
        $ms = Wait-Until { Test-Path -LiteralPath $done } ($TimeoutSec * 1000) 200
        return ($ms -ge 0)
    }
    return $true
}

function Ensure-Explorer {
    if (@(Get-Process -Name explorer -ErrorAction SilentlyContinue).Count -eq 0) {
        Write-Info 'Explorer is not running; starting it...'
        [void](Invoke-Unelevated 'explorer.exe' (Join-Path $env:TEMP 'nsho-explorer.txt'))
        [void](Wait-Until { [NsWin]::HasTray() } 15000 250)
    }
}

# Make sure the taskbar is visible again; uses the watchdog's --restore-now.
function Ensure-TaskbarBack([string]$Why = '') {
    if ([NsWin]::TrayState() -eq 1) { return $true }
    Write-Info "Putting the taskbar back $Why..."
    Ensure-Explorer
    $rc = Invoke-RestoreNow
    if ($rc -eq 3) { Ensure-Explorer; $rc = Invoke-RestoreNow }
    [void][NsWin]::ShowTrays()
    return ([NsWin]::TrayState() -eq 1)
}

function Invoke-Cleanup {
    if ($script:CleanedUp) { return }
    $script:CleanedUp = $true
    Write-Host ''
    Write-Host '  Cleaning up (putting everything back)...' -ForegroundColor Cyan
    try { foreach ($n in $SpikeNames) { Stop-OursByName $n } } catch { }
    try {
        foreach ($p in $script:StartedProcs) {
            try { if ($p -and -not $p.HasExited -and $p.ProcessName -in @('taskmgr', 'notepad')) { [void][NsWin]::CloseProcessWindows([uint32]$p.Id) } } catch { }
        }
    } catch { }
    try { Stop-OursByName 'NothingShell' } catch { }
    try {
        Ensure-Explorer
        $rc = Invoke-RestoreNow
        if ($rc -eq 3) { Ensure-Explorer; $rc = Invoke-RestoreNow }
        Write-Info "restore-now exit code: $rc"
    } catch { Write-Bad "restore failed: $($_.Exception.Message)" }
    try { Stop-OursByName 'NothingShell.Watchdog' } catch { }
    try {
        if ([NsWin]::TrayState() -ne 1) { [void][NsWin]::ShowTrays() }
        if ($null -ne $script:OrigAutoHide -and [NsWin]::HasTray() -and ([NsWin]::GetAutoHide() -ne $script:OrigAutoHide)) {
            [NsWin]::SetAutoHide($script:OrigAutoHide)
            Write-Info "auto-hide set back to the original ($($script:OrigAutoHide))"
        }
    } catch { }
    foreach ($f in $script:TempFiles) { try { Remove-Item -LiteralPath $f -Force -ErrorAction SilentlyContinue } catch { } }
    if ($script:BannersOff) {
        Write-Host ''
        Write-Host '  !! REMINDER: please turn "Show notification banners" back ON for Windows PowerShell' -ForegroundColor Yellow
        Write-Host '     (Settings > System > Notifications > Windows PowerShell).' -ForegroundColor Yellow
    }
}

# ---------- app / window helpers ----------
function Start-App {
    if ((Get-AppProcess).Count -gt 0) { throw 'NothingShell is already running before a test step; aborting.' }
    $p = Start-Process -FilePath $AppExe -WorkingDirectory $AppDir -PassThru
    $script:StartedProcs.Add($p)
    Start-Sleep -Seconds 3
    $p.Refresh()
    return $p
}

function Quit-App($p) {
    $ok = $false
    try { $p.Refresh(); $ok = $p.CloseMainWindow() } catch { }
    if (-not $ok) { try { [void][NsWin]::CloseProcessWindows([uint32]$p.Id) } catch { } }
}

function Test-WatchdogForApp($app) {
    $res = @{ found = $false; pid = $null; parentStartDiffTicks = $null; commandLine = $null; parentStartMatches = $false }
    $expected = $app.StartTime.ToUniversalTime().ToFileTimeUtc()
    foreach ($w in (Get-WatchdogInfo)) {
        $cl = [string]$w.CommandLine
        $mParent = [regex]::Match($cl, '--parent\s+(\d+)')
        if ($mParent.Success -and [int]$mParent.Groups[1].Value -eq $app.Id) {
            $res.found = $true; $res.pid = $w.ProcessId; $res.commandLine = $cl
            $mStart = [regex]::Match($cl, '--parent-start\s+(\d+)')
            if ($mStart.Success) { $res.parentStartDiffTicks = [math]::Abs([int64]$mStart.Groups[1].Value - $expected) }
        }
    }
    $res.parentStartMatches = ($null -ne $res.parentStartDiffTicks -and $res.parentStartDiffTicks -le 10000000)
    return $res
}

function Find-SpikeExe([string]$Name) {
    $c = foreach ($cfg in 'Release', 'Debug') {
        Get-ChildItem (Join-Path $Repo "spikes\$Name\bin\$cfg") -Recurse -Filter "$Name.exe" -ErrorAction SilentlyContinue
    }
    $best = $c | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $best) {
        Write-Info "Building $Name (not built yet)..."
        $dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
        if (-not $dotnet) { $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
        & $dotnet build (Join-Path $Repo "spikes\$Name") -c Debug --nologo -v q | Out-Host
        $best = Get-ChildItem (Join-Path $Repo "spikes\$Name\bin\Debug") -Recurse -Filter "$Name.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    }
    if (-not $best) { throw "$Name.exe not found." }
    return $best.FullName
}

# Starts a long-running spike (normal user token if possible), output into $LogFile. Returns $true when its process is up.
function Start-Spike([string]$Name, [string]$Exe, [string]$ArgString, [string]$LogFile) {
    Stop-OursByName $Name
    Set-Content -Path $LogFile -Value '' -Encoding ASCII
    $script:TempFiles.Add($LogFile)
    $pre = @(Get-Process -Name $Name -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
    $mode = 'normal user'
    [void](Invoke-Unelevated ('"{0}" {1}' -f $Exe, $ArgString) $LogFile)
    $up = Wait-Until { Get-Process -Name $Name -ErrorAction SilentlyContinue | Where-Object { $pre -notcontains $_.Id } } 8000 200
    if ($up -lt 0) {
        $mode = 'elevated (fallback)'
        Write-Info 'Could not start it with a normal token; starting it the elevated way instead.'
        $sp = @{ FilePath = $Exe; PassThru = $true; WindowStyle = 'Hidden'; RedirectStandardOutput = $LogFile; RedirectStandardError = "$LogFile.err" }
        if ($ArgString) { $sp.ArgumentList = $ArgString }
        $p = Start-Process @sp
        $script:StartedProcs.Add($p)
        $script:TempFiles.Add("$LogFile.err")
        $up = Wait-Until { Get-Process -Name $Name -ErrorAction SilentlyContinue | Where-Object { $pre -notcontains $_.Id } } 5000 200
    }
    $script:LastSpikeMode = $mode
    return ($up -ge 0)
}
# ===== END HELPERS =====

# =====================================================================================
# MAIN
# =====================================================================================
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host ''
    Write-Host 'This script must be run from an ELEVATED PowerShell 7 window.' -ForegroundColor Red
    Write-Host 'Right-click PowerShell 7 > "Run as administrator", then run it again.' -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $SddDir)) { New-Item -ItemType Directory -Force $SddDir | Out-Null }
$LogPath = Join-Path $SddDir "handson-$Stamp.log"
Start-Transcript -Path $LogPath -Force | Out-Null

# Last-resort safety if the console window is closed.
$null = Register-EngineEvent -SourceIdentifier PowerShell.Exiting -Action {
    try {
        $wd = Join-Path $env:ProgramFiles 'NothingShell\NothingShell.Watchdog.exe'
        if (Test-Path $wd) { Start-Process -FilePath $wd -ArgumentList @('--restore-now', '--state', ('"{0}"' -f (Join-Path $env:APPDATA 'NothingShell'))) -Wait -WindowStyle Hidden }
    } catch { }
}

$fatal = $null
try {
    Clear-Host
    Write-Host ''
    Write-Host '  NOTHING SHELL - HANDS-ON CHECK (about 20-30 minutes)' -ForegroundColor Cyan
    Write-Host '  I will start and stop the app and some small test programs, and ask you'
    Write-Host '  simple yes/no questions about what you see. The Windows taskbar will'
    Write-Host '  disappear on purpose several times; it always comes back at the end.'
    Write-Host '  If something looks stuck, press Ctrl+C in this window: everything is put back.'
    Write-Host ''
    Write-Host "  Log file:     $LogPath"
    Write-Host "  Results file: $($script:ResultsPath)"
    Write-Host ''
    Wait-Enter 'Press Enter to begin'

    # ---------------------------------------------------------------- setup
    Write-Step 'SETUP: installing the latest app and test programs'
    Record-Preexisting
    $pwsh = (Get-Process -Id $PID).Path
    $installOk = $false
    foreach ($noTs in $false, $true) {
        $extra = if ($noTs) { @('-NoTimestamp') } else { @() }
        Write-Info ("Running build\install.ps1 {0}" -f ($extra -join ' '))
        & $pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Repo 'build\install.ps1') @extra 2>&1 | ForEach-Object { Write-Host "    $_" }
        if ($LASTEXITCODE -eq 0) { $installOk = $true; break }
        Write-Bad "install.ps1 failed (exit $LASTEXITCODE)."
        if (-not $noTs) { Write-Info 'Retrying once without the online timestamp...' }
    }
    if (-not $installOk) { throw 'build\install.ps1 failed. Nothing else was changed.' }
    if (-not (Test-Path $AppExe) -or -not (Test-Path $Watchdog)) { throw "Install finished but $AppExe or the watchdog is missing." }

    $spikesOk = $false
    foreach ($noTs in $false, $true) {
        $extra = if ($noTs) { @('-NoTimestamp') } else { @() }
        Write-Info ("Running spikes\install-spikes.ps1 {0}" -f ($extra -join ' '))
        & $pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Repo 'spikes\install-spikes.ps1') @extra 2>&1 | ForEach-Object { Write-Host "    $_" }
        if ($LASTEXITCODE -eq 0) { $spikesOk = $true; break }
        Write-Bad "install-spikes.ps1 failed (exit $LASTEXITCODE)."
    }
    if (-not $spikesOk) { throw 'spikes\install-spikes.ps1 failed.' }
    Record-Preexisting   # after install: anything left running now is not ours to kill
    Write-Good 'Install done.'

    # ---------------------------------------------------------------- Fenn
    Write-Step 'STEP 1: close Fenn'
    $fenn = @(Get-Process -Name 'fenn' -ErrorAction SilentlyContinue)
    $fennSkipped = $false
    if ($fenn.Count -gt 0) {
        Write-Do 'Please quit Fenn now: find its icon in the tray (bottom right), right-click it and choose Quit.'
        Write-Info 'I will wait until it is gone.  (Press S here to continue anyway - results may then be wrong.)'
        while (@(Get-Process -Name 'fenn' -ErrorAction SilentlyContinue).Count -gt 0) {
            try {
                if ([Console]::KeyAvailable) {
                    $k = [Console]::ReadKey($true)
                    if ($k.KeyChar -in 's', 'S') { $fennSkipped = $true; break }
                }
            } catch { }
            Start-Sleep -Milliseconds 500
        }
    }
    if ($fennSkipped) { Write-Bad 'Continuing with Fenn still running (recorded).' } else { Write-Good 'Fenn is not running.' }
    Start-Sleep -Seconds 2

    # A leftover state file from an earlier run would change the starting point: give the taskbar back first.
    $leftover = Read-StateFile
    if ($null -ne $leftover) {
        Write-Info "An old taskbar-restore.json was found ($($leftover | ConvertTo-Json -Compress)); restoring from it first."
        [void](Invoke-RestoreNow)
        Start-Sleep -Seconds 1
    }
    $script:OrigAutoHide = [NsWin]::GetAutoHide()
    $script:OrigTray     = [NsWin]::TrayState()
    $mon = [NsWin]::PrimaryMonitor()
    $startInfo = @{
        autoHide = $script:OrigAutoHide; trayState = $script:OrigTray
        fennRunning = (@(Get-Process -Name fenn -ErrorAction SilentlyContinue).Count -gt 0)
        fennSkipped = $fennSkipped; leftoverStateFileFound = ($null -ne $leftover)
        primaryMonitor = if ($mon) { "$($mon[2] - $mon[0])x$($mon[3] - $mon[1])" } else { $null }
    }
    Write-Info ("Starting point: taskbar auto-hide = {0}, taskbar visible = {1}" -f $script:OrigAutoHide, ($script:OrigTray -eq 1))
    $s0Notes = if ($fennSkipped) { 'Fenn was still running (user skipped); later results may be unreliable.' } elseif ($script:OrigTray -ne 1) { 'Explorer taskbar was NOT visible at the start.' } else { '' }
    [void](Complete-Check -Id 'S0' -Description 'Starting state (Fenn closed): auto-hide and explorer taskbar visibility' -Measured $startInfo -Info -Notes $s0Notes)
    if ($script:OrigTray -eq -1) { throw 'No Windows taskbar (explorer) found at the start; cannot continue.' }

    # ================================================================ TASK 8 CHECKS
    # ---------------------------------------------------------------- C8.1
    Write-Step 'CHECK 1: start the app - the taskbar should disappear'
    $logOff = Get-LogLength $AppLog
    $app = Start-App
    $trayState = [NsWin]::TrayState(); $ah = [NsWin]::GetAutoHide()
    $wdInfo = Test-WatchdogForApp $app
    $logNew = Get-LogSince $AppLog $logOff
    $uiOk = $logNew -match ('startup uiAccess=1 pid={0}\b' -f $app.Id)
    $state = Read-StateFile
    $stateOk = ($null -ne $state -and $state -ne 'unreadable' -and $state.wasAutoHide -eq $script:OrigAutoHide)

    Write-Info 'Now opening Notepad full-screen to see whether it covers the whole screen...'
    $before = @([NsWin]::VisibleByClass('Notepad'))
    $script:npFound = $null
    Start-Process -FilePath 'notepad.exe' -WindowStyle Maximized | Out-Null
    [void](Wait-Until {
        $now = @([NsWin]::VisibleByClass('Notepad') | Where-Object { $before -notcontains $_ })
        if ($now.Count -gt 0) { $script:npFound = $now[0]; $true } else { $false }
    } 10000 250)
    $np = [IntPtr]::Zero
    if ($script:npFound) { $np = [IntPtr]$script:npFound }
    $npM = @{ found = ($np -ne [IntPtr]::Zero) }
    $fills = $null
    if ($np -ne [IntPtr]::Zero) {
        [NsWin]::Maximize($np); Start-Sleep -Milliseconds 1200
        $fb = [NsWin]::FrameBounds($np); $mon = [NsWin]::PrimaryMonitor()
        if ($fb -and $mon) {
            $monH = $mon[3] - $mon[1]; $workH = $mon[7] - $mon[5]
            $npM.frame = ($fb -join ','); $npM.monitorRect = ($mon[0..3] -join ','); $npM.workArea = ($mon[4..7] -join ',')
            $fills = (($fb[3] - $fb[1]) -ge $monH - 2) -and ($fb[3] -ge $mon[3] - 2) -and ($workH -ge $monH - 2)
            $npM.fillsFullHeight = $fills
        }
    }
    Write-Do 'Look at the screen now.'
    $ans = Ask-YN 'Is the Windows taskbar gone (and Notepad covers the whole screen)?'
    if ($np -ne [IntPtr]::Zero) {
        [NsWin]::CloseWindow($np); [void](Wait-Until { -not [NsWin]::WindowExists($np) } 4000 200)
        if ([NsWin]::WindowExists($np)) { Write-Do 'Please close the Notepad window yourself (do not save).' }
    }
    $pass = ($trayState -eq 0) -and $ah -and $wdInfo.found -and $wdInfo.parentStartMatches -and $uiOk -and $stateOk -and ($fills -eq $true)
    [void](Complete-Check -Id 'C8.1' -Description 'App start: taskbar hidden, auto-hide on, watchdog (--parent-start matches), uiAccess=1, maximized window fills screen' `
        -Measured @{ trayState = $trayState; autoHide = $ah; appPid = $app.Id; watchdog = $wdInfo; uiAccessLog = $uiOk; stateFileOk = $stateOk; notepad = $npM } `
        -MeasuredPass $pass -Answer $ans -Expect 'y' -AnswerRequired $false `
        -Notes $(if ($null -eq $fills) { 'Notepad size could not be measured.' } else { '' }))

    # ---------------------------------------------------------------- C8.1b
    Write-Step 'CHECK 1b: the safety helper keeps waiting while the app runs (10 s)'
    $wdPid = $wdInfo.pid
    1..10 | ForEach-Object { Write-Host -NoNewline '.'; Start-Sleep -Seconds 1 }
    Write-Host ''
    $wdAlive = ($null -ne $wdPid) -and [bool](Get-Process -Id $wdPid -ErrorAction SilentlyContinue)
    $tray2 = [NsWin]::TrayState(); $appAlive = [bool](Get-Process -Id $app.Id -ErrorAction SilentlyContinue)
    [void](Complete-Check -Id 'C8.1b' -Description 'After 10 s: watchdog still alive, app alive, taskbar still hidden (no fail-open)' `
        -Measured @{ watchdogPid = $wdPid; watchdogAlive = $wdAlive; appAlive = $appAlive; trayState = $tray2 } `
        -MeasuredPass ($wdAlive -and $appAlive -and $tray2 -eq 0))

    # ---------------------------------------------------------------- C8.2
    Write-Step 'CHECK 2: quit the app normally - the taskbar should come back'
    Quit-App $app
    $backMs = Wait-Until { [NsWin]::TrayState() -eq 1 } 6000 100
    $exited = $app.WaitForExit(5000)
    Start-Sleep -Milliseconds 800
    $ah2 = [NsWin]::GetAutoHide()
    $appId2 = $app.Id
    $wdGone = (Wait-Until { -not (Get-WatchdogInfo | Where-Object { $_.CommandLine -match ('--parent\s+{0}\b' -f $appId2) }) } 3000 200) -ge 0
    $stateGone = -not (Test-Path -LiteralPath $StateFile)
    $ans = Ask-YN 'Is the Windows taskbar back?'
    $pass = ($backMs -ge 0 -and $backMs -le 3000) -and $exited -and ($ah2 -eq $script:OrigAutoHide) -and $stateGone -and $wdGone
    [void](Complete-Check -Id 'C8.2' -Description 'Normal quit: taskbar back within 3 s, auto-hide = original, state file deleted, watchdog exited' `
        -Measured @{ trayBackMs = $backMs; appExited = $exited; autoHideNow = $ah2; autoHideOriginal = $script:OrigAutoHide; stateFileDeleted = $stateGone; watchdogExited = $wdGone } `
        -MeasuredPass $pass -Answer $ans -Expect 'y' -AnswerRequired $false)
    [void](Ensure-TaskbarBack 'after check 2')

    # ---------------------------------------------------------------- C8.3
    Write-Step 'CHECK 3: the app is force-killed - the taskbar must come back by itself'
    $app = Start-App
    $pre = [NsWin]::TrayState()
    Write-Info "(taskbar hidden before the kill: $($pre -eq 0))"
    Stop-Process -Id $app.Id -Force
    $backMs = Wait-Until { [NsWin]::TrayState() -eq 1 } 10000 100
    Start-Sleep -Milliseconds 1500
    $ah3 = [NsWin]::GetAutoHide(); $stateGone = -not (Test-Path -LiteralPath $StateFile)
    $wdGone = (Wait-Until { (Get-WatchdogInfo).Count -eq 0 } 4000 200) -ge 0
    [void](Complete-Check -Id 'C8.3' -Description 'Force-kill app: explorer taskbar visible again within 3 s, auto-hide restored' `
        -Measured @{ hiddenBeforeKill = ($pre -eq 0); trayBackMs = $backMs; autoHideNow = $ah3; autoHideOriginal = $script:OrigAutoHide; stateFileDeleted = $stateGone; watchdogExited = $wdGone } `
        -MeasuredPass (($pre -eq 0) -and $backMs -ge 0 -and $backMs -le 3000 -and $ah3 -eq $script:OrigAutoHide))
    [void](Ensure-TaskbarBack 'after check 3')

    # ---------------------------------------------------------------- C8.4
    Write-Step 'CHECK 4: Explorer restarts while the app runs - the taskbar must be hidden again'
    Write-Do 'The screen will flicker for a few seconds. This is expected.'
    $logOff = Get-LogLength $AppLog
    $app = Start-App
    $pre = [NsWin]::TrayState()
    Stop-Process -Name explorer -Force
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $trayMs = Wait-Until { [NsWin]::HasTray() } 5000 50
    $startedExplorer = $false
    if ($trayMs -lt 0) {
        Write-Info 'Explorer did not restart by itself; starting it.'
        $startedExplorer = $true
        Ensure-Explorer
        $trayMs = [int]$sw.ElapsedMilliseconds
    }
    $hideMs = Wait-Until { [NsWin]::TrayState() -eq 0 } 5000 20
    $ah4 = $null; try { $ah4 = [NsWin]::GetAutoHide() } catch { }
    $logNew = Get-LogSince $AppLog $logOff
    $sawMsg = $logNew -match 'TaskbarCreated: re-hiding taskbar'
    $appAlive = [bool](Get-Process -Id $app.Id -ErrorAction SilentlyContinue)
    $ans1 = Ask-YN 'Is the Windows taskbar hidden again (after the flicker)?'
    $pass = ($pre -eq 0) -and $appAlive -and $hideMs -ge 0 -and $hideMs -le 2000
    [void](Complete-Check -Id 'C8.4a' -Description 'Explorer restart: new tray hidden again by the app within 2 s of appearing' `
        -Measured @{ hiddenBeforeRestart = ($pre -eq 0); newTrayAppearedMs = $trayMs; hiddenAfterTrayMs = $hideMs; autoHide = $ah4; taskbarCreatedLogged = $sawMsg; appStillAlive = $appAlive; explorerStartedByScript = $startedExplorer } `
        -MeasuredPass $pass -Answer $ans1 -Expect 'y' -AnswerRequired $false)
    Write-Info 'Now quitting the app normally...'
    Quit-App $app
    $backMs = Wait-Until { [NsWin]::TrayState() -eq 1 } 6000 100
    [void]$app.WaitForExit(5000)
    $ans2 = Ask-YN 'Is the Windows taskbar back?'
    [void](Complete-Check -Id 'C8.4b' -Description 'After the Explorer restart, normal quit brings the taskbar back' `
        -Measured @{ trayBackMs = $backMs } -MeasuredPass ($backMs -ge 0 -and $backMs -le 3000) -Answer $ans2 -Expect 'y' -AnswerRequired $false)
    [void](Ensure-TaskbarBack 'after check 4')

    # ---------------------------------------------------------------- C8.5
    Write-Step 'CHECK 5: helper AND app both killed - the next normal quit must restore the ORIGINAL setting'
    Write-Do 'The taskbar will stay hidden for a few seconds ON PURPOSE (nothing is there to restore it yet).'
    $c5Orig = $script:OrigAutoHide
    $c5Note = ''
    if ($c5Orig) {
        # Original was auto-hide ON: the app also sets it ON, so the test could not tell. Turn it OFF for this test only.
        [NsWin]::SetAutoHide($false); Start-Sleep -Milliseconds 800
        $c5Orig = [NsWin]::GetAutoHide()
        $c5Note = 'Auto-hide was ON originally; it was switched OFF just for this check so the test is meaningful, and is put back to ON afterwards.'
    }
    $app1 = Start-App
    $wd1 = Test-WatchdogForApp $app1
    $state1 = Read-StateFile
    if ($wd1.found) { Stop-Process -Id $wd1.pid -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
    Stop-Process -Id $app1.Id -Force
    Start-Sleep -Seconds 4
    $stayedHidden = ([NsWin]::TrayState() -eq 0)
    $stateLeft = Read-StateFile
    Write-Info ("After killing both: taskbar still hidden = {0}, state file present = {1}" -f $stayedHidden, ($null -ne $stateLeft))
    $logOff = Get-LogLength $AppLog
    $app2 = Start-App
    $logNew = Get-LogSince $AppLog $logOff
    $leftoverSeen = $logNew -match 'leftover=True'
    $origLogged = [regex]::Match($logNew, 'taskbar original autoHide=(True|False)').Groups[1].Value
    Quit-App $app2
    $backMs = Wait-Until { [NsWin]::TrayState() -eq 1 } 6000 100
    [void]$app2.WaitForExit(5000)
    Start-Sleep -Milliseconds 800
    $ah5 = [NsWin]::GetAutoHide(); $stateGone = -not (Test-Path -LiteralPath $StateFile)
    $ans = Ask-YN 'Is the Windows taskbar back?'
    $pass = $wd1.found -and $stayedHidden -and ($null -ne $stateLeft) -and $leftoverSeen -and ($backMs -ge 0) -and ($ah5 -eq $c5Orig) -and $stateGone
    [void](Complete-Check -Id 'C8.5' -Description 'Kill watchdog then app, restart, quit: auto-hide returns to the ORIGINAL (leftover state file wins)' `
        -Measured @{ testOriginalAutoHide = $c5Orig; recordedOriginalAtStart = $script:OrigAutoHide; stateFileWrittenByFirstRun = $state1; watchdogKilled = $wd1.found
                     taskbarStayedHiddenAfterBothKilled = $stayedHidden; leftoverStateFileSurvived = ($null -ne $stateLeft); secondRunSawLeftover = $leftoverSeen
                     secondRunLoggedOriginal = $origLogged; trayBackMs = $backMs; autoHideAfterQuit = $ah5; stateFileDeleted = $stateGone } `
        -MeasuredPass $pass -Answer $ans -Expect 'y' -AnswerRequired $false -Notes $c5Note)
    [void](Ensure-TaskbarBack 'after check 5')
    if ($null -ne $script:OrigAutoHide -and [NsWin]::GetAutoHide() -ne $script:OrigAutoHide) {
        [NsWin]::SetAutoHide($script:OrigAutoHide); Write-Info "Auto-hide put back to your original setting ($($script:OrigAutoHide))."
    }
    Stop-OursByName 'NothingShell.Watchdog'

    # ================================================================ SPIKES
    # ---------------------------------------------------------------- KeySpike
    Write-Step 'SPIKE 1: Windows key (KeySpike)'
    Write-Info 'A small test program will watch the Windows key. A black window may pop up: leave it alone.'
    Write-Info 'After each key test: close anything that opened (Esc), then click THIS window and answer.'
    $keyExe = Join-Path $SpikeRoot 'KeySpike\KeySpike.exe'
    $keyLog = Join-Path $env:TEMP "nsho-keyspike-$Stamp.txt"
    if (-not (Test-Path $keyExe)) { throw "KeySpike not installed at $keyExe" }
    $up = Start-Spike 'KeySpike' $keyExe '' $keyLog
    $hookUp = (Wait-Until { (Read-SharedText $keyLog) -match 'hook installed' } 6000 200) -ge 0
    [void](Complete-Check -Id 'K.0' -Description 'KeySpike started and hook installed' -Measured @{ processUp = $up; hookInstalled = $hookUp; launchedAs = $script:LastSpikeMode } -MeasuredPass ($up -and $hookUp))
    if ($up -and $hookUp) {
        $keyTests = @(
            @{ id = 'K.1'; desc = 'Win key alone: Start does NOT open, launcher event logged'; todo = 'Tap the Windows key ONCE, by itself.'; q = 'Did the Start menu open?'; expect = 'n'; launcherMin = 1; launcherMax = 99; panelMin = 0 }
            @{ id = 'K.2'; desc = 'Win+A: Quick Settings does NOT open (panel event logged)'; todo = 'Press Win+A.'; q = 'Did Quick Settings (wifi/volume panel) open?'; expect = 'n'; launcherMin = 0; launcherMax = 0; panelMin = 1 }
            @{ id = 'K.3'; desc = 'Win+N: Notification Center does NOT open (panel event logged)'; todo = 'Press Win+N.'; q = 'Did the Notification Center / calendar open?'; expect = 'n'; launcherMin = 0; launcherMax = 0; panelMin = 1 }
            @{ id = 'K.4'; desc = 'Win+E opens File Explorer as usual'; todo = 'Press Win+E, look, then close the window that opens.'; q = 'Did a File Explorer window open?'; expect = 'y'; launcherMin = 0; launcherMax = 0; panelMin = 0 }
            @{ id = 'K.5'; desc = 'Win+D twice behaves normally'; todo = 'Press Win+D (desktop shows), then Win+D again (windows come back).'; q = 'Did the desktop show and then your windows come back?'; expect = 'y'; launcherMin = 0; launcherMax = 0; panelMin = 0 }
            @{ id = 'K.6'; desc = 'Win+Tab opens Task View'; todo = 'Press Win+Tab, look, then press Esc.'; q = 'Did Task View open?'; expect = 'y'; launcherMin = 0; launcherMax = 0; panelMin = 0 }
            @{ id = 'K.7'; desc = 'Win+Shift+S opens the screenshot bar'; todo = 'Press Win+Shift+S, look at the top of the screen, then press Esc to cancel.'; q = 'Did the screenshot toolbar appear?'; expect = 'y'; launcherMin = 0; launcherMax = 0; panelMin = 0 }
            @{ id = 'K.8'; desc = 'Win+Arrow snaps a window'; todo = 'Open any window (e.g. Notepad), click it, press Win+Left then Win+Right, then close it.'; q = 'Did the window snap to the left and then to the right?'; expect = 'y'; launcherMin = 0; launcherMax = 0; panelMin = 0 }
        )
        foreach ($t in $keyTests) {
            Write-Host ''
            Write-Do $t.todo
            $off = Get-LogLength $keyLog
            $ans = Ask-YN $t.q
            $new = Get-LogSince $keyLog $off
            $nl = Count-Matches $new 'OPEN LAUNCHER'; $npn = Count-Matches $new 'OPEN PANEL'
            $mp = ($nl -ge $t.launcherMin) -and ($nl -le $t.launcherMax) -and ($npn -ge $t.panelMin)
            $mpArg = if ($ans -eq 's') { $null } else { $mp }
            [void](Complete-Check -Id $t.id -Description $t.desc -Measured @{ openLauncherLogged = $nl; openPanelLogged = $npn; spikeLog = @($new.Trim() -split "`r?`n" | Select-Object -Last 8) } `
                -MeasuredPass $mpArg -Answer $ans -Expect $t.expect -AnswerRequired $true)
        }

        # Task Manager (runs elevated: this is what uiAccess is for)
        Write-Host ''
        Write-Do 'Next: Task Manager. It will open; click on it so it is the active window, then tap the Windows key ONCE by itself.'
        $tmBefore = @(Get-Process -Name taskmgr -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
        try { $tm = Start-Process -FilePath 'taskmgr.exe' -PassThru; $script:StartedProcs.Add($tm) } catch { Write-Bad "Could not start Task Manager: $($_.Exception.Message)" }
        Start-Sleep -Seconds 2
        $off = Get-LogLength $keyLog
        $ans = Ask-YN 'With Task Manager active, did the Start menu open when you tapped Win?'
        $new = Get-LogSince $keyLog $off
        $nl = Count-Matches $new 'OPEN LAUNCHER'
        $mpArg = if ($ans -eq 's') { $null } else { $nl -ge 1 }
        [void](Complete-Check -Id 'K.9' -Description 'Win alone while (elevated) Task Manager is focused: Start does NOT open, launcher event logged' `
            -Measured @{ openLauncherLogged = $nl; spikeLog = @($new.Trim() -split "`r?`n" | Select-Object -Last 5) } `
            -MeasuredPass $mpArg -Answer $ans -Expect 'n')
        foreach ($p in @(Get-Process -Name taskmgr -ErrorAction SilentlyContinue)) {
            if ($tmBefore -notcontains $p.Id) { try { [void][NsWin]::CloseProcessWindows([uint32]$p.Id); Start-Sleep -Milliseconds 800; if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } } catch { } }
        }

        # Optional Win+L
        Write-Host ''
        Write-Host '  OPTIONAL: Win+L (lock the PC). This will LOCK your PC; you sign back in and come back here.' -ForegroundColor Yellow
        $r = (Read-Host '  ? Run the Win+L test? [y = yes, anything else = skip]').Trim().ToLowerInvariant()
        if ($r -eq 'y') {
            Write-Do 'Press Win+L now. Sign back in, then come back to this window.'
            $ans = Ask-YN 'Did the PC lock normally (and did you come back fine)?'
            [void](Complete-Check -Id 'K.10' -Description 'Win+L locks normally' -Measured @{} -Answer $ans -Expect 'y')
        } else { [void](Complete-Check -Id 'K.10' -Description 'Win+L locks normally (optional)' -Measured @{} -Answer 's' -Expect 'y' -Notes 'Skipped by default.') }
    }
    Stop-OursByName 'KeySpike'
    Start-Sleep -Milliseconds 500

    # ---------------------------------------------------------------- OverflowSpike
    Write-Step 'SPIKE 2: hidden tray icons (OverflowSpike) - with the app hiding the taskbar'
    Write-Info 'I start the app (taskbar hides), then run the test twice. WATCH the bottom and right of the screen.'
    $ovExe = Find-SpikeExe 'OverflowSpike'
    $app = Start-App
    Write-Info ("taskbar hidden by the app: {0}" -f ([NsWin]::TrayState() -eq 0))
    $ovRuns = @(
        @{ id = 'O.1'; args = @('--run', '--early-hide'); name = 'show/invoke/hide with early hide' },
        @{ id = 'O.2'; args = @('--run'); name = 'show/invoke/hide, hide after flyout' }
    )
    foreach ($run in $ovRuns) {
        Write-Do "Watch the screen: running the test ($($run.name))..."
        $out = @(& $ovExe @($run.args) 2>&1 | ForEach-Object { "$_" })
        $out | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
        $joined = $out -join "`n"
        $s2 = $joined -match 'RESULT strategy 2 WORKS'
        $s3 = $joined -match 'Win\+B[\s\S]*RESULT overflow window appeared'
        $vis = @([regex]::Matches($joined, 'taskbar visible from first show to hide call[^:]*: (\d+) ms') | ForEach-Object { [int]$_.Groups[1].Value })
        $qa = Ask-YN 'Did the little hidden-icons flyout pop up at the bottom right?'
        $qb = Ask-YN 'Did you see the taskbar flash on screen?   (answer n if you saw nothing)'
        [void](Complete-Check -Id $run.id -Description "Overflow flyout works while taskbar is hidden ($($run.name))" `
            -Measured @{ strategy2Worked = $s2; strategy3WinBWorked = $s3; taskbarVisibleMs = $vis; sawFlyout = $qa; sawTaskbarFlash = $qb; output = $out } `
            -MeasuredPass ($s2 -and $s3) -Answer $qa -Expect 'y' -AnswerRequired $false `
            -Notes "Taskbar flash seen by user: $qb (target: under 300 ms, barely visible)")
        Start-Sleep -Seconds 1
    }
    Write-Host ''
    Write-Do 'Now by hand: press Win+B, then Enter. The hidden-icons flyout should open.'
    $a1 = Ask-YN 'Does the flyout show your hidden tray icons (e.g. Discord, Steam)?'
    Write-Do 'Right-click ONE icon in the flyout. A menu should appear. Try one harmless item, or press Esc twice.'
    $a2 = Ask-YN 'Did the right-click menu appear and work?'
    Write-Do 'Press Esc to close everything.'
    [void](Complete-Check -Id 'O.3' -Description 'Win+B then Enter shows correct hidden icons (taskbar hidden)' -Measured @{} -Answer $a1 -Expect 'y')
    [void](Complete-Check -Id 'O.4' -Description 'Right-click menus on flyout icons work' -Measured @{} -Answer $a2 -Expect 'y')
    Write-Info 'Quitting the app...'
    Quit-App $app
    [void](Wait-Until { [NsWin]::TrayState() -eq 1 } 6000 100); [void]$app.WaitForExit(5000)
    [void](Ensure-TaskbarBack 'after overflow test')
    $ahNow = [NsWin]::GetAutoHide()
    [void](Complete-Check -Id 'O.5' -Description 'Taskbar returns to original state after the overflow test' -Measured @{ autoHideNow = $ahNow; autoHideOriginal = $script:OrigAutoHide; trayVisible = ([NsWin]::TrayState() -eq 1) } `
        -MeasuredPass (([NsWin]::TrayState() -eq 1) -and $ahNow -eq $script:OrigAutoHide))
    Stop-OursByName 'NothingShell.Watchdog'

    # ---------------------------------------------------------------- DesktopLayerSpike
    Write-Step 'SPIKE 3: desktop widget (DesktopLayerSpike)'
    $dlExe = Find-SpikeExe 'DesktopLayerSpike'
    $dlLog = Join-Path $env:TEMP "nsho-desktoplayer-$Stamp.txt"
    $up = Start-Spike 'DesktopLayerSpike' $dlExe '--option A --stay' $dlLog
    Start-Sleep -Seconds 3
    [void](Complete-Check -Id 'D.0' -Description 'DesktopLayerSpike started' -Measured @{ processUp = $up; launchedAs = $script:LastSpikeMode } -MeasuredPass $up)
    if ($up) {
        Write-Do 'Press Win+D to show the desktop. Look for a grey square saying "clicks: 0" on the left (about 100 px in, 300 px down).'
        Write-Do 'Click it with the mouse. Then press Win+D again to come back to this window.'
        $a1 = Ask-YN 'Did you see the grey square on the desktop (and still after Win+D)?'
        $a2 = Ask-YN 'Did the number in the square go up when you clicked it?'
        [void](Complete-Check -Id 'D.1' -Description 'Widget visible on the desktop' -Measured @{} -Answer $a1 -Expect 'y')
        [void](Complete-Check -Id 'D.2' -Description 'Widget clickable' -Measured @{} -Answer $a2 -Expect 'y')

        Write-Host ''
        Write-Do 'Next I restart Windows Explorer. The screen will flicker for a few seconds; this is normal.'
        Wait-Enter 'Press Enter to restart Explorer'
        $off = Get-LogLength $dlLog
        Stop-Process -Name explorer -Force
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $trayMs = Wait-Until { [NsWin]::HasTray() } 5000 50
        $startedExplorer = $false
        if ($trayMs -lt 0) { $startedExplorer = $true; Ensure-Explorer; $trayMs = [int]$sw.ElapsedMilliseconds }
        $reMs = Wait-Until { (Get-LogSince $dlLog $off) -match 'WATCH: re-attached' } 15000 200
        $new = Get-LogSince $dlLog $off
        $watch = @($new -split "`r?`n" | Where-Object { $_ -match 'WATCH' })
        $m = [regex]::Match($new, '(\d+) ms since new Progman first seen')
        $sinceProgman = if ($m.Success) { [int]$m.Groups[1].Value } else { $null }
        $a3 = Ask-YN 'After Explorer came back: is the grey square on the desktop again (press Win+D to check) and does clicking it still work?'
        [void](Complete-Check -Id 'D.3' -Description 'Widget re-attaches after Explorer restart (within 2 s of Progman reappearing)' `
            -Measured @{ trayAppearedMs = $trayMs; reattachedLogged = ($reMs -ge 0); msSinceProgmanFirstSeen = $sinceProgman; watchLines = $watch; explorerStartedByScript = $startedExplorer } `
            -MeasuredPass ($reMs -ge 0 -and $null -ne $sinceProgman -and $sinceProgman -le 2000) -Answer $a3 -Expect 'y' -AnswerRequired $false)

        Write-Do 'Now press and HOLD Alt, tap Tab and look at the list of windows. Then release / press Esc.'
        $a4 = Ask-YN 'Is "DesktopLayerSpike" (the grey square) listed in the Alt+Tab list?'
        [void](Complete-Check -Id 'D.4' -Description 'Widget is NOT in the Alt+Tab list' -Measured @{} -Answer $a4 -Expect 'n')
    }
    Stop-OursByName 'DesktopLayerSpike'
    [void](Ensure-TaskbarBack 'after desktop test')

    # ---------------------------------------------------------------- NotifySpike
    Write-Step 'SPIKE 4: notifications (NotifySpike)'
    $nsExe = Join-Path $SpikeRoot 'NotifySpike\NotifySpike.exe'
    $toastPs = Join-Path $Repo 'spikes\NotifySpike\send-test-toast.ps1'
    if (-not (Test-Path $nsExe)) { throw "NotifySpike not installed at $nsExe" }
    $nsLog = Join-Path $env:TEMP "nsho-notify1-$Stamp.txt"
    $up = Start-Spike 'NotifySpike' $nsExe '--seconds 90 --expect "NothingShell spike test"' $nsLog
    $listening = (Wait-Until { (Read-SharedText $nsLog) -match 'Listening' } 20000 250) -ge 0
    $txt = Read-SharedText $nsLog
    $uia = $txt -match 'tokenUIAccess=1'
    $pkg = [regex]::Match($txt, 'GetCurrentPackageFullName rc=0 name=(\S+)').Groups[1].Value
    $allowed = $txt -match 'RequestAccessAsync returned Allowed'
    [void](Complete-Check -Id 'N.1' -Description 'Installed NotifySpike: tokenUIAccess=1, package identity, access Allowed' `
        -Measured @{ processUp = $up; tokenUIAccess1 = $uia; package = $pkg; accessAllowed = $allowed; launchedAs = $script:LastSpikeMode; log = @($txt.Trim() -split "`r?`n" | Select-Object -First 8) } `
        -MeasuredPass ($up -and $uia -and $allowed -and [bool]$pkg))
    if ($listening) {
        $toastText = "NothingShell spike test $(Get-Date -Format HH:mm:ss) auto"
        $off = Get-LogLength $nsLog
        $toastLog1 = Join-Path $env:TEMP "nsho-toast1-$Stamp.txt"; $script:TempFiles.Add($toastLog1)
        $sent = Invoke-Unelevated ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{0}" -Text "{1}"' -f $toastPs, $toastText) $toastLog1 -Wait -TimeoutSec 30
        $m1 = (Wait-Until { (Get-LogSince $nsLog $off) -match 'EXPECT MATCH' } 15000 250) -ge 0
        $mis = (Get-LogSince $nsLog $off) -match 'EXPECT MISMATCH'
        [void](Complete-Check -Id 'N.2' -Description 'Self-sent test toast is received by the listener (EXPECT MATCH)' `
            -Measured @{ toastSent = $sent; expectMatch = $m1; expectMismatch = $mis; newLog = @((Get-LogSince $nsLog $off).Trim() -split "`r?`n" | Select-Object -Last 6) } -MeasuredPass $m1)
    } else {
        [void](Complete-Check -Id 'N.2' -Description 'Self-sent test toast is received by the listener (EXPECT MATCH)' -Measured @{ listening = $false } -MeasuredPass $false -Notes 'Spike did not start listening.')
    }
    Stop-OursByName 'NotifySpike'
    Start-Sleep -Milliseconds 800

    # Banners-off check
    Write-Host ''
    Write-Do 'Next: switching notification BANNERS off for "Windows PowerShell" (the test notifications come from it).'
    Write-Info 'I will open the Notifications settings page. Find "Windows PowerShell" in the list, click it,'
    Write-Info 'and turn OFF "Show notification banners". Then come back here.'
    try { Start-Process 'ms-settings:notifications' } catch { Write-Info 'Open Settings > System > Notifications by hand.' }
    $script:BannersOff = $true
    Wait-Enter 'Press Enter once banners are OFF for Windows PowerShell'
    $nsLog2 = Join-Path $env:TEMP "nsho-notify2-$Stamp.txt"
    $up2 = Start-Spike 'NotifySpike' $nsExe '--seconds 60 --expect "NothingShell spike test"' $nsLog2
    $listening2 = (Wait-Until { (Read-SharedText $nsLog2) -match 'Listening' } 20000 250) -ge 0
    $m2 = $false; $sent2 = $false
    if ($listening2) {
        $toastText = "NothingShell spike test $(Get-Date -Format HH:mm:ss) banners-off"
        $off = Get-LogLength $nsLog2
        $toastLog2 = Join-Path $env:TEMP "nsho-toast2-$Stamp.txt"; $script:TempFiles.Add($toastLog2)
        $sent2 = Invoke-Unelevated ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{0}" -Text "{1}"' -f $toastPs, $toastText) $toastLog2 -Wait -TimeoutSec 30
        $m2 = (Wait-Until { (Get-LogSince $nsLog2 $off) -match 'EXPECT MATCH' } 15000 250) -ge 0
    }
    $ab = Ask-YN 'Did a banner pop up on screen for that test notification?   (n is expected: banners are off)'
    [void](Complete-Check -Id 'N.3' -Description 'With banners OFF, the listener still receives the toast (EXPECT MATCH)' `
        -Measured @{ listening = $listening2; toastSent = $sent2; expectMatch = $m2; bannerSeenByUser = $ab } -MeasuredPass $m2 -Answer $ab -Expect 'n' -AnswerRequired $false)
    Stop-OursByName 'NotifySpike'
    Write-Do 'Please turn "Show notification banners" back ON for Windows PowerShell now.'
    Wait-Enter 'Press Enter once banners are back ON'
    $script:BannersOff = $false
    # Remove our test toasts
    $nsLog3 = Join-Path $env:TEMP "nsho-notify3-$Stamp.txt"; $script:TempFiles.Add($nsLog3)
    $cleanOk = Invoke-Unelevated ('"{0}" --cleanup' -f $nsExe) $nsLog3 -Wait -TimeoutSec 40
    $cl = Read-SharedText $nsLog3
    [void](Complete-Check -Id 'N.4' -Description 'Test toasts cleaned up (--cleanup)' -Measured @{ ran = $cleanOk; log = @($cl.Trim() -split "`r?`n" | Select-Object -Last 3) } -MeasuredPass ($cleanOk -and $cl -match 'cleanup: removed'))
}
catch {
    $fatal = $_
    Write-Host ''
    Write-Host "  STOPPED because of an error: $($_.Exception.Message)" -ForegroundColor Red
    [void](Complete-Check -Id 'ERR' -Description 'Script stopped early' -Measured @{ error = "$($_.Exception.Message)"; at = "$($_.InvocationInfo.PositionMessage)" } -Notes 'See the log file.' -Info)
}
finally {
    # ---------------------------------------------------------------- END
    try { Write-Step 'FINISHING: putting everything back' } catch { }
    try { Invoke-Cleanup } catch { Write-Host "  cleanup error: $($_.Exception.Message)" -ForegroundColor Red }
    try {
        Start-Sleep -Milliseconds 800
        $endAh = [NsWin]::GetAutoHide(); $endTray = [NsWin]::TrayState()
        $same = ($endAh -eq $script:OrigAutoHide) -and ($endTray -eq $script:OrigTray)
        Write-Host ("  Start: auto-hide={0}, taskbar visible={1}" -f $script:OrigAutoHide, ($script:OrigTray -eq 1))
        Write-Host ("  End:   auto-hide={0}, taskbar visible={1}" -f $endAh, ($endTray -eq 1))
        $leftoverProcs = @(foreach ($n in ($SpikeNames + 'NothingShell' + 'NothingShell.Watchdog')) {
            Get-Process -Name $n -ErrorAction SilentlyContinue | Where-Object { @($script:PreexistingIds[$n]) -notcontains $_.Id } | ForEach-Object { $_.ProcessName }
        })
        [void](Complete-Check -Id 'END' -Description 'Final state equals starting state; no test processes left' `
            -Measured @{ startAutoHide = $script:OrigAutoHide; endAutoHide = $endAh; startTrayState = $script:OrigTray; endTrayState = $endTray; leftoverProcesses = $leftoverProcs; stateFilePresent = (Test-Path -LiteralPath $StateFile) } `
            -MeasuredPass ($same -and $leftoverProcs.Count -eq 0))
    } catch { Write-Host "  final check error: $($_.Exception.Message)" -ForegroundColor Red }

    Write-Host ''
    Write-Host '  SUMMARY' -ForegroundColor Cyan
    try {
        $script:Results | ForEach-Object { [pscustomobject]@{ Id = $_.id; Verdict = $_.verdict; Check = $_.description } } |
            Format-Table -AutoSize -Wrap | Out-String -Width 200 | Write-Host
        $counts = $script:Results | Group-Object { $_.verdict } | ForEach-Object { "$($_.Name)=$($_.Count)" }
        Write-Host ("  Totals: " + ($counts -join '  '))
    } catch { }
    Write-Host ''
    Write-Good '  You can start Fenn again now.'
    Write-Host "  Log file:     $LogPath"
    Write-Host "  Results file: $($script:ResultsPath)"
    Write-Host '  The app and test programs stay installed. Please tell Claude that the run finished.'
    try { Stop-Transcript | Out-Null } catch { }
    try { Unregister-Event -SourceIdentifier PowerShell.Exiting -ErrorAction SilentlyContinue } catch { }
}
if ($fatal) { exit 1 }
