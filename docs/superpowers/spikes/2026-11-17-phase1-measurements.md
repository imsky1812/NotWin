# Phase 1 measurements (Plan 1: foundation + dock)

Measured 2026-11-18 on the dev PC: Windows 11 10.0.26300, 12 logical cores, 1920×1080 at 125%.
Installed Release build (signed, uiAccess) from `plan1-foundation` at d73f9e1, keep-visible off. Six pinned
apps and about eight running.

## Budget (spec §3: under 100 MB total working set and about 0% CPU at idle, after 10 minutes)

The shell (pid 18748) and its watchdog started at 15:27:06. The PC was idle for each sample: no mouse
or keyboard input.

| Sample | Process | Working set | Private bytes | CPU over 60 s |
|---|---|---|---|---|
| ~1 min after start (15:28–15:29) | NothingShell | 111.4 → 103.6 MB | 51.4 MB | 15 ms (0.002% of total CPU) |
| | Watchdog | 22.1 → 21.5 MB | 6.3 MB | 0 ms |
| | **Total** | **125.1 MB** | **57.7 MB** | |
| **13 min after start (15:40–15:41)** | NothingShell | 60.6 → 59.3 MB | 41.0 MB | 15 ms (0.002% of total CPU) |
| | Watchdog | 1.8 → 1.9 MB | 6.2 MB | 0 ms |
| | **Total** | **61.2 MB** | **47.2 MB** | |

Method: `Get-Process` (WorkingSet64, PrivateMemorySize64, TotalProcessorTime) before and after a
60-sample `Get-Counter '\Process(NothingShell*)\% Processor Time' -SampleInterval 1`.

**Result: PASS.** The 10-minute idle working set is 61.2 MB, under the 100 MB limit. CPU is about 0.002%
of total, under the 0.5% limit.

## Notes

- The working set is high right after start: about 125 MB total. It includes WPF/.NET startup pages and
  the icon loading. Windows trims it to about 60 MB once the shell is idle. Earlier hands-on samples were
  taken while the PC was in use: 62–114 MB WS for the app alone, private 25–53 MB. Private bytes, the
  memory the shell actually holds, stay at about 40–55 MB throughout.
- The watchdog is framework-dependent, not Native AOT (ruling R7). Its working set falls from 22 MB to
  about 2 MB once idle, and its private bytes are about 6 MB. An AOT watchdog is not needed for the budget.
- Ruling R15 (WS vs private bytes) needs no decision now: the spec's working-set target passes at
  10 minutes. Plans 2–5 add UI (launcher, panels, notch, widgets), so re-measure after each plan.
- Earlier per-feature readings: the idle dock after Task 11 used 62.4 MB WS / 52.7 MB private with
  16 ms CPU per 30 s. The tray (Task 14) added about 16 MB WS / 3 MB private while active, about half of
  it from the WinRT projection.

## Tests

`dotnet test`: 68/68 pass (NothingShell.Core.Tests).
