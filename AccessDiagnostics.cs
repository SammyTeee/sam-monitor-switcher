using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
namespace MonitorSwitcher;
internal static class AccessDiagnostics
{
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll")] static extern bool IsTokenRestricted(IntPtr token);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("user32.dll")] static extern IntPtr GetProcessWindowStation();
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool GetUserObjectInformationW(IntPtr obj, int index, StringBuilder value, uint length, out uint needed);
    static string Name(IntPtr obj)
    {
        var value = new StringBuilder(256);
        return GetUserObjectInformationW(obj, 2, value, 512, out _) ? value.ToString() : "unavailable";
    }
    public static string Read()
    {
        bool restricted = false;
        if (OpenProcessToken(Process.GetCurrentProcess().Handle, 8, out var token))
        { try { restricted = IsTokenRestricted(token); } finally { CloseHandle(token); } }
        return $"Restricted token: {restricted}; window station: {Name(GetProcessWindowStation())}; desktop: {Name(GetThreadDesktop(GetCurrentThreadId()))}";
    }
}
