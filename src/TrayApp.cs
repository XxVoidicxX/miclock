using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace MicLock;

// Lives in the tray. Watches one capture device's endpoint volume and snaps it back to the
// locked level whenever anything (usually Chromium/Electron WebRTC auto gain) moves it.
sealed class TrayApp : ApplicationContext
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "MicLock";
    static readonly int[] Presets = { 50, 60, 70, 80, 90, 100 };

    readonly Settings settings = Settings.Load();
    readonly MMDeviceEnumerator enumerator = new();
    readonly SynchronizationContext ui;
    readonly NotifyIcon tray;
    readonly ContextMenuStrip menu = new();
    readonly System.Windows.Forms.Timer watchdog = new() { Interval = 1500 };
    readonly Icon iconLocked = MakeIcon(Color.FromArgb(76, 200, 120), false);
    readonly Icon iconPaused = MakeIcon(Color.FromArgb(150, 150, 150), false);
    readonly Icon iconMissing = MakeIcon(Color.FromArgb(150, 150, 150), true);

    MMDevice device;
    AudioEndpointVolume volume;
    string deviceName = "";
    int restores;
    string lastRestore = "";

    public TrayApp()
    {
        ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        menu.Opening += (_, _) => BuildMenu();
        tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true, Icon = iconPaused };
        // Left click opens the menu too. NotifyIcon only exposes this through its private ShowContextMenu.
        tray.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(tray, null);
        };

        Attach();
        watchdog.Tick += (_, _) => Watch();
        watchdog.Start();
    }

    // ---- device binding ----

    void Attach()
    {
        Detach();
        device = FindDevice();
        if (device != null)
        {
            settings.DeviceId = device.ID;
            settings.Save();
            deviceName = device.FriendlyName;
            volume = device.AudioEndpointVolume;
            volume.OnVolumeNotification += OnVolumeChanged;
            if (settings.Level < 0)
            {
                // First run: lock whatever level the mic is at right now.
                settings.Level = Percent(volume.MasterVolumeLevelScalar);
                settings.Save();
            }
            Enforce();
        }
        UpdateTray();
    }

    void Detach()
    {
        if (volume != null)
        {
            volume.OnVolumeNotification -= OnVolumeChanged;
            try { volume.Dispose(); } catch (COMException) { }
            volume = null;
        }
        device?.Dispose();
        device = null;
    }

    MMDevice FindDevice()
    {
        // A saved device that is unplugged returns null on purpose: never lock some other mic instead.
        if (!string.IsNullOrEmpty(settings.DeviceId))
        {
            try
            {
                var saved = enumerator.GetDevice(settings.DeviceId);
                if (saved.State == DeviceState.Active) return saved;
                saved.Dispose();
            }
            catch (COMException) { }
            return null;
        }

        // First run: the Windows default mic.
        return enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Console)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console)
            : null;
    }

    // ---- enforcement ----

    // Called on a COM worker thread. Hop to the UI thread so all device access stays on one thread.
    void OnVolumeChanged(AudioVolumeNotificationData data)
    {
        if (settings.Enabled && Percent(data.MasterVolume) != settings.Level)
            ui.Post(_ => Enforce(), null);
    }

    void Enforce()
    {
        if (!settings.Enabled || volume == null || settings.Level < 0) return;
        try
        {
            int current = Percent(volume.MasterVolumeLevelScalar);
            if (current == settings.Level) return;
            volume.MasterVolumeLevelScalar = settings.Level / 100f;
            restores++;
            lastRestore = $"{DateTime.Now:HH:mm:ss} ({current}% to {settings.Level}%)";
            UpdateTray();
        }
        catch (COMException)
        {
            Attach(); // device went away underneath us
        }
    }

    // Safety net for missed notifications and for a mic that gets unplugged and plugged back in.
    void Watch()
    {
        bool alive;
        try { alive = device != null && device.State == DeviceState.Active; }
        catch (COMException) { alive = false; }

        if (!alive) Attach();
        else Enforce();
    }

    static int Percent(float scalar) => (int)Math.Round(scalar * 100);

    // ---- tray ----

    void UpdateTray()
    {
        string text;
        if (device == null)
        {
            tray.Icon = iconMissing;
            text = "MicLock: mic not found";
        }
        else if (!settings.Enabled)
        {
            tray.Icon = iconPaused;
            text = $"MicLock: paused\n{deviceName}";
        }
        else
        {
            tray.Icon = iconLocked;
            text = $"MicLock: {settings.Level}%, {restores} restores\n{deviceName}";
        }
        tray.Text = text.Length > 127 ? text[..127] : text;
    }

    void BuildMenu()
    {
        menu.Items.Clear();

        menu.Items.Add(new ToolStripMenuItem(device == null ? "Mic not found" : deviceName) { Enabled = false });
        string status = !settings.Enabled ? "Paused"
            : $"Locked at {settings.Level}%, restored {restores} time{(restores == 1 ? "" : "s")}";
        menu.Items.Add(new ToolStripMenuItem(status) { Enabled = false });
        if (lastRestore != "")
            menu.Items.Add(new ToolStripMenuItem("Last: " + lastRestore) { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        var enabled = new ToolStripMenuItem("Lock enabled") { Checked = settings.Enabled };
        enabled.Click += (_, _) =>
        {
            settings.Enabled = !settings.Enabled;
            settings.Save();
            Enforce();
            UpdateTray();
        };
        menu.Items.Add(enabled);

        var level = new ToolStripMenuItem(settings.Level < 0 ? "Level (not set)" : $"Level ({settings.Level}%)");
        if (volume != null)
        {
            int now = Percent(volume.MasterVolumeLevelScalar);
            var useCurrent = new ToolStripMenuItem($"Use current level ({now}%)");
            useCurrent.Click += (_, _) => SetLevel(now);
            level.DropDownItems.Add(useCurrent);
            level.DropDownItems.Add(new ToolStripSeparator());
        }
        foreach (int p in Presets)
        {
            var item = new ToolStripMenuItem($"{p}%") { Checked = p == settings.Level };
            item.Click += (_, _) => SetLevel(p);
            level.DropDownItems.Add(item);
        }
        bool custom = settings.Level >= 0 && !Presets.Contains(settings.Level);
        var customItem = new ToolStripMenuItem(custom ? $"Custom ({settings.Level}%)..." : "Custom...") { Checked = custom };
        customItem.Click += (_, _) =>
        {
            int start = settings.Level >= 0 ? settings.Level : 80;
            if (LevelDialog.Ask(start) is int picked) SetLevel(picked);
        };
        level.DropDownItems.Add(customItem);
        menu.Items.Add(level);

        var devices = new ToolStripMenuItem("Microphone");
        foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            string id = d.ID;
            var item = new ToolStripMenuItem(d.FriendlyName) { Checked = id == settings.DeviceId };
            item.Click += (_, _) => { settings.DeviceId = id; Attach(); };
            devices.DropDownItems.Add(item);
            d.Dispose();
        }
        menu.Items.Add(devices);

        var startup = new ToolStripMenuItem("Start with Windows") { Checked = StartsWithWindows() };
        startup.Click += (_, _) => SetStartWithWindows(!startup.Checked);
        menu.Items.Add(startup);

        var panel = new ToolStripMenuItem("Open Sound recording settings");
        panel.Click += (_, _) => OpenRecordingPanel();
        menu.Items.Add(panel);

        menu.Items.Add(new ToolStripSeparator());
        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => Quit();
        menu.Items.Add(exit);
    }

    // --selftest: dump the tray menu, push the mic level off target, and check it comes back.
    public string SelfTest()
    {
        var log = new System.Text.StringBuilder();
        BuildMenu();
        foreach (ToolStripItem item in menu.Items) Dump(item, 0, log);
        log.AppendLine($"tooltip: {tray.Text.Replace('\n', '|')}");

        if (volume == null || !settings.Enabled) return log.AppendLine("restore test: skipped (no device or paused)").ToString();
        foreach (float push in new[] { 0.40f, 1.00f })
        {
            volume.MasterVolumeLevelScalar = push;
            var sw = Stopwatch.StartNew();
            while (Percent(volume.MasterVolumeLevelScalar) != settings.Level && sw.ElapsedMilliseconds < 3000)
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
            int now = Percent(volume.MasterVolumeLevelScalar);
            log.AppendLine($"restore test: pushed to {Percent(push)}%, now {now}% after {sw.ElapsedMilliseconds} ms {(now == settings.Level ? "PASS" : "FAIL")}");
        }
        return log.ToString();
    }

    static void Dump(ToolStripItem item, int depth, System.Text.StringBuilder log)
    {
        string mark = item is ToolStripMenuItem { Checked: true } ? "[x] " : "";
        log.AppendLine(new string(' ', depth * 2) + (item is ToolStripSeparator ? "----" : mark + item.Text));
        if (item is ToolStripMenuItem m)
            foreach (ToolStripItem child in m.DropDownItems) Dump(child, depth + 1, log);
    }

    public void Shutdown() => Quit();

    void SetLevel(int level)
    {
        settings.Level = level;
        settings.Save();
        Enforce();
        UpdateTray();
    }

    static bool StartsWithWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is string;
    }

    static void SetStartWithWindows(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(RunValue, false);
    }

    static void OpenRecordingPanel()
    {
        try { Process.Start(new ProcessStartInfo("control.exe", "mmsys.cpl,,1") { UseShellExecute = true }); }
        catch (Exception) { }
    }

    void Quit()
    {
        watchdog.Stop();
        Detach();
        tray.Visible = false;
        tray.Dispose();
        ExitThread();
    }

    // ---- icon ----

    static Icon MakeIcon(Color color, bool crossed)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            using var pen = new Pen(color, 3f);

            using var capsule = new GraphicsPath();
            capsule.AddArc(10, 1, 12, 12, 180, 180);
            capsule.AddArc(10, 9, 12, 12, 0, 180);
            capsule.CloseFigure();
            g.FillPath(brush, capsule);

            g.DrawArc(pen, 5, 6, 22, 19, 0, 180);
            g.DrawLine(pen, 16, 25, 16, 30);
            g.DrawLine(pen, 10, 30, 22, 30);

            if (crossed)
            {
                using var red = new Pen(Color.FromArgb(230, 70, 70), 3.5f);
                g.DrawLine(red, 4, 4, 28, 28);
            }
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
