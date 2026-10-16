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
    public Layout Without(int index)
    {
        if (index < 0 || index >= Count || Count < 2) throw new ArgumentOutOfRangeException(nameof(index));
        byte[] paths = Paths.Take(index * 72).Concat(Paths.Skip((index + 1) * 72)).ToArray();
        var modes = new List<byte>();
        var remap = new Dictionary<uint, uint>();
        for (int path = 0; path < paths.Length / 72; path++)
        {
            foreach (int field in new[] { 12, 32 })
            {
                int offset = path * 72 + field;
                uint oldIndex = BitConverter.ToUInt32(paths, offset);
                if (oldIndex == uint.MaxValue) continue;
                if (oldIndex >= Modes.Length / 64) throw new InvalidOperationException("Invalid display mode index.");
                if (!remap.TryGetValue(oldIndex, out uint newIndex))
                {
                    newIndex = (uint)modes.Count / 64;
                    remap.Add(oldIndex, newIndex);
                    modes.AddRange(Modes.Skip((int)oldIndex * 64).Take(64));
                }
                BitConverter.GetBytes(newIndex).CopyTo(paths, offset);
            }
        }
        byte[] modeArray = modes.ToArray();
        var sources = Enumerable.Range(0, modeArray.Length / 64)
            .Where(i => BitConverter.ToUInt32(modeArray, i * 64) == 1)
            .OrderBy(i => BitConverter.ToInt32(modeArray, i * 64 + 28)).ToArray();
        if (sources.Length == 0) throw new InvalidOperationException("No remaining desktop display.");
        int primary = sources.FirstOrDefault(i => BitConverter.ToInt32(modeArray, i * 64 + 28) == 0 && BitConverter.ToInt32(modeArray, i * 64 + 32) == 0, -1);
        if (primary < 0) primary = (int)BitConverter.ToUInt32(paths, 12);
        // Removing the primary must leave a source at (0,0). Removing a middle
        // monitor must close its gap. Keep left-to-right order while disconnected.
        int origin = 0;
        foreach (int source in sources)
        {
            if (source == primary) break;
            origin = checked(origin + (int)BitConverter.ToUInt32(modeArray, source * 64 + 16));
        }
        int x = -origin;
        foreach (int source in sources)
        {
            BitConverter.GetBytes(x).CopyTo(modeArray, source * 64 + 28);
            BitConverter.GetBytes(0).CopyTo(modeArray, source * 64 + 32);
            x = checked(x + (int)BitConverter.ToUInt32(modeArray, source * 64 + 16));
        }
        return new(paths, modeArray);
    }
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
        Validate(layout);
        Check(SetDisplayConfig((uint)layout.Count, layout.Paths, (uint)layout.Modes.Length / 64, layout.Modes, 0x80 | 0x20 | 0x400));
    }
    public static void Validate(Layout layout)
    {
        int result = SetDisplayConfig((uint)layout.Count, layout.Paths, (uint)layout.Modes.Length / 64, layout.Modes, 0x40 | 0x20 | 0x400);
        if (result != 0) throw new Win32Exception(result, "Display configuration validation failed: " + new Win32Exception(result).Message);
    }
    static void Check(int result) { if (result != 0) throw new Win32Exception(result); }
}



