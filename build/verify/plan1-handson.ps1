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
param(
    # Run only these sections. Default (no -Only): reinstall + Dock, Apps, Tray, Reserve, Startup.
    # Example: -Only KeySpike,Notify
    [ValidateSet('Task8', 'KeySpike', 'Overflow', 'Desktop', 'Notify', 'Dock', 'Apps', 'Tray', 'Reserve', 'Launcher', 'Notch', 'Panel', 'Dash', 'Widgets', 'Calendar', 'Plan9', 'Startup')]
    [string[]]$Only,
    # With -Only the reinstall step is skipped unless -Reinstall is given.
    [switch]$Reinstall
)
$ErrorActionPreference = 'Stop'
$Sections = if ($Only) { @($Only) } else { @('Widgets', 'Dash', 'Notch', 'Panel', 'Launcher', 'Dock', 'Reserve', 'Startup') }

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
$script:LastLaunchError = ''
$script:OrigWorkArea   = $null
$script:KeepRunning    = $false
$script:KeepChoice     = $null
$script:LastSpikeElevated = -1
$script:foundProc      = $null
$script:VerifyTasks    = [System.Collections.Generic.List[string]]::new()
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
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr p, uint access, out IntPtr tok);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(IntPtr tok, int cls, out uint info, int len, out int ret);

    // 1 = elevated (high integrity token), 0 = not elevated, -1 = could not be read
    public static int IsElevated(uint pid)
    {
        IntPtr p = OpenProcess(0x1000, false, pid);
        if (p == IntPtr.Zero) return -1;
        try
        {
            IntPtr tok;
            if (!OpenProcessToken(p, 0x8, out tok)) return -1;
            try { uint v; int n; if (!GetTokenInformation(tok, 20, out v, 4, out n)) return -1; return v != 0 ? 1 : 0; }
            finally { CloseHandle(tok); }
        }
        finally { CloseHandle(p); }
    }

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

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] static extern bool SystemParametersInfoRect(uint a, uint b, out RECT r, uint f);

    // SPI_GETWORKAREA of the primary monitor: left, top, right, bottom
    public static int[] WorkArea()
    {
        RECT r; if (!SystemParametersInfoRect(0x0030, 0, out r, 0)) return null;
        return new[] { r.L, r.T, r.R, r.B };
    }

    // WM_CLOSE to EVERY top-level window of the process, hidden ones included. Returns how many were posted.
    public static int CloseAllWindows(uint pid)
    {
        var list = new List<IntPtr>();
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid) list.Add(h); return true; }, IntPtr.Zero);
        foreach (var h in list) PostMessageW(h, 0x10, IntPtr.Zero, IntPtr.Zero);
        return list.Count;
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
function Test-Section([string]$Name) { $Sections -contains $Name }

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

function Remove-VerifyTask([string]$Name) {
    try { Unregister-ScheduledTask -TaskName $Name -Confirm:$false -ErrorAction SilentlyContinue } catch { }
}

# Runs a command as the interactive user at NORMAL (limited, medium integrity) level through a one-shot
# scheduled task. Output goes to $LogFile when given. No elevated fallback: returns $false and sets
# $script:LastLaunchError on failure.
function Invoke-Unelevated([string]$CommandLine, [string]$LogFile, [switch]$Wait, [int]$TimeoutSec = 60, [string]$WorkDir = '') {
    $id = [guid]::NewGuid().ToString('N').Substring(0, 8)
    $cmd = Join-Path $env:TEMP "nsho-$id.cmd"
    $done = Join-Path $env:TEMP "nsho-$id.done"
    $taskName = "NothingShellVerify_$id"
    $script:TempFiles.Add($cmd); $script:TempFiles.Add($done)
    $run = if ($LogFile) { "$CommandLine > `"$LogFile`" 2>&1" } else { $CommandLine }
    $lines = @('@echo off')
    if ($WorkDir) { $lines += "cd /d `"$WorkDir`"" }
    $lines += $run
    $lines += "echo done> `"$done`""
    Set-Content -Path $cmd -Value $lines -Encoding ASCII
    try {
        $user = (Get-CimInstance Win32_ComputerSystem).UserName
        if (-not $user) { $user = "$env:USERDOMAIN\$env:USERNAME" }
        $action    = New-ScheduledTaskAction -Execute $cmd
        $trigger   = New-ScheduledTaskTrigger -Once -At ((Get-Date).Date.AddHours(23).AddMinutes(59))
        $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
        $settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -StartWhenAvailable
        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
        $script:VerifyTasks.Add($taskName)
        Start-ScheduledTask -TaskName $taskName
    } catch {
        $script:LastLaunchError = "scheduled task launch failed: $($_.Exception.Message)"
        Write-Bad $script:LastLaunchError
        Remove-VerifyTask $taskName
        return $false
    }
    if ($Wait) {
        $ms = Wait-Until { Test-Path -LiteralPath $done } ($TimeoutSec * 1000) 200
        Remove-VerifyTask $taskName
        if ($ms -lt 0) { $script:LastLaunchError = "command did not finish within $TimeoutSec s" }
        return ($ms -ge 0)
    }
    return $true
}

# Starts a long-running program at normal user level and returns its Process (newest, not seen before), or $null.
function Start-LimitedProcess([string]$Name, [string]$CommandLine, [string]$LogFile, [string]$WorkDir = '') {
    $pre = @(Get-Process -Name $Name -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
    $t0 = (Get-Date).AddSeconds(-2)
    $script:LastLaunchError = ''
    if (-not (Invoke-Unelevated $CommandLine $LogFile -WorkDir $WorkDir)) { return $null }
    $ms = Wait-Until {
        $c = @(Get-Process -Name $Name -ErrorAction SilentlyContinue | Where-Object { $pre -notcontains $_.Id -and $_.StartTime -ge $t0 } | Sort-Object StartTime -Descending)
        if ($c.Count -gt 0) { $script:foundProc = $c[0]; $true } else { $false }
    } 12000 200
    if ($ms -lt 0) { $script:LastLaunchError = "process '$Name' did not appear within 12 s of starting the task"; return $null }
    return $script:foundProc
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
    if ($script:KeepRunning) {
        Write-Info 'Leaving Nothing Shell running (your choice).'
    } else {
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
    }
    foreach ($t in $script:VerifyTasks) { Remove-VerifyTask $t }
    foreach ($f in $script:TempFiles) { try { Remove-Item -LiteralPath $f -Force -ErrorAction SilentlyContinue } catch { } }
    if ($script:BannersOff) {
        Write-Host ''
        Write-Host '  !! REMINDER: please turn "Show notification banners" back ON for Windows PowerShell' -ForegroundColor Yellow
        Write-Host '     (Settings > System > Notifications > Windows PowerShell).' -ForegroundColor Yellow
    }
}

# ---------- app / window helpers ----------
function Start-App {
    # install.ps1 now launches the app once at the end (to register the Run entry), and a copy may also be left
    # from an earlier step: quit any running copy gracefully (WM_CLOSE, never kill) before starting a fresh one.
    foreach ($old in @(Get-AppProcess)) {
        Write-Info ("  A Nothing Shell copy is already running (pid {0}); closing it first." -f $old.Id)
        [void](Quit-App $old)
    }
    if ((Get-AppProcess).Count -gt 0) { throw 'NothingShell is still running after asking it to close; aborting.' }
    $p = Start-LimitedProcess 'NothingShell' ('"{0}"' -f $AppExe) '' $AppDir
    if (-not $p) {
        [void](Complete-Check -Id 'LAUNCH.app' -Description 'Start NothingShell.exe as the normal (non-elevated) user' -Measured @{ error = $script:LastLaunchError } -MeasuredPass $false -Notes $script:LastLaunchError)
        throw "Could not start the app de-elevated: $($script:LastLaunchError)"
    }
    $script:StartedProcs.Add($p)
    Start-Sleep -Seconds 3
    $p.Refresh()
    return $p
}

# Quits the app cleanly: first `NothingShell.exe --quit` (the app's own exit path, Plan 2), and only if that is
# unavailable or does not work, WM_CLOSE to EVERY top-level window of its process (the dock window is hidden, so
# a title lookup or MainWindow does not work). Never kills. Then asserts the work area equals the starting value
# (a leaked appbar reservation would leave a dead strip). -NoWait: only post WM_CLOSE (for timing tests).
function Quit-App($p, [switch]$NoWait) {
    $closed = 0
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $exited = $false
    $via = 'wm_close'
    if (-not $NoWait -and (Test-Path -LiteralPath $AppExe)) {
        try {
            $q = Start-Process -FilePath $AppExe -ArgumentList '--quit' -PassThru -WindowStyle Hidden
            [void]$q.WaitForExit(15000)
            $exited = $p.WaitForExit(500)
            if ($exited) { $via = 'quit_command' }
        } catch { }
    }
    if (-not $exited) {
        try { $closed = [NsWin]::CloseAllWindows([uint32]$p.Id) } catch { }
        if ($NoWait) { return @{ windowsClosed = $closed } }
        try { $exited = $p.WaitForExit(5000) } catch { $exited = $true }
    }
    $exitMs = [int]$sw.ElapsedMilliseconds
    if (-not $exited) { Write-Bad "  The app did not exit within 5 s after WM_CLOSE (not killing it)." }
    $trayMs = Wait-Until { [NsWin]::TrayState() -eq 1 } 4000 100
    $wa = [NsWin]::WorkArea()
    $waOk = $false
    if ($script:OrigWorkArea) {
        [void](Wait-Until { (([NsWin]::WorkArea()) -join ',') -eq ($script:OrigWorkArea -join ',') } 4000 200)
        $wa = [NsWin]::WorkArea()
        $waOk = (($wa -join ',') -eq ($script:OrigWorkArea -join ','))
        if (-not $waOk) { Write-Bad ("  WORK AREA LEAK: now {0}, expected {1}" -f ($wa -join ','), ($script:OrigWorkArea -join ',')) }
    }
    return @{ windowsClosed = $closed; exited = $exited; exitMs = $exitMs; via = $via; trayVisible = ($trayMs -ge 0); trayBackMs = $trayMs
              workAreaOk = $waOk; workArea = ($wa -join ','); startWorkArea = ($script:OrigWorkArea -join ',') }
}

function Complete-Quit([string]$Id, $app, [string]$What = 'app') {
    $q = Quit-App $app
    [void](Complete-Check -Id $Id -Description "Quit ($What): exits on WM_CLOSE, taskbar back, work area back to the start value" -Measured $q `
        -MeasuredPass ($q.exited -and $q.trayVisible -and $q.workAreaOk))
    [void](Ensure-TaskbarBack "after $Id")
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
# uiAccess programs are started by the AppInfo service and do NOT inherit redirected stdout, so
# KeySpike/NotifySpike write their own log files; pass -OwnLog and read $LogFile (their own log).
function Start-Spike([string]$Name, [string]$Exe, [string]$ArgString, [string]$LogFile, [switch]$OwnLog) {
    Stop-OursByName $Name
    $redir = $LogFile
    if ($OwnLog) { $redir = "$LogFile.stdout"; try { Remove-Item -LiteralPath $LogFile -Force -ErrorAction SilentlyContinue } catch { } }
    else { Set-Content -Path $LogFile -Value '' -Encoding ASCII }
    $script:TempFiles.Add($LogFile); $script:TempFiles.Add($redir)
    $p = Start-LimitedProcess $Name ('"{0}" {1}' -f $Exe, $ArgString) $redir
    if (-not $p) {
        $script:LastSpikeMode = "FAILED: $($script:LastLaunchError)"
        $script:LastSpikeElevated = -1
        [void](Complete-Check -Id "LAUNCH.$Name" -Description "Start $Name as the normal (non-elevated) user" -Measured @{ error = $script:LastLaunchError } -MeasuredPass $false -Notes $script:LastLaunchError)
        return $false
    }
    $script:LastSpikeElevated = [NsWin]::IsElevated([uint32]$p.Id)
    $script:LastSpikeMode = if ($script:LastSpikeElevated -eq 0) { 'normal user (limited scheduled task)' } else { "UNEXPECTED elevation=$($script:LastSpikeElevated)" }
    return $true
}

function Ask-Check {
    param([string]$Id, [string]$Description, [string]$Todo, [string]$Question, [string]$Expect = 'y',
          $Measured = @{}, $MeasuredPass = $null, [bool]$AnswerRequired = $true, [string]$Notes = '')
    if ($Todo) { Write-Do $Todo }
    $a = Ask-YN $Question
    [void](Complete-Check -Id $Id -Description $Description -Measured $Measured -MeasuredPass $MeasuredPass -Answer $a -Expect $Expect -AnswerRequired $AnswerRequired -Notes $Notes)
    return $a
}

function Read-DockSettings {
    $f = Join-Path $StateDir 'settings.json'
    if (-not (Test-Path -LiteralPath $f)) { return $null }
    try { return (Get-Content -LiteralPath $f -Raw | ConvertFrom-Json) } catch { return $null }
}
function Get-Prop($o, [string]$Name) {
    if ($null -eq $o) { return $null }
    $pr = $o.PSObject.Properties | Where-Object { $_.Name -ieq $Name } | Select-Object -First 1
    if ($pr) { return $pr.Value }
    return $null
}
function Get-PinnedList {
    $pins = Get-Prop (Read-DockSettings) 'PinnedApps'
    @($pins | Where-Object { $_ } | ForEach-Object { '{0} | {1}' -f (Get-Prop $_ 'DisplayName'), (Get-Prop $_ 'AppId') })
}
function Set-SettingBool([string]$Name, [bool]$Value) {
    $f = Join-Path $StateDir 'settings.json'
    if (-not (Test-Path -LiteralPath $f)) { return $false }
    $t = Get-Content -LiteralPath $f -Raw
    $v = if ($Value) { 'true' } else { 'false' }
    $t2 = [regex]::Replace($t, ('("{0}"\s*:\s*)(true|false)' -f $Name), ('${1}' + $v), 'IgnoreCase')
    if ($t2 -eq $t) { return $false }
    Set-Content -LiteralPath $f -Value $t2 -Encoding UTF8
    return $true
}

# Opens windows as the normal user and returns the NEW visible top-level windows of that class.
function Open-LimitedWindows([string]$Class, [string[]]$CommandLines, [int]$TimeoutMs = 12000) {
    $before = @([NsWin]::VisibleByClass($Class))
    foreach ($c in $CommandLines) { [void](Invoke-Unelevated $c ''); Start-Sleep -Milliseconds 700 }
    $want = $CommandLines.Count
    [void](Wait-Until { @([NsWin]::VisibleByClass($Class) | Where-Object { $before -notcontains $_ }).Count -ge $want } $TimeoutMs 250)
    return @([NsWin]::VisibleByClass($Class) | Where-Object { $before -notcontains $_ })
}
function Close-Hwnds($hwnds) { foreach ($h in @($hwnds)) { try { [NsWin]::CloseWindow([IntPtr]$h) } catch { } } }

function Get-DockTimeText([int]$ProcId) {
    try {
        Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes -ErrorAction Stop
        $ae = [System.Windows.Automation.AutomationElement]
        $cond = [System.Windows.Automation.PropertyCondition]::new($ae::ProcessIdProperty, $ProcId)
        foreach ($w in $ae::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
            foreach ($el in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
                $n = $el.Current.Name
                if ($n -match '^\d{1,2}:\d{2}(\s?[AaPp][Mm])?$') { return $n }
            }
        }
    } catch { }
    return $null
}

function Get-RunValue { try { (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'NothingShell' -ErrorAction Stop).NothingShell } catch { $null } }
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
    Record-Preexisting
    try { Get-ScheduledTask -TaskName 'NothingShellVerify_*' -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false -ErrorAction SilentlyContinue } catch { }   # leftovers of earlier runs
    $doInstall = (-not $Only) -or $Reinstall
    if ($Only) { Write-Info ("Running only: {0}" -f ($Only -join ', ')) }
    $pwsh = (Get-Process -Id $PID).Path
    if ($doInstall) {
    Write-Step 'SETUP: installing the latest app and test programs'
    $installOk = $false
    # Security review 2026-11-21: building and signing run WITHOUT admin rights (build.ps1, started de-elevated),
    # only the install itself runs here, elevated.
    $built = $false
    foreach ($noTs in $false, $true) {
        $extra = if ($noTs) { ' -NoTimestamp' } else { '' }
        $buildLog = Join-Path $env:TEMP ("nsho-build-{0}.log" -f [guid]::NewGuid().ToString('N').Substring(0, 8))
        $script:TempFiles.Add($buildLog)
        Write-Info ("Running build\build.ps1{0} (not elevated)" -f $extra)
        [void](Invoke-Unelevated ("`"{0}`" -NoProfile -ExecutionPolicy Bypass -File `"{1}`"{2}" -f $pwsh, (Join-Path $Repo 'build\build.ps1'), $extra) $buildLog -Wait -TimeoutSec 900)
        if (Test-Path $buildLog) { Get-Content $buildLog | Select-Object -Last 5 | ForEach-Object { Write-Host "    $_" } }
        if (Test-Path (Join-Path $Repo 'out\NothingShell.msix')) { $built = $true; break }
        Write-Bad 'build.ps1 did not produce out\NothingShell.msix.'
        if (-not $noTs) { Write-Info 'Retrying once without the online timestamp...' }
    }
    if ($built) {
        Write-Info 'Running build\install.ps1'
        & $pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Repo 'build\install.ps1') 2>&1 | ForEach-Object { Write-Host "    $_" }
        $installOk = $LASTEXITCODE -eq 0
        if (-not $installOk) { Write-Bad "install.ps1 failed (exit $LASTEXITCODE)." }
    }
    if (-not $installOk) { throw 'build\build.ps1 or build\install.ps1 failed. Nothing else was changed.' }
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
    } else {
        Write-Info 'Skipping the reinstall (use -Reinstall to force it).'
        if ((Test-Section 'Task8') -or (Test-Section 'Overflow') -or (Test-Section 'Dock') -or (Test-Section 'Apps') -or (Test-Section 'Tray') -or (Test-Section 'Reserve') -or (Test-Section 'Launcher') -or (Test-Section 'Notch') -or (Test-Section 'Panel') -or (Test-Section 'Dash') -or (Test-Section 'Widgets') -or (Test-Section 'Startup')) {
            if (-not (Test-Path $AppExe) -or -not (Test-Path $Watchdog)) { throw 'The app is not installed. Run again with -Reinstall.' }
        }
    }
    # KeySpike now mirrors its console to %TEMP%\KeySpike.log; make sure the installed copy is not older than its source.
    if (Test-Section 'KeySpike') {
        $ksInstalled = Join-Path $SpikeRoot 'KeySpike\KeySpike.exe'
        $ksSource = Join-Path $Repo 'spikes\KeySpike\Program.cs'
        if (-not (Test-Path $ksInstalled) -or ((Get-Item $ksInstalled).LastWriteTime -lt (Get-Item $ksSource).LastWriteTime)) {
            Write-Info 'Updating the installed KeySpike...'
            & $pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Repo 'spikes\install-spikes.ps1') -Only KeySpike -NoTimestamp 2>&1 | ForEach-Object { Write-Host "    $_" }
            if ($LASTEXITCODE -ne 0) { throw 'Could not install KeySpike.' }
            Record-Preexisting
        }
    }
    if ((Test-Section 'Notify') -and -not (Test-Path (Join-Path $SpikeRoot 'NotifySpike\NotifySpike.exe'))) { throw 'NotifySpike is not installed. Run again with -Reinstall.' }

    # ---------------------------------------------------------------- Fenn
    Write-Step 'STEP 1: close Fenn'
    $fenn = @(Get-Process -Name 'fenn' -ErrorAction SilentlyContinue)
    $fennSkipped = $false
    $needFennGone = (Test-Section 'Task8') -or (Test-Section 'Overflow') -or (Test-Section 'Dock') -or (Test-Section 'Apps') -or (Test-Section 'Tray') -or (Test-Section 'Reserve') -or (Test-Section 'Launcher') -or (Test-Section 'Notch') -or (Test-Section 'Panel') -or (Test-Section 'Dash') -or (Test-Section 'Widgets')
    if ($fenn.Count -gt 0 -and $needFennGone) {
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

    # A running Nothing Shell (e.g. the installed copy, maybe with "Keep taskbar visible" on) would make its own
    # reserved strip part of the "starting" work area: close it first so the baseline is the plain Windows one.
    foreach ($old in @(Get-AppProcess)) {
        Write-Info ("A Nothing Shell copy is running (pid {0}); closing it before measuring the starting point." -f $old.Id)
        [void](Quit-App $old)
    }
    # A leftover state file from an earlier run would change the starting point: give the taskbar back first.
    $leftover = Read-StateFile
    if ($null -ne $leftover) {
        Write-Info "An old taskbar-restore.json was found ($($leftover | ConvertTo-Json -Compress)); restoring from it first."
        [void](Invoke-RestoreNow)
        Start-Sleep -Seconds 1
    }
    $script:OrigAutoHide = [NsWin]::GetAutoHide()
    $script:OrigTray     = [NsWin]::TrayState()
    $script:OrigWorkArea = [NsWin]::WorkArea()
    $mon = [NsWin]::PrimaryMonitor()
    $startInfo = @{
        autoHide = $script:OrigAutoHide; trayState = $script:OrigTray
        fennRunning = (@(Get-Process -Name fenn -ErrorAction SilentlyContinue).Count -gt 0)
        workArea = ($script:OrigWorkArea -join ','); fennSkipped = $fennSkipped; leftoverStateFileFound = ($null -ne $leftover)
        primaryMonitor = if ($mon) { "$($mon[2] - $mon[0])x$($mon[3] - $mon[1])" } else { $null }
    }
    Write-Info ("Starting point: taskbar auto-hide = {0}, taskbar visible = {1}" -f $script:OrigAutoHide, ($script:OrigTray -eq 1))
    $s0Notes = if ($fennSkipped) { 'Fenn was still running (user skipped); later results may be unreliable.' } elseif ($script:OrigTray -ne 1) { 'Explorer taskbar was NOT visible at the start.' } else { '' }
    [void](Complete-Check -Id 'S0' -Description 'Starting state (Fenn closed): auto-hide and explorer taskbar visibility' -Measured $startInfo -Info -Notes $s0Notes)
    if ($script:OrigTray -eq -1) { throw 'No Windows taskbar (explorer) found at the start; cannot continue.' }

    if (Test-Section 'Task8') {
    # ================================================================ TASK 8 CHECKS
    # ---------------------------------------------------------------- C8.1
    Write-Step 'CHECK 1: start the app - the taskbar should disappear'
    $logOff = Get-LogLength $AppLog
    $app = Start-App
    $trayState = [NsWin]::TrayState(); $ah = [NsWin]::GetAutoHide()
    $wdInfo = Test-WatchdogForApp $app
    $elev = [NsWin]::IsElevated([uint32]$app.Id)
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
    $pass = ($trayState -eq 0) -and $ah -and $wdInfo.found -and $wdInfo.parentStartMatches -and $uiOk -and $stateOk -and ($fills -eq $true) -and ($elev -eq 0)
    [void](Complete-Check -Id 'C8.1' -Description 'App start (normal user, not elevated): taskbar hidden, auto-hide on, watchdog (--parent-start matches), uiAccess=1, maximized window fills screen' `
        -Measured @{ trayState = $trayState; autoHide = $ah; appPid = $app.Id; appElevated = ($elev -eq 1); appElevationRead = ($elev -ge 0); watchdog = $wdInfo; uiAccessLog = $uiOk; stateFileOk = $stateOk; notepad = $npM } `
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
    Quit-App $app -NoWait
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
    Quit-App $app -NoWait
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
    Quit-App $app2 -NoWait
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
    }

    # ================================================================ SPIKES
    if (Test-Section 'KeySpike') {
    # ---------------------------------------------------------------- KeySpike
    Write-Step 'SPIKE 1: Windows key (KeySpike)'
    Write-Info 'A small test program (started as your normal user) will watch the Windows key. A black window may pop up: leave it alone.'
    Write-Info 'After each key test: close anything that opened (Esc), then click THIS window and answer.'
    $keyExe = Join-Path $SpikeRoot 'KeySpike\KeySpike.exe'
    $keyLog = Join-Path $env:TEMP 'KeySpike.log'
    if (-not (Test-Path $keyExe)) { throw "KeySpike not installed at $keyExe" }
    $up = Start-Spike 'KeySpike' $keyExe '' $keyLog -OwnLog
    $hookUp = (Wait-Until { (Read-SharedText $keyLog) -match 'hook installed' } 6000 200) -ge 0
    [void](Complete-Check -Id 'K.0' -Description 'KeySpike started and hook installed' -Measured @{ processUp = $up; hookInstalled = $hookUp; launchedAs = $script:LastSpikeMode } -MeasuredPass ($up -and $hookUp -and $script:LastSpikeElevated -eq 0))
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
    }

    if (Test-Section 'Overflow') {
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
    Quit-App $app -NoWait
    [void](Wait-Until { [NsWin]::TrayState() -eq 1 } 6000 100); [void]$app.WaitForExit(5000)
    [void](Ensure-TaskbarBack 'after overflow test')
    $ahNow = [NsWin]::GetAutoHide()
    [void](Complete-Check -Id 'O.5' -Description 'Taskbar returns to original state after the overflow test' -Measured @{ autoHideNow = $ahNow; autoHideOriginal = $script:OrigAutoHide; trayVisible = ([NsWin]::TrayState() -eq 1) } `
        -MeasuredPass (([NsWin]::TrayState() -eq 1) -and $ahNow -eq $script:OrigAutoHide))
    Stop-OursByName 'NothingShell.Watchdog'
    }

    if (Test-Section 'Desktop') {
    # ---------------------------------------------------------------- DesktopLayerSpike
    Write-Step 'SPIKE 3: desktop widget (DesktopLayerSpike)'
    $dlExe = Find-SpikeExe 'DesktopLayerSpike'
    $dlLog = Join-Path $env:TEMP "nsho-desktoplayer-$Stamp.txt"
    $up = Start-Spike 'DesktopLayerSpike' $dlExe '--option A --stay' $dlLog
    Start-Sleep -Seconds 3
    [void](Complete-Check -Id 'D.0' -Description 'DesktopLayerSpike started' -Measured @{ processUp = $up; launchedAs = $script:LastSpikeMode } -MeasuredPass ($up -and $script:LastSpikeElevated -eq 0))
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
    }

    if (Test-Section 'Notify') {
    # ---------------------------------------------------------------- NotifySpike
    Write-Step 'SPIKE 4: notifications (NotifySpike)'
    $nsExe = Join-Path $SpikeRoot 'NotifySpike\NotifySpike.exe'
    $toastPs = Join-Path $Repo 'spikes\NotifySpike\send-test-toast.ps1'
    if (-not (Test-Path $nsExe)) { throw "NotifySpike not installed at $nsExe" }
    $nsLog = Join-Path $env:TEMP 'NotifySpike.log'
    $up = Start-Spike 'NotifySpike' $nsExe '--seconds 90 --expect "NothingShell spike test"' $nsLog -OwnLog
    $listening = (Wait-Until { (Read-SharedText $nsLog) -match 'Listening' } 20000 250) -ge 0
    $txt = Read-SharedText $nsLog
    $uia = $txt -match 'tokenUIAccess=1'
    $pkg = [regex]::Match($txt, 'GetCurrentPackageFullName rc=0 name=(\S+)').Groups[1].Value
    $allowed = $txt -match 'RequestAccessAsync returned Allowed'
    [void](Complete-Check -Id 'N.1' -Description 'Installed NotifySpike: tokenUIAccess=1, package identity, access Allowed' `
        -Measured @{ processUp = $up; tokenUIAccess1 = $uia; package = $pkg; accessAllowed = $allowed; launchedAs = $script:LastSpikeMode; log = @($txt.Trim() -split "`r?`n" | Select-Object -First 8) } `
        -MeasuredPass ($up -and $uia -and $allowed -and [bool]$pkg -and $script:LastSpikeElevated -eq 0))
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
    $nsLog2 = Join-Path $env:TEMP 'NotifySpike.log'
    $up2 = Start-Spike 'NotifySpike' $nsExe '--seconds 60 --expect "NothingShell spike test"' $nsLog2 -OwnLog
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
    $nsLog3 = Join-Path $env:TEMP 'NotifySpike.log'; $script:TempFiles.Add("$nsLog3.stdout")
    $cleanOk = Invoke-Unelevated ('"{0}" --cleanup' -f $nsExe) "$nsLog3.stdout" -Wait -TimeoutSec 40
    $cl = Read-SharedText $nsLog3
    [void](Complete-Check -Id 'N.4' -Description 'Test toasts cleaned up (--cleanup)' -Measured @{ ran = $cleanOk; log = @($cl.Trim() -split "`r?`n" | Select-Object -Last 3) } -MeasuredPass ($cleanOk -and $cl -match 'cleanup: removed'))
    }


    # ================================================================ DOCK (tasks 11)
    if (Test-Section 'Dock') {
        Write-Step 'DOCK: reveal feel, look, full screen, idle CPU'
        $logOff = Get-LogLength $AppLog
        $app = Start-App
        $elev = [NsWin]::IsElevated([uint32]$app.Id)
        # Startup can take a few seconds (icons, tray, appbar): wait for the line instead of reading once.
        [void](Wait-Until { (Get-LogSince $AppLog $logOff) -match 'dock ready' } 15000 250)
        $logNew = Get-LogSince $AppLog $logOff
        $ready = $logNew -match 'dock ready'
        $mon = [NsWin]::PrimaryMonitor()
        $geo = @{}; $geoPass = $null
        $pm = [regex]::Match($logNew, 'dock placed:.*?pill=RectI \{ Left = (-?\d+), Top = (-?\d+), Right = (-?\d+), Bottom = (-?\d+) \}')
        if ($pm.Success -and $mon) {
            $pl = [int]$pm.Groups[1].Value; $pr = [int]$pm.Groups[3].Value; $pb = [int]$pm.Groups[4].Value
            $cx = (($pl + $pr) / 2) - (($mon[0] + $mon[2]) / 2); $gap = $mon[3] - $pb
            $geo = @{ pillLeft = $pl; pillRight = $pr; pillBottom = $pb; centerOffsetPx = $cx; gapAboveBottomPx = $gap }
            $geoPass = ([math]::Abs($cx) -le 3) -and ($gap -ge 8) -and ($gap -le 30)
        }
        [void](Complete-Check -Id 'DK.0' -Description 'Dock starts (normal user), pill centered, about 12 DIP above the bottom' `
            -Measured (@{ dockReady = $ready; elevated = ($elev -eq 1); workAreaWhileRunning = ((([NsWin]::WorkArea()) -join ',')) } + $geo) `
            -MeasuredPass ($ready -and ($elev -eq 0) -and ($geoPass -ne $false)))
        Write-Info 'The dock is hidden until you push the mouse to the bottom edge of the screen.'
        [void](Ask-Check 'DK.1' 'Pointer held at the bottom edge: dock slides up after a short pause' `
            'Push the mouse to the very bottom edge of the screen and hold it still for a second.' 'Did the dock slide up after a short pause?')
        [void](Ask-Check 'DK.2' 'Smooth slide animation' '' 'Did it slide smoothly (not jerky)?')
        [void](Ask-Check 'DK.3' 'Leaving the edge hides the dock after about half a second' `
            'Move the mouse up and away from the dock.' 'Did it hide about half a second later?')
        [void](Ask-Check 'DK.4' 'Resting on the pill keeps it up' `
            'Show the dock again, then rest the pointer ON the dock for 3 seconds.' 'Did it stay up the whole time?')
        [void](Ask-Check 'DK.5' 'A quick flick to the edge does not show the dock' `
            'Move to the middle of the screen, then flick the mouse quickly down to the bottom edge and straight back up. Do that 4 times.' `
            'Did the dock appear during the flicks? (a single flash is OK, answer n)' -Expect 'n')
        [void](Ask-Check 'DK.6' 'Dock looks centered with a small gap under it' 'Show the dock and look at it.' 'Is it centered, with a small gap between it and the screen bottom?')
        $fsOff = Get-LogLength $AppLog
        Write-Do 'FULL SCREEN: open a YouTube video in your browser (do not make it full screen yet).'
        Write-Do 'After you press Enter you get 20 seconds. In that time: click the browser, make the video full screen (or press F11), then push the mouse to the bottom edge 3 or 4 times and hold it there. Do NOT click this window during the 20 seconds.'
        Write-Do 'You will hear a beep when the 20 seconds are up. Then leave full screen and come back here.'
        Wait-Enter 'Press Enter to start the 20 seconds'
        Start-Sleep -Seconds 20
        try { [console]::Beep(880, 300) } catch { }
        $fsLog = Get-LogSince $AppLog $fsOff
        $a = Ask-YN 'While in full screen, did the dock stay hidden every time?'
        $fsLog = Get-LogSince $AppLog $fsOff
        [void](Complete-Check -Id 'DK.7' -Description 'Full screen: the bottom edge does nothing' `
            -Measured @{ fullscreenTrueLogged = ($fsLog -match 'fullscreen=True') } -MeasuredPass ($fsLog -match 'fullscreen=True') -Answer $a -Expect 'y' -AnswerRequired $true)
        Write-Do 'Make sure you are out of full screen, then push the mouse to the bottom edge again.'
        $a = Ask-YN 'Does the dock work again?'
        $fsLog2 = Get-LogSince $AppLog $fsOff
        [void](Complete-Check -Id 'DK.8' -Description 'After leaving full screen the edge works again' `
            -Measured @{ fullscreenFalseLogged = ($fsLog2 -match 'fullscreen=False') } -MeasuredPass ($fsLog2 -match 'fullscreen=False') -Answer $a -Expect 'y')
        Write-Do 'IDLE CPU TEST: move the mouse to the TOP of the screen and do not touch the mouse or keyboard for 30 seconds.'
        Wait-Enter 'Press Enter, then keep still'
        $p0 = Get-Process -Id $app.Id; $t0 = $p0.TotalProcessorTime
        1..30 | ForEach-Object { Start-Sleep -Seconds 1; if ($_ % 5 -eq 0) { Write-Host -NoNewline "$_ " } }
        Write-Host ''
        $p1 = Get-Process -Id $app.Id
        $cpuMs = [int]($p1.TotalProcessorTime - $t0).TotalMilliseconds
        [void](Complete-Check -Id 'DK.9' -Description 'Idle CPU about 0 over 30 s (mouse still)' `
            -Measured @{ cpuMsOver30s = $cpuMs; workingSetMB = [math]::Round($p1.WorkingSet64 / 1MB, 1); privateMB = [math]::Round($p1.PrivateMemorySize64 / 1MB, 1) } -MeasuredPass ($cpuMs -le 300) `
            -Notes 'Passes at 300 ms or less of CPU time in 30 s.')
        Complete-Quit 'DK.quit' $app 'dock section'
    }

    # ================================================================ APPS (tasks 12, 13)
    if (Test-Section 'Apps') {
        Write-Step 'APPS: pinned and running apps'
        $sf = Join-Path $StateDir 'settings.json'
        if (Test-Path -LiteralPath $sf) {
            Copy-Item -LiteralPath $sf -Destination "$sf.handson-bak-$Stamp" -Force
            Remove-Item -LiteralPath $sf -Force
            Write-Info "Started fresh: your old settings.json was copied to settings.json.handson-bak-$Stamp"
        }
        $app = Start-App
        Start-Sleep -Seconds 2
        $pins = @(Get-PinnedList)
        $hasChrome = [bool]($pins | Where-Object { $_ -match 'chrome' })
        Write-Info ('Pinned apps found: ' + (($pins | ForEach-Object { ($_ -split ' \| ')[0] }) -join ', '))
        [void](Ask-Check 'AP.1' 'First run shows your old taskbar pins, including Chrome' `
            'Show the dock (mouse to the bottom edge) and look at the icons.' 'Do you see Chrome (and your other pinned apps) in the dock, with no dots under them?' `
            -Measured @{ pinned = $pins; chromePinned = $hasChrome } -MeasuredPass ($pins.Count -ge 1) -Notes 'Chrome is only expected if it was pinned to the old taskbar.')
        [void](Ask-Check 'AP.2' 'Clicking a closed pinned app launches it' `
            'Make sure Chrome is closed. Click the Chrome icon in the dock.' 'Did Chrome open?')
        [void](Ask-Check 'AP.3' 'Clicking a running app that is not in front brings it to the front' `
            'Open Notepad or any other window on top of Chrome, then click Chrome in the dock.' 'Did Chrome come to the front?')
        [void](Ask-Check 'AP.4' 'Clicking the app that is already in front minimizes it' `
            'With Chrome in front, click the Chrome icon in the dock once more.' 'Did Chrome minimize?')
        [void](Ask-Check 'AP.5' 'The focused app shows a red dot, running apps a grey dot' `
            'Click Chrome again to bring it back, then look at its icon.' 'Is there a RED dot under the active app and a grey dot under other running apps?')

        Write-Info 'Opening 3 File Explorer windows and the Settings app for you...'
        $ex = Open-LimitedWindows 'CabinetWClass' @('explorer.exe "C:\Windows"', 'explorer.exe "C:\Users"', 'explorer.exe "C:\Program Files"')
        $se = Open-LimitedWindows 'ApplicationFrameWindow' @('start ms-settings:')
        Start-Sleep -Seconds 2
        [void](Ask-Check 'AP.6' 'Three Explorer windows = ONE dock item' 'Look at the dock.' 'Is there ONE File Explorer item (not three)?' `
            -Measured @{ explorerWindowsOpened = $ex.Count } -MeasuredPass ($ex.Count -ge 3))
        [void](Ask-Check 'AP.7' 'Explorer item shows a list of its windows; a row click switches window' `
            'Click (or hover) the File Explorer item, then click one row in the small list.' 'Did a list of 3 windows appear and did the row bring that window forward?')
        [void](Ask-Check 'AP.8' 'Settings = one item, no strange extra item' '' 'Is there ONE Settings item, with no extra odd item (like "ApplicationFrameHost")?' `
            -Measured @{ settingsWindowOpened = ($se.Count -ge 1) })
        Close-Hwnds $ex; Close-Hwnds $se
        Start-Sleep -Seconds 2

        $np = Open-LimitedWindows 'Notepad' @('start "" notepad.exe')
        [void](Ask-Check 'AP.9' 'Right-click a running app: menu with Pin / Close window(s)' `
            'Right-click the Notepad icon in the dock (show the dock first). Choose "Pin to dock".' 'Did a menu appear and did you pin Notepad?')
        Close-Hwnds $np; Start-Sleep -Seconds 2
        $pinsNow = @(Get-PinnedList); $notepadPinned = [bool]($pinsNow | Where-Object { $_ -match 'notepad' })
        [void](Ask-Check 'AP.10' 'Pinned Notepad stays in the dock after it closes' '' 'With Notepad closed, is a Notepad icon still in the dock?' `
            -Measured @{ pinnedAfterClose = $pinsNow } -MeasuredPass $notepadPinned)
        Complete-Quit 'AP.quit1' $app 'before restart'
        $app = Start-App
        Start-Sleep -Seconds 2
        $pinsRestart = @(Get-PinnedList)
        [void](Ask-Check 'AP.11' 'Pin survives restarting the app' 'Show the dock.' 'After the restart, is Notepad still in the dock?' `
            -Measured @{ pinnedAfterRestart = $pinsRestart } -MeasuredPass ([bool]($pinsRestart | Where-Object { $_ -match 'notepad' })))
        Write-Do 'Right-click the Notepad icon and choose "Unpin from dock".'
        $a = Ask-YN 'Did the Notepad icon disappear?'
        Start-Sleep -Milliseconds 800
        $pinsUnpinned = @(Get-PinnedList)
        [void](Complete-Check -Id 'AP.12' -Description 'Unpin removes it and saves' -Measured @{ pinnedAfterUnpin = $pinsUnpinned } `
            -MeasuredPass (-not [bool]($pinsUnpinned | Where-Object { $_ -match 'notepad' })) -Answer $a -Expect 'y')
        $np = Open-LimitedWindows 'Notepad' @('start "" notepad.exe')
        Write-Do 'Right-click the Notepad icon and choose "Close window(s)".'
        $a = Ask-YN 'Did Notepad close?'
        Start-Sleep -Seconds 1
        $left = @($np | Where-Object { [NsWin]::WindowExists([IntPtr]$_) })
        [void](Complete-Check -Id 'AP.13' -Description 'Close window(s) closes the app windows' -Measured @{ windowsStillOpen = $left.Count } -MeasuredPass ($left.Count -eq 0) -Answer $a -Expect 'y')
        Close-Hwnds $left
        Complete-Quit 'AP.quit2' $app 'apps section'
    }

    # ================================================================ TRAY (task 14)
    if (Test-Section 'Tray') {
        Write-Step 'TRAY: Wi-Fi, volume, battery, overflow arrow, clock'
        $wifi = @(Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object { $_.PhysicalMediaType -match '802\.11' })
        $battery = @(Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue)
        [void](Complete-Check -Id 'TR.0' -Description 'This PC: Wi-Fi adapter and battery present' -Measured @{
            wifiAdapters = @($wifi | ForEach-Object { "$($_.Name) $($_.Status)" }); batteries = $battery.Count } -Info)
        $logOff = Get-LogLength $AppLog
        $app = Start-App
        Start-Sleep -Seconds 2
        [void](Ask-Check 'TR.1' 'Right end of the dock shows: up-arrow, Wi-Fi, speaker, (battery), time' `
            'Show the dock and look at its right end.' 'Do you see an up-arrow, Wi-Fi, speaker, battery (if a laptop) and the time?')
        if ($wifi.Count -gt 0) {
            [void](Ask-Check 'TR.2' 'Turning Wi-Fi off updates the symbol within 2 s' `
                'Turn Wi-Fi OFF (Win+A, then the Wi-Fi button). Watch the dock.' 'Did the Wi-Fi symbol change (crossed out / dimmer) within about 2 seconds?')
            [void](Ask-Check 'TR.3' 'Turning Wi-Fi on updates the symbol' 'Turn Wi-Fi back ON.' 'Did the symbol go back to normal?')
        } else { [void](Complete-Check -Id 'TR.2' -Description 'Wi-Fi symbol follows Wi-Fi on/off' -Measured @{} -Answer 's' -Expect 'y' -Notes 'No Wi-Fi adapter on this PC.') }
        [void](Ask-Check 'TR.4' 'Volume change updates the speaker symbol' `
            'Change the volume (volume keys or the slider) between loud, quiet and zero.' 'Did the speaker symbol change within about 2 seconds?')
        [void](Ask-Check 'TR.5' 'Mute updates the speaker symbol' 'Mute, then unmute.' 'Did the speaker show a mute mark when muted, and go back when unmuted?')
        if ($battery.Count -gt 0) {
            [void](Ask-Check 'TR.6' 'Charger plug/unplug updates the battery symbol' `
                'Unplug the charger, wait a moment, then plug it back in.' 'Did a lightning bolt appear when charging and disappear when unplugged, each within about 2 seconds?')
        } else { [void](Complete-Check -Id 'TR.6' -Description 'Battery symbol follows the charger' -Measured @{} -Answer 's' -Expect 'y' -Notes 'No battery on this PC.') }
        $ovOff = Get-LogLength $AppLog
        [void](Ask-Check 'TR.7' 'Up-arrow opens Windows hidden-icons panel' `
            'Click the up-arrow (^) at the left of the tray area.' 'Did Windows hidden-icons panel open (with your Discord / Steam style icons)?')
        [void](Ask-Check 'TR.8' 'Right-click menus work on those icons' 'Right-click one icon in the panel.' 'Did its menu appear? (then press Esc twice)')
        $ovLog = Get-LogSince $AppLog $ovOff
        $ovLines = @($ovLog -split "`r?`n" | Where-Object { $_ -match 'overflow|FocusNotOnTray|Opened' } | Select-Object -Last 6)
        [void](Ask-Check 'TR.9' 'Taskbar flash when opening the panel is brief' '' 'Did you see the Windows taskbar flash when the panel opened? (n = no flash; a very short flash is fine)' -Expect 'n' `
            -Measured @{ overflowLogLines = $ovLines } -AnswerRequired $false -Notes 'Informational: a brief flash is known and acceptable.')
        [void](Ask-Check 'TR.10' 'Clicking the tray (Wi-Fi/speaker/battery) opens Windows Quick Settings' `
            'Click the Wi-Fi / speaker area of the dock (not the up-arrow).' 'Did Windows Quick Settings open? (press Esc to close it)')
        # Clock minute rollover
        $t0 = Get-DockTimeText $app.Id
        if ($t0) {
            Write-Info "Clock text read by the script: $t0. Waiting for the next minute..."
            $ch = Wait-Until { (Get-DockTimeText $app.Id) -ne $t0 } 75000 200
            $now = Get-Date; $msPastMinute = $now.Second * 1000 + $now.Millisecond
            [void](Complete-Check -Id 'TR.11' -Description 'Clock text changes within 2 s of the minute rollover' `
                -Measured @{ firstText = $t0; changedAfterMs = $ch; msPastMinuteWhenSeen = $msPastMinute } -MeasuredPass ($ch -ge 0 -and $msPastMinute -le 2500))
        } else {
            $secs = 60 - (Get-Date).Second
            Write-Info "The script cannot read the clock text. The next minute starts in about $secs seconds."
            Write-Do 'Show the dock and watch the time. Do not look away at the minute change.'
            Start-Sleep -Seconds ($secs + 2)
            $a = Ask-YN 'Did the time on the dock change at the minute (within about 2 seconds of it)?'
            [void](Complete-Check -Id 'TR.11' -Description 'Clock text changes within 2 s of the minute rollover' -Measured @{ readByScript = $false } -Answer $a -Expect 'y')
        }
        Complete-Quit 'TR.quit' $app 'tray section'
    }

    # ================================================================ RESERVE (task 15)
    if (Test-Section 'Reserve') {
        Write-Step 'RESERVE: keep the dock visible (slides down for maximized and full-screen apps)'
        $app = Start-App
        Start-Sleep -Seconds 2
        $waRun = [NsWin]::WorkArea(); $mon = [NsWin]::PrimaryMonitor()
        Write-Info ('Work area now (app running, taskbar hidden): ' + ($waRun -join ','))
        [void](Ask-Check 'RS.1' 'Right-click on the start button shows the menu' `
            'Show the dock and right-click the START button (the left-most one).' 'Does a menu appear with: Keep taskbar visible, Start with Windows, Quit Nothing Shell?')
        Write-Do 'Click "Keep taskbar visible" so it gets a tick.'
        $kvMs = Wait-Until { $w = [NsWin]::WorkArea(); $w -and ($w[3] -lt $waRun[3] - 40) } 20000 200
        $waKeep = [NsWin]::WorkArea()
        [void](Complete-Check -Id 'RS.2' -Description 'Keep visible ON: the work area shrinks (space reserved)' `
            -Measured @{ workAreaBefore = ($waRun -join ','); workAreaAfter = ($waKeep -join ','); shrinkSeenAfterMs = $kvMs } -MeasuredPass ($kvMs -ge 0))
        $np = Open-LimitedWindows 'Notepad' @('start "" notepad.exe')
        $fills = $null; $npM = @{ opened = ($np.Count -ge 1) }
        if ($np.Count -ge 1) {
            [NsWin]::Maximize($np[0]); Start-Sleep -Milliseconds 1200
            $fb = [NsWin]::FrameBounds($np[0])
            if ($fb -and $waKeep) { $npM.frame = ($fb -join ','); $npM.workArea = ($waKeep -join ','); $fills = ([math]::Abs($fb[3] - $waKeep[3]) -le 3) -and ($fb[3] -lt $mon[3] - 40) }
        }
        [void](Ask-Check 'RS.3' 'A maximized window gets the whole screen; the dock slides down' 'Look at the maximized Notepad, then touch the bottom edge, then restore Notepad to a normal window.' 'Did Notepad reach the bottom with the dock hidden, did the edge bring the dock back, and did the dock stay up once Notepad was a normal window?' `
            -Measured $npM -MeasuredPass $fills -AnswerRequired $false)
        Close-Hwnds $np
        $fsOff = Get-LogLength $AppLog
        Write-Do 'FULL SCREEN again. After you press Enter you get 15 seconds: click the browser, make the video full screen (or press F11) and watch the bottom of the screen. Do NOT click this window during the 15 seconds.'
        Write-Do 'After the beep, stay in full screen for a moment and remember whether the dock was hidden, then come back here (leave full screen afterwards).'
        Wait-Enter 'Press Enter to start the 15 seconds'
        Start-Sleep -Seconds 15
        try { [console]::Beep(880, 300) } catch { }
        $a = Ask-YN 'With "Keep taskbar visible" ON, was the dock hidden while you were in full screen?'
        $fsLog = Get-LogSince $AppLog $fsOff
        [void](Complete-Check -Id 'RS.4' -Description 'Full screen hides the dock even in keep-visible mode' -Measured @{ fullscreenTrueLogged = ($fsLog -match 'fullscreen=True') } `
            -MeasuredPass ($fsLog -match 'fullscreen=True') -Answer $a -Expect 'y')
        Write-Do 'Leave full screen. The dock should come back by itself.'
        [void](Ask-Check 'RS.5' 'Leaving full screen brings the kept dock back' '' 'Is the dock back?')
        Write-Do 'Next I restart Windows Explorer. The screen flickers for a few seconds; this is normal.'
        Wait-Enter 'Press Enter to restart Explorer'
        $waBefore = [NsWin]::WorkArea()
        $rsOff = Get-LogLength $AppLog
        Stop-Process -Name explorer -Force
        # Wait for the NEW Explorer: the app logs this once it has re-hidden the taskbar and re-reserved its strip.
        # (Reading right after the kill measured the dying Explorer's stale values.)
        $tcMs = Wait-Until { (Get-LogSince $AppLog $rsOff) -match 'TaskbarCreated received' } 20000 200
        if ($tcMs -lt 0) { Ensure-Explorer }
        $hideMs = Wait-Until { [NsWin]::HasTray() -and [NsWin]::TrayState() -eq 0 } 10000 100
        $reMs = Wait-Until { (([NsWin]::WorkArea()) -join ',') -eq ($waBefore -join ',') } 15000 200
        Start-Sleep -Seconds 2
        $waAfter = [NsWin]::WorkArea()
        $a = Ask-YN 'With no maximized window in front, is the dock still showing?'
        [void](Complete-Check -Id 'RS.6' -Description 'Explorer restart: reservation still there' `
            -Measured @{ workAreaBefore = ($waBefore -join ','); workAreaAfter = ($waAfter -join ','); taskbarCreatedLoggedMs = $tcMs; trayHiddenAgainMs = $hideMs; workAreaBackMs = $reMs } `
            -MeasuredPass (($waAfter -join ',') -eq ($waBefore -join ',') -and $hideMs -ge 0) -Answer $a -Expect 'y' -AnswerRequired $false)
        [void](Ask-Check 'RS.7' "Start button's last dot is red" 'Look closely at the start button (left-most).' 'Is the LAST dot of the start button red?')
        Write-Do 'Right-click the start button and click "Keep taskbar visible" again to turn it OFF.'
        $offMs = Wait-Until { (([NsWin]::WorkArea()) -join ',') -eq ($waRun -join ',') } 20000 200
        [void](Complete-Check -Id 'RS.8' -Description 'Keep visible OFF: work area back to the value before' `
            -Measured @{ expected = ($waRun -join ','); now = (([NsWin]::WorkArea()) -join ','); backAfterMs = $offMs } -MeasuredPass ($offMs -ge 0))
        Write-Do 'Now right-click the start button and choose "Quit Nothing Shell".'
        $q0 = [System.Diagnostics.Stopwatch]::StartNew()
        $exited = $app.WaitForExit(60000)
        $quitMs = [int]$q0.ElapsedMilliseconds
        $trayMs2 = Wait-Until { [NsWin]::TrayState() -eq 1 } 6000 100
        [void](Wait-Until { (([NsWin]::WorkArea()) -join ',') -eq ($script:OrigWorkArea -join ',') } 4000 200)
        $waEnd = [NsWin]::WorkArea()
        $wdGone = (Wait-Until { (Get-WatchdogInfo).Count -eq 0 } 4000 200) -ge 0
        $ans = Ask-YN 'Is the Windows taskbar back?'
        [void](Complete-Check -Id 'RS.9' -Description 'Quit from the menu: taskbar back, work area at start value, helper gone' `
            -Measured @{ exited = $exited; msAfterPromptToExit = $quitMs; trayBackMs = $trayMs2; workAreaNow = ($waEnd -join ','); workAreaStart = ($script:OrigWorkArea -join ','); watchdogGone = $wdGone; stateFileGone = (-not (Test-Path -LiteralPath $StateFile)) } `
            -MeasuredPass ($exited -and $trayMs2 -ge 0 -and (($waEnd -join ',') -eq ($script:OrigWorkArea -join ',')) -and $wdGone) -Answer $ans -Expect 'y' -AnswerRequired $false)
        if (-not $exited) { Complete-Quit 'RS.quit' $app 'fallback quit' }
        [void](Ensure-TaskbarBack 'after reserve section')
    }

    # ================================================================ LAUNCHER (plan 2)
    if (Test-Section 'Launcher') {
        Write-Step 'LAUNCHER: Win key, search, launch, All apps, power menu'
        $logOff = Get-LogLength $AppLog
        $app = Start-App
        [void](Wait-Until { (Get-LogSince $AppLog $logOff) -match 'dock ready' } 15000 250)
        [void](Wait-Until { (Get-LogSince $AppLog $logOff) -match 'app index: \d+ apps' } 20000 250)
        $idx = [regex]::Match((Get-LogSince $AppLog $logOff), 'app index: (\d+) apps, (\d+) with run history')
        $hookOk = (Get-LogSince $AppLog $logOff) -match 'win key hook installed'
        [void](Complete-Check -Id 'LA.0' -Description 'Win key hook installed; app index built' `
            -Measured @{ winHook = $hookOk; apps = $(if ($idx.Success) { [int]$idx.Groups[1].Value } else { 0 }); withHistory = $(if ($idx.Success) { [int]$idx.Groups[2].Value } else { 0 }) } `
            -MeasuredPass ($hookOk -and $idx.Success -and [int]$idx.Groups[1].Value -gt 20))

        $o = Get-LogLength $AppLog
        $a = Ask-YN 'Tap the Win key once (alone). Did the launcher open in the middle of the screen, and Windows Start did NOT open?'
        $l = Get-LogSince $AppLog $o
        [void](Complete-Check -Id 'LA.1' -Description 'Lone Win opens the launcher, not Start' -Measured @{ loneWinLogged = ($l -match 'lone win -> launcher') } `
            -MeasuredPass ($l -match 'lone win -> launcher') -Answer $a -Expect 'y')
        Write-Do 'Press Esc to close it.'

        $np = Start-Process notepad -PassThru; Start-Sleep -Seconds 2
        $o = Get-LogLength $AppLog
        Write-Do 'Click into the Notepad window, then tap Win and type  calc  WITHOUT clicking anything.'
        $a = Ask-YN 'Did "calc" appear in the launcher search box (and NOT in Notepad)?'
        $l = Get-LogSince $AppLog $o
        [void](Complete-Check -Id 'LA.2' -Description 'Typing after Win goes into the search box' -Measured @{ focusOkLogged = ($l -match 'launcher focus ok=True') } `
            -MeasuredPass ($l -match 'launcher focus ok=True') -Answer $a -Expect 'y')
        try { if (-not $np.HasExited) { $np.CloseMainWindow() | Out-Null } } catch { }

        $o = Get-LogLength $AppLog
        $a = Ask-YN 'With "calc" typed, is Calculator the Best match? Press Enter: did Calculator open and the launcher close?'
        $l = Get-LogSince $AppLog $o
        [void](Complete-Check -Id 'LA.3' -Description 'Enter launches the best match and closes the launcher' -Measured @{ launchLogged = ($l -match 'launch App ".*" ok=True') } `
            -MeasuredPass ($l -match 'launch App ".*" ok=True') -Answer $a -Expect 'y')
        Get-Process -Name CalculatorApp -ErrorAction SilentlyContinue | ForEach-Object { try { $_.CloseMainWindow() | Out-Null } catch { } }

        [void](Ask-Check 'LA.4' 'Settings results' 'Tap Win, type  display' 'Is "Display" shown with a SETTING label? Press Enter: did Settings open on Display?')
        [void](Ask-Check 'LA.5' 'File results from this PC' 'Tap Win and type part of a file name you know (a document or download).' 'Did that file appear in the list (FILE label) within about a second?')
        [void](Ask-Check 'LA.6' 'Esc, click outside and Win close the launcher' 'Open the launcher and close it three ways: Esc; clicking outside the panel; tapping Win again.' 'Did all three close it?')
        [void](Ask-Check 'LA.7' 'Other Win shortcuts unchanged' 'Try Win+E (Explorer), Win+D (desktop), Win+Shift+S (snip, then Esc), Win+Tab, Win+A and Win+N.' 'Did they all do their normal Windows thing (no launcher)?')
        [void](Ask-Check 'LA.8' 'Dock start and search buttons' 'Show the dock and click the start button (dots), close the launcher, then click the search button.' 'Did both open the launcher?')
        [void](Ask-Check 'LA.9' 'All apps A-Z' 'Open the launcher and click "ALL APPS ›". Scroll, use the arrow keys, then press Esc.' 'Was there an A-Z list of your apps, and did Esc go back to the start view?')
        [void](Ask-Check 'LA.10' 'Power menu shows (do NOT click a choice)' 'Click the round power button at the bottom right of the launcher. Do NOT click any choice; press Esc.' 'Did SLEEP, RESTART and SHUT DOWN appear?')
        [void](Ask-Check 'LA.2b' 'Typing after Win over an elevated window' 'Open Task Manager as administrator (Ctrl+Shift+Esc), click into it, then tap Win and type  note  without clicking.' 'Did "note" appear in the launcher search box?')
        [void](Ask-Check 'LA.14' 'Quick re-open stays open' 'Tap Win, press Esc, and IMMEDIATELY tap Win again. Do this 3 times.' 'Did the launcher stay open every time (not vanish by itself)?')
        [void](Ask-Check 'LA.15' 'Alt+F4 only closes it' 'Open the launcher, press Alt+F4, then tap Win.' 'Did the launcher open again normally after Alt+F4?')
        [void](Ask-Check 'LA.16' 'Win after locking' 'Press Win+L to lock, sign back in, then tap Win ONCE.' 'Did the launcher open (and NOT Windows Start)?')
        [void](Ask-Check 'LA.11' 'Most used follows launches' 'Launch two different apps from the launcher (e.g. Notepad and Paint), close them, then open the launcher with nothing typed.' 'Are those apps now at the start of MOST USED?')
        Write-Do 'FULL SCREEN: open a YouTube video in your browser (not full screen yet).'
        Write-Do 'After you press Enter you get 15 seconds: click the video and press f (full screen). Do NOT click this window. A beep ends it.'
        Wait-Enter 'Press Enter to start the 15 seconds'
        $o = Get-LogLength $AppLog
        Start-Sleep -Seconds 15
        try { [console]::Beep(880, 300) } catch { }
        $l = Get-LogSince $AppLog $o
        [void](Complete-Check -Id 'LA.12' -Description 'Full screen is detected while the launcher feature runs' -Measured @{ fullscreenLogged = ($l -match 'fullscreen=True') } `
            -MeasuredPass ($l -match 'fullscreen=True') -AnswerRequired $false)
        Write-Do 'Leave full screen now.'

        # Watchdog relaunch (Plan 2): kill the watchdog; the shell starts a new one.
        $o = Get-LogLength $AppLog
        Get-Process -Name 'NothingShell.Watchdog' -ErrorAction SilentlyContinue | Stop-Process -Force
        [void](Wait-Until { (Get-LogSince $AppLog $o) -match 'watchdog relaunched' } 10000 250)
        $l = Get-LogSince $AppLog $o
        [void](Complete-Check -Id 'LA.13' -Description 'Killed watchdog is relaunched once' `
            -Measured @{ relaunchLogged = ($l -match 'watchdog relaunched'); watchdogs = @(Get-Process -Name 'NothingShell.Watchdog' -ErrorAction SilentlyContinue).Count } `
            -MeasuredPass (($l -match 'watchdog relaunched') -and @(Get-Process -Name 'NothingShell.Watchdog' -ErrorAction SilentlyContinue).Count -eq 1))

        Complete-Quit 'LA.quit' $app 'launcher section (--quit)'
    }

    # ================================================================ NOTCH + PANEL (plan 3)
    if ((Test-Section 'Notch') -or (Test-Section 'Panel')) {
        Write-Step 'NOTCH + PANEL: package identity, notch states, quick settings, notifications'
        $toast = Join-Path $Repo 'spikes\NotifySpike\send-test-toast.ps1'
        $logOff = Get-LogLength $AppLog
        $app = Start-App
        [void](Wait-Until { (Get-LogSince $AppLog $logOff) -match 'dock ready' } 15000 250)
        [void](Wait-Until { (Get-LogSince $AppLog $logOff) -match 'notifications: (listener ready|unavailable)|notification listener failed' } 10000 250)
        $l = Get-LogSince $AppLog $logOff
        [void](Complete-Check -Id 'PN.0' -Description 'Package identity present and notification listener ready' `
            -Measured @{ identity = ($l -match 'package identity=True'); listener = ($l -match 'notifications: listener ready') } `
            -MeasuredPass (($l -match 'package identity=True') -and ($l -match 'notifications: listener ready')))

        # PN.1: identity must not break the watchdog's restore (state file in the real %AppData%).
        Get-Process -Name 'NothingShell' -ErrorAction SilentlyContinue | Stop-Process -Force
        $back = Wait-Until { [NsWin]::TrayState() -eq 1 } 8000 200
        [void](Complete-Check -Id 'PN.1' -Description 'Hard kill with package identity: watchdog restores the taskbar' `
            -Measured @{ trayBackMs = $back } -MeasuredPass ($back -ge 0))
        $logOff = Get-LogLength $AppLog
        $app = Start-App
        [void](Wait-Until { (Get-LogSince $AppLog $logOff) -match 'dock ready' } 15000 250)

        if (Test-Section 'Notch') {
            [void](Ask-Check 'NT.1' 'Notch: top centre, dot-matrix time + battery' 'Look at the top centre of the screen.' 'Is there a black pill with the time in dots and the battery on the right?')
            $o = Get-LogLength $AppLog
            [void](Ask-Check 'NT.2' 'Launching shows "Opening …" in the notch' 'Tap Win, type  calc  and press Enter. Watch the notch.' 'Did the notch briefly say "Opening Calculator…"?' `
                -Measured @{ openingLogged = ((Get-LogSince $AppLog $o) -match 'notch state=Opening') } -AnswerRequired $true)
            Get-Process -Name CalculatorApp -ErrorAction SilentlyContinue | ForEach-Object { try { $_.CloseMainWindow() | Out-Null } catch { } }
            [void](Ask-Check 'NT.3' 'Music state' 'Play music (Spotify or a YouTube video in the browser, not full screen) for ~5 s, then pause it.' 'While playing: art + moving dots in the notch? After pausing: back to the time within ~1 s?')
            $o = Get-LogLength $AppLog
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $toast -Text ("NothingShell test " + (Get-Date -Format 'HH:mm:ss')) | Out-Null
            Start-Sleep -Seconds 2
            $l = Get-LogSince $AppLog $o
            $a = Ask-YN 'A test notification was just sent. Did the notch grow and show "Windows PowerShell" + the text for a few seconds?'
            [void](Complete-Check -Id 'NT.4' -Description 'New notification shows in the notch' -Measured @{ addedLogged = ($l -match 'notification added'); notchLogged = ($l -match 'notch state=Notification') } `
                -MeasuredPass (($l -match 'notification added') -and ($l -match 'notch state=Notification')) -Answer $a -Expect 'y')
            $banner = Ask-YN 'Did Windows also show its own pop-up at the bottom right? (just recording; y or n are both fine)'
            [void](Complete-Check -Id 'NT.4b' -Description 'Windows banner also shown (depends on your banner settings)' -Measured @{ windowsBanner = $banner } -Info)
            Write-Do 'FULL SCREEN: open a YouTube video. After Enter you get 15 s: click it, press f. A test notification is sent at 8 s. Do NOT click this window.'
            Wait-Enter 'Press Enter to start the 15 seconds'
            Start-Sleep -Seconds 8
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $toast -Text ("NothingShell test " + (Get-Date -Format 'HH:mm:ss')) | Out-Null
            Start-Sleep -Seconds 7
            try { [console]::Beep(880, 300) } catch { }
            [void](Ask-Check 'NT.5' 'Full screen hides the notch; a notification during it does not pop the notch' 'Leave full screen now.' 'In full screen: was the notch hidden, and did the notification NOT show in the notch?')
        }

        if (Test-Section 'Panel') {
            $o = Get-LogLength $AppLog
            [void](Ask-Check 'QP.1' 'Win+A / Win+N open Nothing Shell''s panel' 'Press Win+A. Then Win+A again. Then Win+N, and Esc.' 'Did our panel open bottom-right (and Windows'' own Quick Settings / notifications did NOT), and close again?' `
                -Measured @{ panelLogged = ((Get-LogSince $AppLog $o) -match 'win\+a/n -> panel') })
            [void](Ask-Check 'QP.2' 'Tray click opens the panel; click outside closes; dock stays up' 'Show the dock and click the tray (Wi-Fi / volume / clock area). Then click on the desktop.' 'Did the panel open with the dock staying visible, and close when you clicked outside?')
            [void](Ask-Check 'QP.3' 'Volume slider + output chip' 'Open the panel, drag the volume slider. If you have two outputs (e.g. buds connected), click the chip next to it.' 'Did the system volume follow the slider (and the chip switch device, if you have two)?')
            [void](Ask-Check 'QP.4' 'Brightness slider' 'Drag the brightness slider down and back up.' 'Did the screen brightness change?')
            [void](Ask-Check 'QP.5' 'Wi-Fi tile' 'Click the arrow (›) on the Wi-Fi tile. Optionally tap the tile to turn Wi-Fi off and on again (a few seconds offline).' 'Did it list networks (or a location hint), with your network marked Connected?')
            [void](Ask-Check 'QP.6' 'Bluetooth tile' 'Click the arrow on the Bluetooth tile. If your buds are paired, try Connect / Connected on them.' 'Did it list paired devices, and did connecting work (or open Bluetooth settings)?')
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $toast -Text ("NothingShell test " + (Get-Date -Format 'HH:mm:ss')) | Out-Null
            Start-Sleep -Seconds 2
            [void](Ask-Check 'QP.7' 'Notifications in the panel' 'Open the panel. Find "Windows PowerShell" under NOTIFICATIONS. Click its ✕, then send yourself nothing else and press CLEAR ALL if anything remains.' 'Was the test notification listed, and did ✕ / Clear all remove it?')
            [void](Ask-Check 'QP.8' 'Banner tip (one time)' 'Look at the top of NOTIFICATIONS for the tip about Windows pop-ups. Click "Got it".' 'Did the tip show and disappear after "Got it"?')
        }
        Complete-Quit 'NP.quit' $app 'notch + panel section (--quit)'
    }

    # ================================================================ DASH (Plan 4: notch dashboard, timer, settings)
    if (Test-Section 'Dash') {
        Write-Step 'DASH: notch dashboard (hover), timer, media, clipboard, notes, edit mode, shelf, weather, tools, settings'
        $logOff = Get-LogLength $AppLog
        $app = Start-App
        [void](Wait-Until { (Get-LogSince $AppLog $logOff) -match 'dock ready' } 15000 250)

        $o = Get-LogLength $AppLog
        [void](Ask-Check 'ND.1' 'Hover opens the dashboard; sweeping past does not; leaving closes it' `
            'Rest the mouse on the notch (top centre) for a moment. Move away. Then sweep the mouse quickly across the notch to a browser tab.' `
            'Did resting open the dashboard, moving away close it, and the quick sweep NOT open it?' `
            -Measured @{ openLogged = ((Get-LogSince $AppLog $o) -match 'notch state=Open') })
        [void](Ask-Check 'ND.2' 'Pages and Settings' 'Open the dashboard. Click the second page dot (bottom right), scroll the wheel back to page 1. Then right-click the notch.' `
            'Did the pages switch (dots and wheel), and did right-click open the Settings window?')

        $o = Get-LogLength $AppLog
        Write-Do 'TIMER: on the Timer tile press - until it shows 01:00, then press play. Close the dashboard and wait ~1 minute.'
        Wait-Enter 'Press Enter after the timer has ended'
        $l = Get-LogSince $AppLog $o
        [void](Ask-Check 'ND.3' 'Timer: live state, flash, "Time is up" in notch and panel' 'Open the panel (Win+A) and look at NOTIFICATIONS.' `
            'While running, did the notch show a small dot grid + countdown? At zero did it flash, show "Timer / Time is up", and is it listed in the panel?' `
            -Measured @{ finishedLogged = ($l -match 'timer finished'); timerState = ($l -match 'notch state=Timer') })

        [void](Ask-Check 'ND.4' 'Media tile' 'Play music, open the dashboard and look at the Media tile. Try previous / pause / next.' `
            'Did it show the track, artist, source app and moving progress dots, and did the buttons control the player?')

        [void](Ask-Check 'ND.5' 'Clipboard history' 'Copy two different bits of text (Ctrl+C). Open the dashboard: click the older one, then paste somewhere. Then copy a password from your password manager.' `
            'Were both copies listed, did clicking copy it again, and was the password NOT added?')
        [void](Ask-Check 'ND.6' 'Notes' 'Click into the Notes tile, type a line, move the mouse away from the notch while still typing, then press Esc.' `
            'Did the notch stay open while you typed, and close on Esc?')
        [void](Ask-Check 'ND.7' 'Edit mode' 'Click EDIT. Click a tile''s size chip, remove a tile with the red x, bring it back with + Add, then click DONE.' `
            'Did resizing, removing and adding back work, and did the notch stay open until DONE?')

        $o = Get-LogLength $AppLog
        Write-Do 'KEEP AWAKE: go to page 2 and turn Keep awake ON.'
        Wait-Enter
        $req = (& powercfg /requests 2>&1 | Out-String)
        [void](Complete-Check -Id 'ND.8' -Description 'Keep awake registers a display/system request' `
            -Measured @{ listed = ($req -match 'NothingShell'); logged = ((Get-LogSince $AppLog $o) -match 'keep awake=True') } `
            -MeasuredPass ($req -match 'NothingShell'))
        Write-Do 'Turn Keep awake OFF again.'
        Wait-Enter

        [void](Ask-Check 'ND.9' 'File shelf' 'Drag a file from File Explorer onto the notch (it opens; the shelf page shows). Drop it on the Shelf tile. Then drag the chip out onto the desktop.' `
            'Was the file added to the shelf, and did dragging it out copy it to the desktop?')
        [void](Ask-Check 'ND.10' 'Weather' 'Open Settings (right-click the notch), type your city, press Enter. Look at the Weather tile on page 2.' `
            'Did Settings find the city, and does the tile show the temperature in dots with high/low?')

        $shots = Join-Path ([Environment]::GetFolderPath('MyPictures')) 'Screenshots'
        $before = Get-Date
        [void](Ask-Check 'ND.11' 'Grab text, Color, Snip' 'Page 2: Grab text -> drag over some text, paste it in Notepad. Color -> click a pixel, paste. Snip -> drag a region, paste in Paint.' `
            'Did Grab text copy the text, Color copy a #hex, and Snip copy the image (each with a card in the notch)?' `
            -Measured @{ snipSaved = (@(Get-ChildItem $shots -Filter 'Nothing Shell *.png' -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $before }).Count -gt 0) })
        [void](Ask-Check 'ND.12' 'Phone and Draw' 'Page 2: click Phone, then Draw.' 'Did Phone Link open (or a card say it is not installed), and did Snipping Tool open?')

        Write-Do 'FULL SCREEN: start a 1:00 timer, then open a YouTube video full screen (f) and wait until the timer has ended; also try resting the mouse at the top centre.'
        Wait-Enter 'Press Enter after leaving full screen'
        [void](Ask-Check 'ND.13' 'Full screen: no dashboard, time-up only in the panel' 'Open the panel (Win+A).' `
            'In full screen: did the notch stay hidden (no dashboard on hover, no "Time is up" card), and is "Time is up" listed in the panel?')

        # Persistence: restart once, then check what was saved.
        [void](Quit-App $app)
        $logOff = Get-LogLength $AppLog
        $app = Start-App
        [void](Wait-Until { (Get-LogSince $AppLog $logOff) -match 'dock ready' } 15000 250)
        [void](Ask-Check 'ND.R' 'Saved across a restart' 'Nothing Shell was just restarted. Open the dashboard.' `
            'Are the timer length, clipboard items, your note, the edited layout, the shelf file and the weather city all still there?')
        Complete-Quit 'ND.quit' $app 'dash section (--quit)'
    }

    if (Test-Section 'Widgets') {
        Write-Step 'WIDGETS: desktop widgets, snake, edit mode, settings size / wallpaper, full screen'
        $logOff = Get-LogLength $AppLog
        $app = Start-App
        [void](Wait-Until { (Get-LogSince $AppLog $logOff) -match 'dock ready' } 15000 250)

        [void](Ask-Check 'DW.1' 'Seven widgets on the desktop, under app windows' 'Look at the desktop (minimise windows if needed), then open any app window over a widget.' `
            'Are clock, snake, timer, calendar, weather, media and battery all on the desktop, and does an app window cover them?')
        [void](Ask-Check 'DW.2' 'Win+D: widgets stay visible and clickable' 'Press Win+D. Click the Timer widget to start it, click it again to pause.' `
            'After Win+D were the widgets still there, and did the timer start and pause on click?')

        if ((Ask-YN 'DW.3 restarts Explorer (the screen flickers for a few seconds). Restart Explorer now?') -eq 'y') {
            $o = Get-LogLength $AppLog
            Stop-Process -Name explorer -Force
            $ms = Wait-Until { (Get-LogSince $AppLog $o) -match 'widgets reattached in \d+ ms' } 10000 100
            if (@(Get-Process -Name explorer -ErrorAction SilentlyContinue).Count -eq 0) { Ensure-Explorer }
            $l = Get-LogSince $AppLog $o
            $n = if ($l -match 'widgets reattached in (\d+) ms') { [int]$Matches[1] } else { -1 }
            [void](Complete-Check -Id 'DW.3' -Description 'Explorer restart: widgets back within 2 s' `
                -Measured @{ reattachedMs = $n; waitedMs = $ms } -MeasuredPass ($n -ge 0 -and $n -le 2000))
            [void](Ensure-TaskbarBack 'after DW.3')
        }
        else { [void](Complete-Check -Id 'DW.3' -Description 'Explorer restart: widgets back within 2 s' -Answer 's' -Expect 'y' -AnswerRequired $false -Notes 'skipped by the user') }

        [void](Ask-Check 'DW.4' 'No widget in Alt+Tab' 'Hold Alt and press Tab a few times.' 'Is Alt+Tab free of any widget (or empty desktop-looking) entry?')
        [void](Ask-Check 'DW.5' 'Clock legible on every wallpaper' 'Right-click the notch > Settings > Wallpaper: click each of the four in turn and look at the clock.' `
            'Was the clock legible on all four (dark dots on light, light dots on dark)? (Pick the one you want to keep last.)')

        [void](Ask-Check 'DW.6' 'Snake' 'Click the snake board, play with arrows / WASD, press Space to pause and again to resume. Then click another window and press the arrow keys there. Note your best score.' `
            'Did the snake play and pause, did clicking elsewhere pause it, and did the arrows work normally in the other window?')
        [void](Ask-Check 'DW.7' 'Timer widget = notch timer' 'Set 01:00 on the Timer widget (- / +) and start it. Hover the notch and look at the Timer tile. Wait until it ends.' `
            'Did the notch tile show the same countdown, and did the widget flash at zero?')
        [void](Ask-Check 'DW.8' 'Weather and Media match the notch' 'Play music. Compare the Weather and Media widgets with the notch tiles.' `
            'Do they match (temperature, track, progress), and do the media buttons work?')
        [void](Ask-Check 'DW.9' 'Battery' 'Look at the Battery widget; connect Bluetooth buds if you have them; use the dots or the wheel.' `
            'Does it show the laptop (and the buds when connected), and did connecting jump to the new device?')

        $o = Get-LogLength $AppLog
        [void](Ask-Check 'DW.10' 'Edit mode' 'Right-click a widget > Edit widgets. Drag one onto another (it should turn red and slide back), drag one to a free spot, click a size chip, then click Done.' `
            'Did the dot grid show, did dragging snap, did the overlap show red and refuse, did the chip resize, and did Done end it?' `
            -Measured @{ editOn = ((Get-LogSince $AppLog $o) -match 'widget edit mode on'); editOff = ((Get-LogSince $AppLog $o) -match 'widget edit mode off') })
        [void](Ask-Check 'DW.13' 'Add and remove widgets' 'In edit mode click + Add: add a Photo frame and an App folder. Click the x on one widget to remove it, then open + Add again.' `
            'Were the frame and folder placed on free spots, did x remove the widget, and does + Add now list the removed one again (and never a one-off widget that is still on the desktop)?')
        [void](Ask-Check 'DW.14' 'Photo frame' 'Click Done. Click the empty frame and pick a few photos. In edit mode click its shape chip (square / circle). Wait a minute. Click the frame: remove one photo with x, add one with +.' `
            'Did the photos appear inside the card, did the shape switch, did the slideshow change after a minute, and did removing / adding work?')
        [void](Ask-Check 'DW.15' 'App folder' 'In edit mode click the folder once (its picture changes). Click Done. Click the folder: + adds apps (search one), click an icon to open it, right-click an icon > Remove from folder.' `
            'Did the picture change, did the popup show the apps with +, did an icon open its app, and did Remove from folder work?')
        [void](Ask-Check 'DW.11' 'Settings: widget size S / M / L' 'Settings > Widgets: click S, then L, then M.' 'Did the widgets resize live each time?')

        Write-Do 'FULL SCREEN: open a YouTube video full screen (f) for a few seconds, then leave full screen.'
        Wait-Enter 'Press Enter after leaving full screen'
        [void](Ask-Check 'DW.12' 'Full screen: no widget over the video' '' 'In full screen, was nothing of the widgets visible over the video?')

        [void](Quit-App $app)
        $logOff = Get-LogLength $AppLog
        $app = Start-App
        [void](Wait-Until { (Get-LogSince $AppLog $logOff) -match 'dock ready' } 15000 250)
        [void](Ask-Check 'DW.R' 'Saved across a restart' 'Nothing Shell was just restarted. Look at the desktop.' `
            'Are the widget positions and sizes, the widget scale, the wallpaper and the snake best score all still there?')
        [void](Ask-Check 'DW.16' 'Frames, folders and removals survive a restart' 'Look at the photo frames, app folders and the widget you removed.' `
            'Do frames keep their photos and shape, folders their picture and apps, and does the removed widget stay removed?')
        Complete-Quit 'DW.quit' $app 'widgets section (--quit)'
    }

    if (Test-Section 'Widgets') {
        Write-Step 'DOCK PREVIEWS (Plan 7): window previews'
        $logOff = Get-LogLength $AppLog
        $app = Start-App
        [void](Wait-Until { (Get-LogSince $AppLog $logOff) -match 'dock ready' } 15000 250)
        [void](Ask-Check 'DD.5' 'Window previews' 'Rest the mouse on a running app with 2+ windows (e.g. two File Explorer windows), click a preview, then reopen it and press a preview''s x.' `
            'Did live previews with titles appear, did clicking switch to that window, and did x close it?')
        [void](Ask-Check 'DD.6' 'Previews switch off' 'Settings > Shell: turn off "Window previews in the dock", hover a running app, then turn it back on.' `
            'With it off, did hovering show just the name?')
        Complete-Quit 'DD.quit' $app 'dock effects section (--quit)'
    }

    # ================================================================ PLAN 9: new widgets and sync
    if (Test-Section 'Plan9') {
        Write-Step 'PLAN 9: tic-tac-toe, notes, alarm, folder pictures, sync'
        $logOff = Get-LogLength $AppLog
        $app = Start-App
        [void](Wait-Until { (Get-LogSince $AppLog $logOff) -match 'dock ready' } 15000 250)
        [void](Ask-Check 'P9.1' 'Tic-tac-toe' 'Edit widgets > Add > Tic-tac-toe. Play two games (try to win one, lose or draw one).' `
            'Did Nothing answer each move, block your wins, show who won, and count the score under the board?')
        [void](Ask-Check 'P9.2' 'Notes in both places' 'Add > Notes. Click it, type a line, press Esc. Then open the notch Notes tile.' `
            'Did the widget show your line, and did the notch tile show the same text (and the other way round)?')
        [void](Ask-Check 'P9.3' 'Alarm rings' 'Add > Alarm. Click it, add an alarm, set it to the next minute (e.g. 14:05), switch it on, close with Esc and wait.' `
            'At that minute: sound, "Alarm" in the notch, the Snooze / Stop card at the top and a red flashing widget? Did Snooze silence it and ring again 5 minutes later, and Stop end it?')
        [void](Ask-Check 'P9.4' 'Folder pictures' 'In edit mode, click an app folder a few times.' `
            'Did the new pictures (rocket, heart, moon, sun, code, book, bolt, ghost) come round?')
        [void](Ask-Check 'P9.5' 'Sync' 'Settings > Sync > Choose folder… and pick an empty test folder. Then open "<that folder>\Nothing Shell sync".' `
            'Does it hold settings.json, notes.txt (and alarms.json once you made one), and does Settings say SAVED TO THE FOLDER hh:mm (· ONEDRIVE UPLOADS IT for a OneDrive folder)?')
        $p9Log = Get-LogSince $AppLog $logOff
        if ($p9Log -match 'sync: \d+ pulled, \d+ pushed') { Write-Good 'log shows a sync (counts only)' } else { Write-Bad 'no "sync: ..." line in the log' }
        [void](Ask-Check 'P9.6' 'Sync off' 'Settings > Sync > Turn off.' 'Does Settings show the "Choose folder…" text again?')
        Complete-Quit 'P9.quit' $app 'plan 9 section (--quit)'
    }

    # ================================================================ GOOGLE CALENDAR (Plan 6)
    if (Test-Section 'Calendar') {
        Write-Step 'GOOGLE CALENDAR (Plan 6): connect, dots, next event, offline, disconnect'
        $logOff = Get-LogLength $AppLog
        $app = Start-App
        [void](Wait-Until { (Get-LogSince $AppLog $logOff) -match 'dock ready' } 15000 250)
        [void](Ask-Check 'GC.1' 'Hint in edit mode only' 'Not connected yet: look at the calendar widget, then enter Edit widgets.' `
            'Plain dates normally, and "Connect Google Calendar in Settings" only while editing?')
        [void](Ask-Check 'GC.2' 'Guided setup and sign-in' 'Settings > Google Calendar: follow the four steps (Open Google Cloud Console), paste the client ID and secret, press Connect and approve in the browser (Advanced > Continue on the unverified-app warning).' `
            'Did the browser say you can close the tab, and does Settings show CONNECTED with a number of calendars?')
        [void](Ask-Check 'GC.3' 'Event dots' 'Look at the 2x2 calendar widget and compare with Google Calendar.' `
            'Is there a dot under each day that has an event (holidays and birthdays included), white on today''s red circle?')
        [void](Ask-Check 'GC.4' 'Next event' 'Switch the calendar widget to 2x1 in edit mode.' `
            'Does the right half show the next event with TODAY / TOMORROW / a day and its title?')
        [void](Ask-Check 'GC.5' 'Offline' 'Turn Wi-Fi off yourself, press Refresh in Settings, then turn Wi-Fi back on.' `
            'Did the status say OFFLINE and did the dots stay?')
        $calLog = Get-LogSince $AppLog $logOff
        if ($calLog -match 'calendar: refreshed') { Write-Good 'log shows a calendar refresh (counts only)' } else { Write-Bad 'no "calendar: refreshed" line in the log' }
        [void](Ask-Check 'GC.6' 'Disconnect' 'Settings > Google Calendar > Disconnect.' `
            'Do the dots and the next event go away, and does Settings show the setup steps again?')
        Complete-Quit 'GC.quit' $app 'google calendar section (--quit)'
    }

    # ================================================================ STARTUP (task 16)
    if (Test-Section 'Startup') {
        Write-Step 'STARTUP: Fenn guard, single instance, start with Windows'
        $fennNow = @(Get-Process -Name 'fenn' -ErrorAction SilentlyContinue).Count -gt 0
        if (-not $fennNow) {
            Write-Do 'Please START Fenn now (as you normally do). I will wait up to 2 minutes. (Press S to skip this part.)'
            $skipFenn = $false
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            while (@(Get-Process -Name 'fenn' -ErrorAction SilentlyContinue).Count -eq 0 -and $sw.Elapsed.TotalSeconds -lt 120 -and -not $skipFenn) {
                try { if ([Console]::KeyAvailable) { $k = [Console]::ReadKey($true); if ($k.KeyChar -in 's', 'S') { $skipFenn = $true } } } catch { }
                Start-Sleep -Milliseconds 500
            }
            $fennNow = @(Get-Process -Name 'fenn' -ErrorAction SilentlyContinue).Count -gt 0
            if ($fennNow) { Start-Sleep -Seconds 4 }
        }
        if ($fennNow) {
            $ahBefore = [NsWin]::GetAutoHide()
            $app = Start-App
            $dialogs = @([NsWin]::TopLevelOfProcess([uint32]$app.Id)).Count
            $wdCount = (Get-WatchdogInfo).Count; $stateThere = (Test-Path -LiteralPath $StateFile)
            $ahAfter = [NsWin]::GetAutoHide()
            $a = Ask-YN 'Is there a box saying "Fenn is running. Quit Fenn to use Nothing Shell..." with "Check again" and "Quit Nothing Shell" buttons?'
            [void](Complete-Check -Id 'ST.1' -Description 'Fenn running: warning box shown and the taskbar is left alone' `
                -Measured @{ appAlive = [bool](Get-Process -Id $app.Id -ErrorAction SilentlyContinue); visibleWindows = $dialogs; watchdogProcesses = $wdCount; stateFilePresent = $stateThere; autoHideBefore = $ahBefore; autoHideAfter = $ahAfter } `
                -MeasuredPass ($wdCount -eq 0 -and -not $stateThere -and $ahBefore -eq $ahAfter -and $dialogs -ge 1) -Answer $a -Expect 'y' -AnswerRequired $false)
            Write-Do 'Now QUIT Fenn (tray icon > Quit), then click "Check again" in the box.'
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            $takeMs = Wait-Until { (@(Get-Process -Name 'fenn' -ErrorAction SilentlyContinue).Count -eq 0) -and ((Get-WatchdogInfo).Count -gt 0) -and ([NsWin]::TrayState() -eq 0) } 240000 500
            $a = Ask-YN 'Did Nothing Shell take over (box gone, Windows taskbar hidden, dock works)?'
            [void](Complete-Check -Id 'ST.2' -Description 'Fenn quit + Check again: Nothing Shell takes over' `
                -Measured @{ takeoverMs = $takeMs; watchdogProcesses = (Get-WatchdogInfo).Count; trayHidden = ([NsWin]::TrayState() -eq 0) } -MeasuredPass ($takeMs -ge 0) -Answer $a -Expect 'y' -AnswerRequired $false)
        } else {
            [void](Complete-Check -Id 'ST.1' -Description 'Fenn running: warning box shown and the taskbar is left alone' -Measured @{ fennStarted = $false } -Answer 's' -Expect 'y' -Notes 'Skipped (Fenn not started).')
            $app = Start-App
            Start-Sleep -Seconds 2
        }
        # second copy
        $cnt0 = @(Get-Process -Name NothingShell -ErrorAction SilentlyContinue).Count
        $winsBefore = @([NsWin]::TopLevelOfProcess([uint32]$app.Id)).Count
        [void](Invoke-Unelevated ('"{0}"' -f $AppExe) '')
        Start-Sleep -Seconds 5
        $cnt1 = @(Get-Process -Name NothingShell -ErrorAction SilentlyContinue).Count
        $a = Ask-YN 'I just started a second copy. Did anything visible happen (box, window, flicker)?'
        [void](Complete-Check -Id 'ST.3' -Description 'Second copy: nothing happens, still one process' `
            -Measured @{ processesBefore = $cnt0; processesAfter = $cnt1 } -MeasuredPass ($cnt1 -eq $cnt0 -and $cnt1 -eq 1) -Answer $a -Expect 'n' -AnswerRequired $false)
        # Run value
        $rv = Get-RunValue
        $want = '"' + $AppExe + '"'
        [void](Complete-Check -Id 'ST.4' -Description 'Start with Windows: HKCU Run value points at the installed copy' `
            -Measured @{ runValue = $rv; expected = $want; startWithWindowsSetting = (Get-Prop (Read-DockSettings) 'StartWithWindows') } `
            -MeasuredPass (($rv -replace '"', '') -ieq $AppExe))
        [void](Ask-Check 'ST.5' 'Menu shows a tick next to Start with Windows' `
            'Right-click the start button on the dock.' 'Is there a tick next to "Start with Windows"? (press Esc to close the menu)')
        $script:StartupApp = $app
    }

    # ================================================================ LAST QUESTION
    if ((Test-Section 'Dock') -or (Test-Section 'Apps') -or (Test-Section 'Tray') -or (Test-Section 'Reserve') -or (Test-Section 'Launcher') -or (Test-Section 'Notch') -or (Test-Section 'Panel') -or (Test-Section 'Dash') -or (Test-Section 'Widgets') -or (Test-Section 'Startup')) {
        Write-Step 'LAST QUESTION'
        Write-Host '  Keep Nothing Shell as your taskbar from now on?' -ForegroundColor Cyan
        Write-Host '    y = Nothing Shell starts at sign-in and keeps running (Fenn must stay closed)'
        Write-Host '    n = go back to Fenn (Nothing Shell is closed and will not start at sign-in)'
        do { $keep = (Read-Host '  ? Keep Nothing Shell? [y/n]').Trim().ToLowerInvariant() } until ($keep -in 'y', 'n', 'yes', 'no')
        $keepYes = $keep -in 'y', 'yes'
        $script:KeepChoice = if ($keepYes) { 'y' } else { 'n' }
        $running = @(Get-AppProcess)
        foreach ($rp in $running) { $qr = Quit-App $rp; Write-Info ("closed running copy: exited={0}, work area ok={1}" -f $qr.exited, $qr.workAreaOk) }
        if ($keepYes) {
            Write-Info 'Starting Nothing Shell for good (independent of this script)...'
            [void](Invoke-Unelevated ('explorer.exe "{0}"' -f $AppExe) '')
            $upMs = Wait-Until { (Get-AppProcess).Count -gt 0 } 15000 250
            $hideMs = Wait-Until { [NsWin]::TrayState() -eq 0 } 10000 200
            $rv = Get-RunValue; $fixed = $false
            if (($rv -replace '"', '') -ine $AppExe) {
                Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'NothingShell' -Value ('"' + $AppExe + '"')
                [void](Set-SettingBool 'startWithWindows' $true)
                $fixed = $true; $rv = Get-RunValue
            }
            $script:KeepRunning = ($upMs -ge 0)
            $elevK = $null; if ($upMs -ge 0) { $elevK = [NsWin]::IsElevated([uint32](Get-AppProcess)[0].Id) }
            [void](Complete-Check -Id 'KEEP' -Description 'Final choice: keep Nothing Shell as the taskbar' `
                -Measured @{ answer = 'y'; appStartedMs = $upMs; taskbarHiddenMs = $hideMs; runValue = $rv; runValueWrittenByScript = $fixed; appElevated = ($elevK -eq 1); watchdogProcesses = (Get-WatchdogInfo).Count } `
                -MeasuredPass ($upMs -ge 0 -and $hideMs -ge 0 -and (($rv -replace '"', '') -ieq $AppExe) -and $elevK -eq 0) -Answer 'y' -AnswerRequired $false)
        } else {
            try { Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'NothingShell' -ErrorAction SilentlyContinue } catch { }
            $setOff = Set-SettingBool 'startWithWindows' $false
            [void](Ensure-TaskbarBack 'after quitting')
            [void](Complete-Check -Id 'KEEP' -Description 'Final choice: go back to Fenn' `
                -Measured @{ answer = 'n'; runValueNow = (Get-RunValue); settingStartWithWindowsOff = $setOff; trayVisible = ([NsWin]::TrayState() -eq 1) } `
                -MeasuredPass ($null -eq (Get-RunValue) -and [NsWin]::TrayState() -eq 1) -Answer 'n' -AnswerRequired $false)
            Write-Good '  Nothing Shell is closed. Start Fenn again from the Start menu.'
        }
    }
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
        if ($script:KeepRunning) {
            $keepApp = @(Get-AppProcess)
            [void](Complete-Check -Id 'END' -Description 'Kept running: Nothing Shell up, Windows taskbar hidden, helper alive, Run value set' `
                -Measured @{ appRunning = ($keepApp.Count -eq 1); trayState = $endTray; watchdogProcesses = (Get-WatchdogInfo).Count; runValue = (Get-RunValue); workArea = (([NsWin]::WorkArea()) -join ',') } `
                -MeasuredPass ($keepApp.Count -eq 1 -and $endTray -eq 0 -and (Get-WatchdogInfo).Count -ge 1))
        } else {
            [void](Complete-Check -Id 'END' -Description 'Final state equals starting state; no test processes left' `
                -Measured @{ startAutoHide = $script:OrigAutoHide; endAutoHide = $endAh; startTrayState = $script:OrigTray; endTrayState = $endTray; startWorkArea = ($script:OrigWorkArea -join ','); endWorkArea = (([NsWin]::WorkArea()) -join ','); leftoverProcesses = $leftoverProcs; stateFilePresent = (Test-Path -LiteralPath $StateFile) } `
                -MeasuredPass ($same -and $leftoverProcs.Count -eq 0 -and ((([NsWin]::WorkArea()) -join ',') -eq ($script:OrigWorkArea -join ','))))
        }
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
    if ($script:KeepRunning) {
        Write-Good '  Nothing Shell is running as your taskbar. Keep Fenn closed.'
        Write-Host ''
        Write-Host '  LAST CHECK (please do this now):' -ForegroundColor Cyan
        Write-Host '    Sign out of Windows and sign back in.'
        Write-Host '    The dock should work and the Windows taskbar should stay hidden, with no prompts.'
        Write-Host '    Then tell Claude what you saw.'
    } elseif ($script:KeepChoice -eq 'n') {
        Write-Good '  You can start Fenn again now.'
    } else {
        Write-Good '  You can start Fenn again now.'
    }
    Write-Host "  Log file:     $LogPath"
    Write-Host "  Results file: $($script:ResultsPath)"
    Write-Host '  The app and test programs stay installed. Please tell Claude that the run finished.'
    try { Stop-Transcript | Out-Null } catch { }
    try { Unregister-Event -SourceIdentifier PowerShell.Exiting -ErrorAction SilentlyContinue } catch { }
}
if ($fatal) { exit 1 }
