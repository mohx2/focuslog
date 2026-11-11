using System;

namespace FocusLog.Native;

/// <summary>Event-driven foreground detection (no polling): foreground change + minimize/restore events.</summary>
internal sealed class ForegroundHook : IDisposable
{
    private readonly NativeMethods.WinEventDelegate _proc; // keep a reference so the GC cannot collect it
    private IntPtr _hookForeground;
    private IntPtr _hookMinimize;

    public event Action? Changed;

    public ForegroundHook()
    {
        _proc = OnEvent;
        _hookForeground = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _proc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
        _hookMinimize = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_MINIMIZESTART, NativeMethods.EVENT_SYSTEM_MINIMIZEEND,
            IntPtr.Zero, _proc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
    }

    private void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (evt != NativeMethods.EVENT_SYSTEM_FOREGROUND && idObject != 0) return; // only window-level events
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_hookForeground != IntPtr.Zero) { NativeMethods.UnhookWinEvent(_hookForeground); _hookForeground = IntPtr.Zero; }
        if (_hookMinimize != IntPtr.Zero) { NativeMethods.UnhookWinEvent(_hookMinimize); _hookMinimize = IntPtr.Zero; }
    }
}
