using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using FocusLog.Core;

namespace FocusLog.Native;

/// <summary>
/// One hidden top-level window that receives: session lock/unlock, suspend/resume, display on/off
/// (lid closed / screen timeout) and time/zone changes. No timers, no polling.
/// </summary>
internal sealed class PowerWatcher : IDisposable
{
    private const int WM_TIMECHANGE = 0x001E;
    private const int WM_POWERBROADCAST = 0x0218;
    private const int WM_WTSSESSION_CHANGE = 0x02B1;
    private const int PBT_APMSUSPEND = 0x0004;
    private const int PBT_APMRESUMESUSPEND = 0x0007;
    private const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    private const int PBT_POWERSETTINGCHANGE = 0x8013;
    private const int WTS_SESSION_LOCK = 7;
    private const int WTS_SESSION_UNLOCK = 8;

    private static Guid _guidDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    private HwndSource? _src;
    private IntPtr _displayReg;

    public event Action<Block, bool>? BlockChanged;
    public event Action? TimeChanged;

    public PowerWatcher()
    {
        var p = new HwndSourceParameters("FocusLogPowerWatcher")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, never shown
            ExtendedWindowStyle = 0x00000080,         // WS_EX_TOOLWINDOW
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0
        };
        _src = new HwndSource(p);
        _src.AddHook(WndProc);
        _displayReg = NativeMethods.RegisterPowerSettingNotification(_src.Handle, ref _guidDisplayState, 0);
        NativeMethods.WTSRegisterSessionNotification(_src.Handle, 0);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_POWERBROADCAST:
                int pbt = wParam.ToInt32();
                if (pbt == PBT_APMSUSPEND) BlockChanged?.Invoke(Block.Sleep, true);
                else if (pbt == PBT_APMRESUMESUSPEND || pbt == PBT_APMRESUMEAUTOMATIC) BlockChanged?.Invoke(Block.Sleep, false);
                else if (pbt == PBT_POWERSETTINGCHANGE && lParam != IntPtr.Zero)
                {
                    var s = Marshal.PtrToStructure<NativeMethods.POWERBROADCAST_SETTING>(lParam);
                    if (s.PowerSetting == _guidDisplayState)
                        BlockChanged?.Invoke(Block.DisplayOff, s.Data == 0); // 0 = off, 1 = on, 2 = dimmed
                }
                break;
            case WM_WTSSESSION_CHANGE:
                int code = wParam.ToInt32();
                if (code == WTS_SESSION_LOCK) BlockChanged?.Invoke(Block.Locked, true);
                else if (code == WTS_SESSION_UNLOCK) BlockChanged?.Invoke(Block.Locked, false);
                break;
            case WM_TIMECHANGE:
                TimeChanged?.Invoke();
                break;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_src == null) return;
        try
        {
            if (_displayReg != IntPtr.Zero) NativeMethods.UnregisterPowerSettingNotification(_displayReg);
            NativeMethods.WTSUnRegisterSessionNotification(_src.Handle);
        }
        catch { }
        _src.Dispose();
        _src = null;
    }
}
