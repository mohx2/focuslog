# Measuring FocusLog's CPU, RAM, disk and battery impact

FocusLog is *designed* to be light, but no number is claimed here: measure on your own laptop.

## Why it should be cheap
- Foreground changes are delivered by Windows events (no polling loop).
- The only timer is a 5 s heartbeat that exists **only while a session is open**; when you are idle, locked,
  asleep, paused or on the desktop there are zero wake-ups.
- One tiny SQLite write every 30 s (WAL, `synchronous=NORMAL`).
- Closing the dashboard destroys the window and trims memory; no UI timers run.
- Self-contained .NET, no Chromium/Electron, no network access.

## 1. CPU, memory, disk (about 30 minutes)
1. Start FocusLog, close the dashboard (tray only), and use the laptop normally (study in Claude, browse).
2. In PowerShell run: `powershell -ExecutionPolicy Bypass -File tools\measure.ps1 -Minutes 30`
3. It writes `focuslog-measure.csv` every 10 s with: CPU % (of the whole machine), working set and private
   memory (MB), cumulative disk bytes written, and battery discharge rate (mW, if Windows exposes it).
4. Expect to look at the **average** CPU and the **write bytes per hour**. Also compare in Task Manager >
   Details (add columns *CPU*, *Power usage*, *Power usage trend*).

## 2. Battery A/B test (the honest way to get "% per hour")
Battery readings are noisy, so compare *with* vs *without* FocusLog under the same workload.
1. Unplug at about 90 %. Same brightness, Wi-Fi, same activity (e.g. a fixed 30-minute video/PDF routine).
2. Run A = FocusLog **not running**, 30-60 min. Note the battery % and `powercfg /batteryreport`
   (`powercfg /batteryreport /output "%USERPROFILE%\battery.html"`).
3. Charge back to the same level, repeat as B = FocusLog running.
4. Repeat each at least 3 times and compare the averages. If the difference is inside the run-to-run spread, the
   impact is not measurable on your machine. For a finer view use `powercfg /energy` and Windows Performance
   Recorder (Power profile) and look for FocusLog wake-ups.

## 3. Wake-up check
Task Manager > Details > right-click column header > Select columns > *Power usage* should read "Very low"
for FocusLog.exe. In Resource Monitor > CPU, FocusLog.exe should sit at about 0 % between switches.
