using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace MonitorSwitcher;

// Native DISPLAYCONFIG_PATH_INFO and DISPLAYCONFIG_MODE_INFO have fixed sizes
// of 72 and 64 bytes. Preserve the complete driver supplied structures.
internal sealed record Layout(byte[] Paths, byte[] Modes)
{
    public int Count => Paths.Length / 72;
    public Layout Without(int index) => new(Paths.Take(index * 72).Concat(Paths.Skip((index + 1) * 72)).ToArray(), Modes);
}
internal static class Displays
{
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] byte[] pathData, ref uint modes, [Out] byte[] modeData, IntPtr topology);
    [DllImport("user32.dll")] static extern int SetDisplayConfig(uint paths, byte[] pathData, uint modes, byte[] modeData, uint flags);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo([In, Out] byte[] packet);
    public static Layout Read()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Check(GetDisplayConfigBufferSizes(2, out var pc, out var mc));
            var p = new byte[pc * 72]; var m = new byte[mc * 64];
            int result = QueryDisplayConfig(2, ref pc, p, ref mc, m, IntPtr.Zero);
            if (result == 122) continue;
            Check(result);
            return new(p.Take((int)pc * 72).ToArray(), m.Take((int)mc * 64).ToArray());
        }
        throw new InvalidOperationException("Displays kept changing. Please try again.");
    }
    public static (string Name, string Id) Target(Layout layout, int index)
    {
        var packet = new byte[420];
        BitConverter.GetBytes(2).CopyTo(packet, 0);
        BitConverter.GetBytes(420).CopyTo(packet, 4);
        Array.Copy(layout.Paths, index * 72 + 20, packet, 8, 12);
        Check(DisplayConfigGetDeviceInfo(packet));
        return (Encoding.Unicode.GetString(packet, 36, 128).TrimEnd('\0'), Encoding.Unicode.GetString(packet, 164, 256).TrimEnd('\0'));
    }
    public static void Apply(Layout layout)
    {
        if (layout.Count < 1 || layout.Paths.Length % 72 != 0 || layout.Modes.Length % 64 != 0)
            throw new InvalidOperationException("Invalid saved display layout.");
        // Validate before changing anything; allow Windows to resolve mode conflicts.
        Check(SetDisplayConfig((uint)layout.Count, layout.Paths, (uint)layout.Modes.Length / 64, layout.Modes, 0x40 | 0x20 | 0x400));
        Check(SetDisplayConfig((uint)layout.Count, layout.Paths, (uint)layout.Modes.Length / 64, layout.Modes, 0x80 | 0x20 | 0x400 | 0x200));
    }
    static void Check(int result) { if (result != 0) throw new Win32Exception(result); }
}
