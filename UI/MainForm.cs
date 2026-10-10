using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using ExaltHUD.Capture;
using ExaltHUD.Data;
using ExaltHUD.Models;
using ExaltHUD.Tracking;

namespace ExaltHUD;

internal sealed class MainForm : Form
{
    private readonly ComboBox adapters = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Button refresh = new Button { Text = "Refresh", Dock = DockStyle.Fill };
    private readonly Button listen = new Button { Text = "Start listening", Dock = DockStyle.Fill, Enabled = false };
    private readonly CheckBox overlayToggle = new CheckBox { Text = "Enable overlay (F8)", AutoSize = true };
    private readonly CheckBox lockToggle = new CheckBox { Text = "Click through (F9)", AutoSize = true };
    private readonly Label status = new Label { Text = "Loading game data...", Dock = DockStyle.Fill, ForeColor = Color.LightSkyBlue };
    private readonly DpsOverlayForm dashboard = new DpsOverlayForm(true) { Dock = DockStyle.Fill };
    private readonly DpsOverlayForm overlay = new DpsOverlayForm(false);
    private readonly CaptureService capture = new CaptureService();
    private readonly TrackingSession session = new TrackingSession();
    private readonly Timer timer = new Timer { Interval = 50 };
    private readonly string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExaltHUD.settings.xml");
    private bool ready;
    private bool busy;
    private bool synchronizing;
    private bool captureStarted;
    private bool cleanedUp;
    private string savedAdapter;
    private CaptureStatus lastStatus;

    public MainForm()
    {
        Text = "ExaltHUD Standalone";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        BackColor = Color.FromArgb(14, 15, 20);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(412, 760);
        MinimumSize = new Size(428, 570);
        MaximumSize = new Size(428, 32767);
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 8), ColumnCount = 1, RowCount = 8 };
        foreach (int height in new[] { 28, 30, 36, 30, 50, 38 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.Controls.Add(new Label { Text = "EXALTHUD", Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = Color.FromArgb(0, 210, 255), AutoSize = true }, 0, 0);
        var networkRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        networkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        networkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        networkRow.Controls.Add(adapters, 0, 0);
        networkRow.Controls.Add(refresh, 1, 0);
        layout.Controls.Add(networkRow, 0, 1);
        layout.Controls.Add(listen, 0, 2);
        var toggles = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        toggles.Controls.Add(overlayToggle);
        toggles.Controls.Add(lockToggle);
        layout.Controls.Add(toggles, 0, 3);
        layout.Controls.Add(status, 0, 4);
        layout.Controls.Add(new Label { Text = "Start listening before connecting. Use windowed or borderless mode for the overlay.", Dock = DockStyle.Fill, ForeColor = Color.Silver }, 0, 5);
        var hudPanel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        hudPanel.Controls.Add(dashboard);
        layout.Controls.Add(hudPanel, 0, 6);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var help = new LinkLabel { Text = "Setup / help", AutoSize = true, LinkColor = Color.LightSkyBlue };
        help.LinkClicked += (_, __) => ShowHelp();
        footer.Controls.Add(help);
        footer.Controls.Add(new Label { Text = "v" + typeof(MainForm).Assembly.GetName().Version.ToString(3), AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(18, 0, 0, 0) });
        layout.Controls.Add(footer, 0, 7);
        Controls.Add(layout);
        foreach (Button button in new[] { refresh, listen })
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Color.FromArgb(30, 36, 48);
        }
        ReadSettings();
        dashboard.CaptureStatusText = overlay.CaptureStatusText = "Start listening to track the game";
        DpsOverlayManager.Attach(dashboard, overlay);
        dashboard.Show();
        overlay.OverlayPreferenceChanged += (_, __) => SynchronizeToggles();
        overlay.ClickThroughChanged += (_, __) => SynchronizeToggles();
        overlayToggle.CheckedChanged += (_, __) => { if (!synchronizing) overlay.SetOverlayEnabled(overlayToggle.Checked); };
        lockToggle.CheckedChanged += (_, __) => { if (!synchronizing) overlay.SetClickThrough(lockToggle.Checked); };
        refresh.Click += async (_, __) => await RefreshAdapters();
        listen.Click += async (_, __) => await ToggleListening();
        timer.Tick += (_, __) => DrainCapture();
        Shown += async (_, __) => await InitializeData();
        FormClosing += (_, __) => SaveSettings();
    }

    private async Task InitializeData()
    {
        try
        {
            await Task.Run(() => { GameData.LoadGameData(); AbilityScalingManager.EnsureLoaded(); RealmSharkData.EnsureLoaded(); ItemSpriteManager.InitializeAssets(); });
            if (IsDisposed) return;
            ready = true;
            await RefreshAdapters();
            if (!IsDisposed) timer.Start();
        }
        catch (Exception ex) { if (!IsDisposed) status.Text = "Cannot load game data: " + ex.Message + " Extract the entire release folder."; }
    }

    private async Task RefreshAdapters()
    {
        if (busy || capture.IsRunning) return;
        busy = true;
        refresh.Enabled = listen.Enabled = false;
        string selected = (adapters.SelectedItem as CaptureDevice)?.Name ?? savedAdapter;
        try
        {
            var devices = await Task.Run(NpcapNative.GetDevices);
            if (IsDisposed) return;
            adapters.Items.Clear();
            foreach (var device in devices) adapters.Items.Add(device);
            if (adapters.Items.Count > 0)
            {
                adapters.SelectedIndex = 0;
                for (int i = 0; i < adapters.Items.Count; i++) if (((CaptureDevice)adapters.Items[i]).Name == selected) adapters.SelectedIndex = i;
                status.Text = "Ready. Start listening, then connect to the game.";
            }
            else status.Text = "No capture adapters found. Check your Npcap installation.";
        }
        catch (Exception ex) { if (!IsDisposed) status.Text = ex.Message; }
        finally
        {
            busy = false;
            if (!IsDisposed) { refresh.Enabled = true; listen.Enabled = ready && adapters.Items.Count > 0; }
        }
    }

    private async Task ToggleListening()
    {
        if (busy) return;
        busy = true;
        listen.Enabled = false;
        try
        {
            if (capture.IsRunning) await Task.Run(capture.Stop);
            else if (adapters.SelectedItem is CaptureDevice device)
            {
                capture.Start(device);
                captureStarted = true;
            }
            lastStatus = null;
        }
        catch (Exception ex) { if (!IsDisposed) status.Text = ex.Message; }
        finally { busy = false; if (!IsDisposed) listen.Enabled = ready && adapters.Items.Count > 0; }
    }

    private void DrainCapture()
    {
        capture.ConsumeOverflow();
        var budget = Stopwatch.StartNew();
        try
        {
            while (!busy && budget.ElapsedMilliseconds < 20 && capture.TryDequeue(out var item))
            {
                if (item.BeginConnection) session.BeginConnection();
                else if (item.Packet != null) session.Process(item.Packet);
            }
        }
        catch (Exception ex)
        {
            timer.Stop();
            try { capture.Stop(); } catch (InvalidOperationException) { }
            status.Text = "Tracking stopped: " + ex.Message + " Restart listening and reconnect.";
            dashboard.CaptureStatusText = overlay.CaptureStatusText = "Tracking stopped - reconnect to resume";
            lastStatus = capture.Status;
            timer.Start();
        }
        if (captureStarted && !ReferenceEquals(lastStatus, capture.Status))
        {
            lastStatus = capture.Status;
            status.Text = lastStatus.Message;
            status.ForeColor = lastStatus.State == CaptureState.Tracking ? Color.LightGreen : Color.LightSkyBlue;
            string banner = lastStatus.State == CaptureState.Tracking ? null :
                lastStatus.State == CaptureState.NeedsReconnect ? "Reconnect the game to resume tracking" :
                lastStatus.State == CaptureState.Listening || lastStatus.State == CaptureState.Connecting ? "Waiting for game data - last results retained" : "Capture stopped - last results retained";
            dashboard.CaptureStatusText = overlay.CaptureStatusText = banner;
            DpsOverlayManager.RequestRedraw();
        }
        if (!busy)
        {
            adapters.Enabled = refresh.Enabled = !capture.IsRunning;
            listen.Text = capture.IsRunning ? "Stop listening" : "Start listening";
        }
    }

    private void SynchronizeToggles()
    {
        synchronizing = true;
        overlayToggle.Checked = overlay.OverlayEnabled;
        lockToggle.Checked = overlay.ClickThroughEnabled;
        synchronizing = false;
    }

    private void ReadSettings()
    {
        try
        {
            if (!File.Exists(settingsPath)) return;
            var root = XElement.Load(settingsPath);
            savedAdapter = (string)root.Element("Adapter");
            overlay.SetClickThrough((bool?)root.Element("ClickThrough") ?? false);
            if (int.TryParse((string)root.Element("OverlayX"), out int x) && int.TryParse((string)root.Element("OverlayY"), out int y))
                overlay.RestorePosition(new Point(x, y));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException || ex is FormatException) { }
        // Visibility starts off so opening the app never unexpectedly covers the game.
        SynchronizeToggles();
    }

    private void SaveSettings()
    {
        try
        {
            new XElement("ExaltHUD", new XElement("Adapter", (adapters.SelectedItem as CaptureDevice)?.Name ?? savedAdapter ?? ""),
                new XElement("ClickThrough", overlay.ClickThroughEnabled), new XElement("OverlayX", overlay.Left), new XElement("OverlayY", overlay.Top)).Save(settingsPath);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
    }

    private void ShowHelp() => MessageBox.Show(this,
        "1. Install Npcap from https://npcap.com if needed.\n2. Select the adapter carrying your game traffic (Ethernet, Wi-Fi, or your VPN).\n3. Click Start listening, then connect/reconnect in the game.\n\nEnable overlay or press F8 to show/hide the overlay. F9 changes click-through mode; unlock it to click controls and scroll. The dashboard remains available when the overlay is hidden or locked.\n\nUse windowed or borderless mode. Exclusive fullscreen may cover the overlay. One game client is supported at a time.\n\nIf Npcap was installed for administrators only, run ExaltHUD as administrator. Keep ExaltHUD_Data beside the executable. See README.md for limitations.",
        "ExaltHUD setup", MessageBoxButtons.OK, MessageBoxIcon.Information);

    protected override void Dispose(bool disposing)
    {
        if (disposing && !cleanedUp)
        {
            cleanedUp = true;
            timer.Stop();
            timer.Dispose();
            capture.Dispose();
            DpsOverlayManager.Detach();
            session.Dispose();
            overlay.Dispose();
        }
        base.Dispose(disposing);
    }
}
