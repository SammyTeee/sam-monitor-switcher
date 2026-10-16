using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MonitorSwitcher;

internal sealed class GlobalShortcut : IDisposable
{
    delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookExW(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string? name);
    readonly HookProc callback; // Keep the native callback alive until unhooked.
    readonly Action pressed;
    IntPtr hook;
    bool enterDown, consumed;
    public GlobalShortcut(Action pressed)
    {
        this.pressed = pressed;
        callback = Handle;
        hook = SetWindowsHookExW(13, callback, GetModuleHandleW(null), 0);
        if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the global keyboard shortcut.");
    }
    IntPtr Handle(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && Marshal.ReadInt32(data) == 13)
        {
            int msg = (int)message;
            if (msg == 0x100 || msg == 0x104)
            {
                if (!enterDown)
                {
                    enterDown = true;
                    // Ctrl and Alt have already been processed when Enter arrives.
                    consumed = GetAsyncKeyState(0xA2) < 0 && GetAsyncKeyState(0xA5) < 0;
                    if (consumed) pressed(); // Queue work; never change displays in the hook.
                }
                if (consumed) return (IntPtr)1;
            }
            else if (msg == 0x101 || msg == 0x105)
            {
                enterDown = false;
                bool suppress = consumed;
                consumed = false;
                if (suppress) return (IntPtr)1;
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }
    public void Dispose() { if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } }
}
