using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MonitorSwitcher;

// Windows delivers WM_HOTKEY even after the modifier keys have been released.
// Accept either Ctrl/Alt key, including the requested Left Ctrl + Right Alt.
internal sealed class GlobalShortcut : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window, int id);
    readonly IntPtr window;
    public const int Id = 1;
    public GlobalShortcut(IntPtr window)
    {
        this.window = window;
        if (!RegisterHotKey(window, Id, 0x4000 | 1 | 2, 13))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register Ctrl + Alt + Enter. Another app may already use it.");
    }
    public void Dispose() => UnregisterHotKey(window, Id);
}
