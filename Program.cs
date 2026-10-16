using System.Runtime.InteropServices;
using System.Text.Json;

namespace MonitorSwitcher;
internal sealed record SavedState(Layout Layout, string TargetId);
internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--diagnose"))
        {
            var layout = Displays.Read();
            var targets = Enumerable.Range(0, layout.Count).Select(i => Displays.Target(layout, i)).Select(t => new { t.Name, t.Id });
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "display-check.json"), JsonSerializer.Serialize(targets, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        using var mutex = new Mutex(true, "SamMonitorSwitcher.SingleInstance", out bool first);
        if (!first) return;
        Application.Run(new Switcher());
    }
}
internal sealed class Switcher : Form
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    readonly string stateFile = Path.Combine(AppContext.BaseDirectory, "saved-layout.json");
    readonly NotifyIcon tray;
    readonly Label status = new() { AutoSize = false, Dock = DockStyle.Fill, Padding = new Padding(16) };
    bool exiting, busy;
    public Switcher()
    {
        Text = "LG Monitor Switcher"; ClientSize = new Size(460, 180); StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(status);
        var toggle = new Button { Text = "Toggle LG monitor", Dock = DockStyle.Bottom, Height = 40 };
        toggle.Click += (_, _) => Toggle(); Controls.Add(toggle);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Toggle LG monitor", null, (_, _) => Toggle());
        menu.Items.Add("Show", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        menu.Items.Add("Exit (keeps current display layout)", null, (_, _) => { exiting = true; Close(); });
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "LG: Left Ctrl + Right Alt + Enter", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => { Show(); Activate(); };
        status.Text = "Left Ctrl + Right Alt + Enter toggles your LG monitor.\n\nClosing this window keeps the hotkey running in the system tray.\nSwitch the monitor's input to Xbox separately.";
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!RegisterHotKey(Handle, 1, 0x4000 | 1 | 2, 13))
            MessageBox.Show(this, "Could not register Ctrl + Alt + Enter. Another app may already use it. Exit that app and restart this one.", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
    protected override void OnHandleDestroyed(EventArgs e) { UnregisterHotKey(Handle, 1); base.OnHandleDestroyed(e); }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x312 && m.WParam == (IntPtr)1 && GetAsyncKeyState(0xA2) < 0 && GetAsyncKeyState(0xA5) < 0)
            BeginInvoke((Action)Toggle);
        base.WndProc(ref m);
    }
    void Toggle()
    {
        if (busy) return;
        busy = true;
        try
        {
            var current = Displays.Read();
            var targets = Enumerable.Range(0, current.Count).Select(i => Displays.Target(current, i)).ToArray();
            SavedState? saved = File.Exists(stateFile) ? JsonSerializer.Deserialize<SavedState>(File.ReadAllText(stateFile)) : null;
            if (saved != null && !targets.Any(t => t.Id == saved.TargetId))
            {
                Displays.Apply(saved.Layout);
                var restored = Displays.Read();
                if (!Enumerable.Range(0, restored.Count).Select(i => Displays.Target(restored, i)).Any(t => t.Id == saved.TargetId))
                    throw new InvalidOperationException("Windows did not reconnect the LG. Check its PC cable and input, then try again.");
                File.Delete(stateFile);
                Report("LG reconnected. Your original display layout is restored.");
                return;
            }
            var candidates = Enumerable.Range(0, targets.Length).Where(i => targets[i].Name.Contains("LG", StringComparison.OrdinalIgnoreCase) || targets[i].Id.Contains("GSM", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length != 1) throw new InvalidOperationException($"Expected one LG monitor, found {candidates.Length}. Active monitors: {string.Join(", ", targets.Select(t => t.Name))}");
            if (current.Count < 2) throw new InvalidOperationException("Connect another active PC display before disconnecting the LG.");
            int target = candidates[0];
            // Persist BEFORE disconnecting so restoring still works after a restart.
            string temp = stateFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new SavedState(current, targets[target].Id)));
            File.Move(temp, stateFile, true);
            try
            {
                Displays.Apply(current.Without(target));
                var after = Displays.Read();
                if (Enumerable.Range(0, after.Count).Any(i => Displays.Target(after, i).Id == targets[target].Id))
                    throw new InvalidOperationException("Windows did not disconnect the LG.");
            }
            catch
            {
                Displays.Apply(current); // Roll back a partial or rejected change.
                throw;
            }
            Report("LG disconnected from Windows. Press the same shortcut to reconnect.");
        }
        catch (Exception ex) { Report("Could not switch: " + ex.Message, true); }
        finally { busy = false; }
    }
    void Report(string message, bool error = false)
    {
        status.Text = message + "\n\nShortcut: Left Ctrl + Right Alt + Enter";
        tray.ShowBalloonTip(4000, Text, message, error ? ToolTipIcon.Error : ToolTipIcon.Info);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing) { if (disposing) tray.Dispose(); base.Dispose(disposing); }
}



