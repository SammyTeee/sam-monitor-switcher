using System.Runtime.InteropServices;
using System.Text.Json;

namespace MonitorSwitcher;
internal sealed record SavedState(Layout Layout, string TargetId);
internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "access.log"), DateTime.Now + " " + AccessDiagnostics.Read() + Environment.NewLine); } catch { }
        if (args.Contains("--test-switch"))
        {
            var log = Path.Combine(AppContext.BaseDirectory, "test-switch.log");
            void Log(string text) => File.AppendAllText(log, DateTime.Now + " " + text + Environment.NewLine);
            try
            {
                var original = Displays.Read();
                var candidates = Enumerable.Range(0, original.Count).Where(i => Displays.Target(original, i).Name.Contains("LG", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (candidates.Length != 1 || original.Count < 2) throw new InvalidOperationException("Expected one LG plus another active display.");
                string id = Displays.Target(original, candidates[0]).Id;
                string recovery = Path.Combine(AppContext.BaseDirectory, "saved-layout.json");
                if (File.Exists(recovery)) throw new InvalidOperationException("Restore the pending saved layout before testing.");
                File.WriteAllText(recovery, JsonSerializer.Serialize(new SavedState(original, id)));
                try
                {
                    Log("Disconnecting LG");
                    Displays.Apply(original.Without(candidates[0]));
                    var off = Displays.Read();
                    if (Enumerable.Range(0, off.Count).Any(i => Displays.Target(off, i).Id == id)) throw new InvalidOperationException("LG still active.");
                    Log("PASS: LG disconnected; " + off.Count + " displays remain active.");
                }
                finally
                {
                    Log("Restoring original layout");
                    Displays.Apply(original);
                    var restored = Displays.Read();
                    if (!Enumerable.Range(0, restored.Count).Any(i => Displays.Target(restored, i).Id == id)) throw new InvalidOperationException("LG was not restored.");
                    File.Delete(recovery);
                    Log("PASS: LG restored; " + restored.Count + " displays active.");
                }
            }
            catch (Exception ex) { Log(ex.ToString()); Environment.ExitCode = 1; }
            return;
        }
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
    GlobalShortcut? shortcut;
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
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try { shortcut = new GlobalShortcut(Handle); Report("Ready: Left Ctrl + Right Alt + Enter"); }
        catch (Exception ex) { Report(ex.Message, true); }
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x312 && message.WParam == (IntPtr)GlobalShortcut.Id)
        {
            Report("Hotkey received. Switching LG...");
            Toggle();
        }
        base.WndProc(ref message);
    }
    async void Toggle()
    {
        if (busy) return;
        busy = true;
        status.Text = "Switching LG monitor...";
        try { await Task.Run(ToggleCore); }
        finally { busy = false; }
    }
    void ToggleCore()
    {
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
    }
    void Report(string message, bool error = false)
    {
        if (InvokeRequired) { BeginInvoke((Action)(() => Report(message, error))); return; }
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "switcher.log"), DateTime.Now + " " + message + Environment.NewLine); } catch { }
        status.Text = message + "\n\nShortcut: Left Ctrl + Right Alt + Enter";
        tray.ShowBalloonTip(4000, Text, message, error ? ToolTipIcon.Error : ToolTipIcon.Info);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing) { if (disposing) { shortcut?.Dispose(); tray.Dispose(); } base.Dispose(disposing); }
}










