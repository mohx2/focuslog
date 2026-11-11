# FocusLog

A private, battery-friendly Windows 11 app that automatically records which application is in the
foreground, so you know how long you *really* studied in **Claude Desktop**.

- Fully automatic: no Start/Stop. Reading counts (no idle detection).
- Pauses on app switch, minimise, lock, sleep and screen-off. One app owns time at any moment.
- Local only: SQLite file on your PC. No account, no network, no telemetry.
- Native .NET 8 + WPF, event-driven (no polling while nothing is tracked), tray icon, dark UI.

## Getting FocusLog.exe (no tools to install)

1. Create a free repository on github.com (e.g. `focuslog`, private is fine).
2. Unzip `FocusLog-source.zip`. Upload the **contents** of the `FocusLog` folder to the repo
   (Add file > Upload files, then drag everything in, including the `.github` folder). Commit to `main`.
   - If `.github` does not upload, use Add file > Create new file, type `.github/workflows/build.yml` as the
     name, and paste in the contents of that file.
3. Open the **Actions** tab. The "Build FocusLog" run starts by itself (about 3-6 minutes).
   If it does not, open the workflow and press **Run workflow**.
4. When it is green, open the run and download the **FocusLog** artifact (a zip). It contains
   `FocusLog.exe` and `FocusLog-portable.zip`.
5. Run `FocusLog.exe`. Windows SmartScreen may say "Windows protected your PC" because the file is not
   code-signed: click **More info > Run anyway**.

The first launch of the single-file exe unpacks a few native libraries (a second or two); later launches are faster.
The "Unit tests" step shows the test results in the run log.

## Using it

| Where | What |
|---|---|
| Overview | Today's Claude time (big), current session, sessions, longest, average, week, month, weekly goal, last 7 days, top apps |
| Applications | Usage per app for Today / Yesterday / 7 days / 30 days / This month / Custom range, sortable |
| Analytics | Totals, daily average, Claude vs other, distribution, weekly and monthly trends |
| History | Every session with start, end and duration; step through previous days |
| Settings | Study apps, startup, tray behaviour, accent colour, CSV export, DB backup, clear data, shortcuts |

Tray icon: left-click opens the dashboard. Right-click: Open Dashboard, Pause Tracking, Resume Tracking, Exit.
Closing the window keeps tracking in the tray (switchable in Settings). **Exit** from the tray stops everything.

## How tracking works

- Foreground changes arrive through `SetWinEventHook` (foreground + minimise events), not polling.
  Events are coalesced for 600 ms, so Alt+Tab flicker is not recorded; sessions under 2 s are dropped.
- The app is identified by **executable**: `GetForegroundWindow` -> `GetWindowThreadProcessId` ->
  `QueryFullProcessImageNameW`. Claude Desktop is an Electron app; all its processes are `claude.exe`, and only the
  window actually in front counts, so helper processes are never counted separately. The executable name is
  configurable in Settings > Study applications.
- Several Chrome windows are one "Google Chrome". YouTube / Instagram / WhatsApp Web are recognised only from the
  browser window title (kept in memory, never stored). Titles are a heuristic: a page whose title merely
  mentions a site can occasionally be misclassified. Patterns are editable in `settings.json`.
- Durations use a monotonic clock (`Stopwatch`), so changing the system clock cannot corrupt them.
- Lock/unlock, suspend/resume and display on/off come from Windows notifications. A 5-second heartbeat (running
  only while a session is open) also detects sleeps that sent no notification: any gap is discarded.
- Sessions crossing midnight are split at local midnight. The open session is checkpointed every 30 s, so a crash
  loses at most about 30 s and the session is closed automatically on the next start.
- Not counted: the desktop, taskbar, Start menu, Alt+Tab switcher, the lock screen and FocusLog's own window.

## Data and privacy

Stored: application name, executable, start time and duration. Never stored: keystrokes, screenshots, clipboard,
window titles, browsing history, messages, files. Data folder: `%LOCALAPPDATA%\FocusLog`
(`focuslog.db`, `settings.json`, `error.log`).

**Portable mode:** create an empty `portable.txt` next to `FocusLog.exe`; data is then kept in `.\Data`.

## Battery and performance

Design targets (not measured results): negligible CPU, no network, minimal disk writes (one tiny SQLite write per
30 s while tracking), no timers at all when nothing is being tracked, no UI work while the dashboard is closed
(the window is destroyed, memory is trimmed). See `docs/PERFORMANCE.md` for a reproducible way to measure CPU,
RAM, disk and battery on your EliteBook with `tools/measure.ps1`.

## Uninstall

Settings > turn off "Launch at Windows startup", Exit from the tray, delete `FocusLog.exe`, the shortcuts, and
(optionally) `%LOCALAPPDATA%\FocusLog`.

## Project layout

```
src/FocusLog/Core      engine (state machine), database, reports, settings - no UI/OS dependencies
src/FocusLog/Native    Win32: foreground probe, event hook, power/lock watcher, startup, shortcuts
src/FocusLog/UI        WPF dashboard pages, tray icon, charts
tests/FocusLog.Tests   xUnit tests for the Core
```

MIT licensed.
