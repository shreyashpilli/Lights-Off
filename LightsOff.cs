using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Lights Off")]
[assembly: AssemblyDescription("Keep Windows awake while displays enter standby")]
[assembly: AssemblyCompany("Lights Off")]
[assembly: AssemblyProduct("Lights Off")]
[assembly: AssemblyVersion("0.2.0.0")]
[assembly: AssemblyFileVersion("0.2.0.0")]

internal static class NativeMethods
{
    internal const uint EsContinuous = 0x80000000;
    internal const uint EsSystemRequired = 0x00000001;
    internal const uint WmSysCommand = 0x0112;
    internal const uint SmtoAbortIfHung = 0x0002;
    internal const int WmHotkey = 0x0312;
    internal const uint ModAlt = 0x0001;
    internal const uint ModControl = 0x0002;
    internal static readonly IntPtr HwndBroadcast = new IntPtr(0xffff);
    internal static readonly IntPtr ScMonitorPower = new IntPtr(0xF170);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint SetThreadExecutionState(uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [DllImport("dwmapi.dll", PreserveSig = true)]
    internal static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int value, int valueSize);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    internal static bool RequestDisplaysStandby()
    {
        IntPtr result;
        IntPtr sent = SendMessageTimeout(HwndBroadcast, WmSysCommand, ScMonitorPower,
            new IntPtr(2), SmtoAbortIfHung, 1500, out result);
        return sent != IntPtr.Zero;
    }

    internal static void EnablePerMonitorDpiAwareness()
    {
        try
        {
            // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
            SetProcessDpiAwarenessContext(new IntPtr(-4));
        }
        catch (EntryPointNotFoundException)
        {
            SetProcessDPIAware();
        }
    }
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && String.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
        {
            uint state = NativeMethods.SetThreadExecutionState(
                NativeMethods.EsContinuous | NativeMethods.EsSystemRequired);
            if (state == 0)
            {
                Console.Error.WriteLine("SetThreadExecutionState failed: " + Marshal.GetLastWin32Error());
                return 1;
            }

            NativeMethods.SetThreadExecutionState(NativeMethods.EsContinuous);
            Console.WriteLine("Lights Off startup and Windows power APIs are available.");
            return 0;
        }

        bool createdNew;
        bool legacyCreatedNew;
        using (Mutex mutex = new Mutex(true, "Local\\LightsOff.SingleInstance", out createdNew))
        using (Mutex legacyMutex = new Mutex(true, "Local\\Sleepwalk.SingleInstance", out legacyCreatedNew))
        {
            // Share the old mutex during the rename to prevent both app versions running together.
            if (!createdNew || !legacyCreatedNew)
                return 0;

            NativeMethods.EnablePerMonitorDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LightsOffForm());
        }
        return 0;
    }
}

internal sealed class LightsOffForm : Form
{
    private const int HotkeyMonitorStandby = 1;
    private const int HotkeyToggleKeepAwake = 2;
    private uint monitorModifiers = NativeMethods.ModControl | NativeMethods.ModAlt;
    private Keys monitorKey = Keys.M;
    private uint awakeModifiers = NativeMethods.ModControl | NativeMethods.ModAlt;
    private Keys awakeKey = Keys.K;
    private bool monitorHotkeyRegistered;
    private bool awakeHotkeyRegistered;
    private readonly ActionButton displayButton;
    private readonly ActionButton stopButton;
    private readonly ActionButton customStartButton;
    private readonly ActionButton preset30Button;
    private readonly ActionButton preset60Button;
    private readonly ActionButton preset120Button;
    private readonly Label status;
    private readonly Label statusDot;
    private readonly DarkSwitch keepAwakeToggle;
    private readonly TextBox customMinutes;
    private readonly Icon normalSleepIcon;
    private readonly Icon keepAwakeIcon;
    private readonly NotifyIcon trayIcon;
    private readonly System.Windows.Forms.Timer offTimer;
    private readonly System.Windows.Forms.Timer sessionTimer;
    private readonly System.Windows.Forms.Timer pulseTimer;
    private ToolStripMenuItem sessionMenuItem;
    private DateTime? sessionDeadline;
    private int? sessionPresetMinutes;
    private bool active;
    private bool exiting;
    private bool displayRequestFailed;
    private bool pulseBright;

    internal LightsOffForm()
    {
        normalSleepIcon = LoadIconResource("LightsOff.TrayNormal.ico");
        keepAwakeIcon = LoadIconResource("LightsOff.TrayAwake.ico");
        Text = "Lights Off";
        Icon = normalSleepIcon;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 548);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        Font = new Font("Segoe UI", 9.5F);
        BackColor = Color.FromArgb(18, 19, 22);
        ForeColor = Color.FromArgb(237, 239, 244);

        NightGlyph brandGlyph = new NightGlyph {
            Location = new Point(20, 20),
            Size = new Size(30, 30)
        };
        Controls.Add(brandGlyph);
        Label title = new Label {
            Text = "Lights Off",
            Font = new Font("Segoe UI", 17F, FontStyle.Bold),
            ForeColor = Color.FromArgb(242, 243, 247),
            Location = new Point(60, 18),
            Size = new Size(220, 34)
        };
        Controls.Add(title);

        RoundedPanel displayCard = new RoundedPanel {
            Location = new Point(18, 64),
            Size = new Size(484, 132),
            BackColor = Color.FromArgb(30, 32, 40),
            SurroundingColor = Color.FromArgb(18, 19, 22)
        };
        Controls.Add(displayCard);
        Label displayHeading = new Label {
            Text = "MONITORS OFF",
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(165, 170, 184),
            Location = new Point(18, 13),
            Size = new Size(200, 22)
        };
        displayCard.Controls.Add(displayHeading);

        ToolTip trayHint = new ToolTip { AutoPopDelay = 9000, InitialDelay = 300, ReshowDelay = 100 };
        ActionButton infoButton = new ActionButton {
            Text = "i",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(190, 194, 206),
            AccentColor = Color.FromArgb(43, 46, 57),
            HoverColor = Color.FromArgb(61, 65, 78),
            SurfaceColor = Color.FromArgb(30, 32, 40),
            Location = new Point(438, 10),
            Size = new Size(28, 28),
            TabStop = false,
            IsPill = true
        };
        trayHint.SetToolTip(infoButton, "Tip: left-click the Lights Off tray icon to turn off monitors.");
        displayCard.Controls.Add(infoButton);

        displayButton = new ActionButton {
            Text = "Turn monitors off now",
            ShowMonitorIcon = true,
            AccentColor = Color.FromArgb(99, 102, 241),
            HoverColor = Color.FromArgb(119, 122, 255),
            UseAccent = true,
            ForeColor = Color.White,
            SurfaceColor = Color.FromArgb(30, 32, 40),
            Font = new Font("Segoe UI Semibold", 11F),
            Location = new Point(16, 49),
            Size = new Size(452, 64)
        };
        displayButton.Click += delegate { RequestDisplaysStandby(true); };
        displayCard.Controls.Add(displayButton);

        RoundedPanel awakeCard = new RoundedPanel {
            Location = new Point(18, 212),
            Size = new Size(484, 300),
            BackColor = Color.FromArgb(30, 32, 40),
            SurroundingColor = Color.FromArgb(18, 19, 22)
        };
        Controls.Add(awakeCard);
        Label awakeHeading = new Label {
            Text = "Keep PC awake",
            Font = new Font("Segoe UI", 13F, FontStyle.Bold),
            ForeColor = Color.FromArgb(242, 243, 247),
            Location = new Point(18, 14),
            Size = new Size(250, 26)
        };
        awakeCard.Controls.Add(awakeHeading);
        keepAwakeToggle = new DarkSwitch {
            Location = new Point(420, 14),
            Size = new Size(44, 26),
            OnColor = Color.FromArgb(245, 158, 11)
        };
        keepAwakeToggle.CheckedChanged += OnKeepAwakeToggleChanged;
        awakeCard.Controls.Add(keepAwakeToggle);
        Label awakeCaption = new Label {
            Text = "Override Windows sleep while your work runs",
            ForeColor = Color.FromArgb(165, 170, 184),
            Location = new Point(18, 44),
            Size = new Size(400, 22)
        };
        awakeCard.Controls.Add(awakeCaption);

        preset30Button = AddPresetButton(awakeCard, "30 min", 18, 78, 30);
        preset60Button = AddPresetButton(awakeCard, "1 hour", 161, 78, 60);
        preset120Button = AddPresetButton(awakeCard, "2 hours", 304, 78, 120);

        Label customLabel = new Label {
            Text = "Custom duration",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(194, 198, 208),
            Location = new Point(18, 132),
            Size = new Size(160, 21)
        };
        awakeCard.Controls.Add(customLabel);
        RoundedPanel inputGroup = new RoundedPanel {
            Location = new Point(18, 155),
            Size = new Size(448, 42),
            BackColor = Color.FromArgb(18, 19, 22),
            BorderColor = Color.FromArgb(46, 50, 62),
            SurroundingColor = Color.FromArgb(30, 32, 40),
            Radius = 9
        };
        awakeCard.Controls.Add(inputGroup);
        customMinutes = new TextBox {
            Text = "90",
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(18, 19, 22),
            ForeColor = Color.FromArgb(242, 243, 247),
            Font = new Font("Segoe UI Semibold", 10F),
            Location = new Point(12, 12),
            Size = new Size(72, 20)
        };
        customMinutes.KeyPress += OnlyAllowDigits;
        inputGroup.Controls.Add(customMinutes);
        Label minutesLabel = new Label {
            Text = "minutes",
            ForeColor = Color.FromArgb(150, 155, 170),
            Location = new Point(83, 10),
            Size = new Size(64, 22)
        };
        inputGroup.Controls.Add(minutesLabel);
        customStartButton = new ActionButton {
            Text = "Start",
            AccentColor = Color.FromArgb(245, 158, 11),
            HoverColor = Color.FromArgb(251, 191, 36),
            UseAccent = true,
            SurfaceColor = Color.FromArgb(18, 19, 22),
            ForeColor = Color.FromArgb(30, 25, 15),
            Font = new Font("Segoe UI Semibold", 9F),
            Location = new Point(354, 5),
            Size = new Size(88, 32)
        };
        customStartButton.Click += delegate {
            int minutes;
            if (!Int32.TryParse(customMinutes.Text, out minutes) || minutes < 1 || minutes > 59999)
            {
                MessageBox.Show(this, "Enter a whole number of minutes from 1 to 59999.",
                    "Lights Off", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            StartTimedKeepAwake(minutes, null);
        };
        inputGroup.Controls.Add(customStartButton);

        RoundedPanel statusPanel = new RoundedPanel {
            Location = new Point(18, 211),
            Size = new Size(448, 68),
            BackColor = Color.FromArgb(25, 27, 34),
            BorderColor = Color.FromArgb(46, 50, 62),
            SurroundingColor = Color.FromArgb(30, 32, 40),
            Radius = 10
        };
        awakeCard.Controls.Add(statusPanel);
        statusDot = new Label {
            Text = "●",
            Font = new Font("Segoe UI Symbol", 11F),
            ForeColor = Color.FromArgb(122, 126, 140),
            Location = new Point(14, 21),
            Size = new Size(20, 24)
        };
        statusPanel.Controls.Add(statusDot);
        status = new Label {
            Text = "PC follows normal sleep settings",
            ForeColor = Color.FromArgb(177, 181, 193),
            Location = new Point(36, 19),
            Size = new Size(218, 30)
        };
        statusPanel.Controls.Add(status);
        stopButton = new ActionButton {
            Text = "Allow normal sleep",
            AccentColor = Color.FromArgb(57, 60, 72),
            HoverColor = Color.FromArgb(72, 76, 90),
            SurfaceColor = Color.FromArgb(25, 27, 34),
            ForeColor = Color.FromArgb(237, 239, 244),
            Font = new Font("Segoe UI Semibold", 8.5F),
            Location = new Point(300, 17),
            Size = new Size(132, 34),
            Enabled = false,
            Visible = false
        };
        stopButton.Click += delegate { StopSession(false, true); };
        statusPanel.Controls.Add(stopButton);

        LinkLabel shortcutsLink = new LinkLabel {
            Text = "Keyboard shortcuts…",
            LinkColor = Color.FromArgb(165, 168, 255),
            ActiveLinkColor = Color.FromArgb(190, 192, 255),
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 8.5F),
            Location = new Point(20, 520),
            Size = new Size(145, 20)
        };
        shortcutsLink.LinkClicked += delegate { ShowShortcutSettings(); };
        Controls.Add(shortcutsLink);
        Label footer = new Label {
            Text = "Monitor standby uses normal low-power mode.",
            ForeColor = Color.FromArgb(126, 131, 145),
            Font = new Font("Segoe UI", 8.5F),
            Location = new Point(170, 520),
            Size = new Size(320, 20)
        };
        Controls.Add(footer);

        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add("Open Lights Off", null, delegate { ShowWindow(); });
        menu.Items.Add("Keyboard shortcuts…", null, delegate { ShowShortcutSettings(); });
        sessionMenuItem = new ToolStripMenuItem("Keep PC awake") { CheckOnClick = true };
        sessionMenuItem.Click += OnTrayKeepAwakeClicked;
        menu.Items.Add(sessionMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, delegate { exiting = true; StopSession(false, false); Close(); });

        trayIcon = new NotifyIcon {
            Icon = normalSleepIcon,
            Text = "Lights Off is off",
            ContextMenuStrip = menu,
            Visible = true
        };
        trayIcon.MouseClick += OnTrayIconMouseClick;

        offTimer = new System.Windows.Forms.Timer { Interval = 900 };
        offTimer.Tick += OnDisplayStandbyTimer;

        sessionTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        sessionTimer.Tick += OnSessionTimer;

        pulseTimer = new System.Windows.Forms.Timer { Interval = 520 };
        pulseTimer.Tick += delegate {
            pulseBright = !pulseBright;
            statusDot.ForeColor = active
                ? (pulseBright ? Color.FromArgb(245, 158, 11) : Color.FromArgb(158, 101, 18))
                : Color.FromArgb(122, 126, 140);
        };

        FormClosing += OnFormClosing;
    }

    private ActionButton AddPresetButton(Control parent, string text, int x, int y, int minutes)
    {
        ActionButton button = new ActionButton {
            Text = text,
            AccentColor = Color.FromArgb(245, 158, 11),
            HoverColor = Color.FromArgb(251, 191, 36),
            SurfaceColor = Color.FromArgb(30, 32, 40),
            ForeColor = Color.FromArgb(237, 239, 244),
            Location = new Point(x, y),
            Size = new Size(130, 40),
            IsPill = true
        };
        button.Click += delegate { StartTimedKeepAwake(minutes, minutes); };
        parent.Controls.Add(button);
        return button;
    }

    private static void OnlyAllowDigits(object sender, KeyPressEventArgs e)
    {
        if (!Char.IsControl(e.KeyChar) && !Char.IsDigit(e.KeyChar))
            e.Handled = true;
    }

    private void OnKeepAwakeToggleChanged(object sender, EventArgs e)
    {
        if (keepAwakeToggle.Checked)
            StartKeepAwake(null);
        else if (active && !sessionDeadline.HasValue)
            StopSession(false, true);
    }

    private void OnTrayKeepAwakeClicked(object sender, EventArgs e)
    {
        if (!sessionMenuItem.Checked)
            StopSession(false, true);
        else
            StartKeepAwake(null);
    }

    private void StartKeepAwake(DateTime? deadline)
    {
        StartKeepAwake(deadline, null);
    }

    private void StartTimedKeepAwake(int minutes, int? presetMinutes)
    {
        StartKeepAwake(DateTime.Now.AddMinutes(minutes), presetMinutes);
    }

    private void StartKeepAwake(DateTime? deadline, int? presetMinutes)
    {
        if (!active)
        {
            uint state = NativeMethods.SetThreadExecutionState(
                NativeMethods.EsContinuous | NativeMethods.EsSystemRequired);
            if (state == 0)
            {
                status.Text = "Windows could not enable keep-awake mode.";
                MessageBox.Show(this,
                    "Windows could not enable keep-awake mode. Try closing and reopening Lights Off.",
                    "Lights Off", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            active = true;
        }

        displayRequestFailed = false;
        sessionDeadline = deadline;
        sessionPresetMinutes = presetMinutes;
        sessionMenuItem.Checked = true;
        keepAwakeToggle.Checked = !deadline.HasValue;
        stopButton.Enabled = true;
        stopButton.Visible = true;
        pulseBright = true;
        pulseTimer.Start();
        UpdateSessionStatus();
        UpdatePresetButtons();
        UpdateTrayText();
        offTimer.Start();
        if (sessionDeadline.HasValue)
            sessionTimer.Start();
        else
            sessionTimer.Stop();
        TopMost = false;
    }

    private void OnTrayIconMouseClick(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        RequestDisplaysStandby(false);
    }

    private void OnDisplayStandbyTimer(object sender, EventArgs e)
    {
        offTimer.Stop();
        RequestDisplaysStandby(true);
    }

    private void RequestDisplaysStandby(bool showSuccess)
    {
        displayRequestFailed = !NativeMethods.RequestDisplaysStandby();
        UpdateTrayText();
        if (displayRequestFailed)
        {
            trayIcon.ShowBalloonTip(5000, "Display standby request not confirmed",
                "Windows did not confirm the monitor standby request. Check the display connection.",
                ToolTipIcon.Warning);
        }
        else if (showSuccess)
        {
            trayIcon.ShowBalloonTip(1800, "Lights Off is active",
                "Windows accepted the request to turn off the monitors.",
                ToolTipIcon.Info);
        }
    }

    private void OnSessionTimer(object sender, EventArgs e)
    {
        if (!active || !sessionDeadline.HasValue)
            return;

        if (DateTime.Now >= sessionDeadline.Value)
        {
            StopSession(true, true);
            return;
        }

        UpdateSessionStatus();
        UpdateTrayText();
    }

    private void UpdateSessionStatus()
    {
        if (!sessionDeadline.HasValue)
        {
            status.Text = "Awake until you switch it off";
            return;
        }

        status.Text = String.Format("Awake for {0}", FormatRemaining(sessionDeadline.Value - DateTime.Now));
    }

    private void UpdateTrayText()
    {
        if (!active)
        {
            trayIcon.Text = "Lights Off is off";
            trayIcon.Icon = normalSleepIcon;
            sessionMenuItem.Checked = false;
            keepAwakeToggle.Checked = false;
            stopButton.Visible = false;
            status.Text = "PC follows normal sleep settings";
            statusDot.ForeColor = Color.FromArgb(122, 126, 140);
            UpdatePresetButtons();
            return;
        }

        sessionMenuItem.Checked = true;
        trayIcon.Icon = keepAwakeIcon;
        keepAwakeToggle.Checked = !sessionDeadline.HasValue;
        stopButton.Visible = true;
        UpdatePresetButtons();

        if (displayRequestFailed)
        {
            trayIcon.Text = "Lights Off: display request failed";
            return;
        }

        string sessionText = sessionDeadline.HasValue
            ? String.Format("Lights Off: {0} left", FormatRemaining(sessionDeadline.Value - DateTime.Now))
            : "Lights Off is active";
        trayIcon.Text = sessionText;
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        int totalMinutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        if (totalMinutes < 60)
            return String.Format("{0}m", totalMinutes);

        return String.Format("{0}h {1}m", totalMinutes / 60, totalMinutes % 60);
    }

    private void StopSession(bool sessionExpired, bool notifyUser)
    {
        if (!active)
            return;

        offTimer.Stop();
        sessionTimer.Stop();
        uint keepAwakeCleared = NativeMethods.SetThreadExecutionState(NativeMethods.EsContinuous);
        active = false;
        sessionDeadline = null;
        sessionPresetMinutes = null;
        displayRequestFailed = false;
        pulseTimer.Stop();
        stopButton.Enabled = false;
        stopButton.Visible = false;
        sessionMenuItem.Checked = false;
        keepAwakeToggle.Checked = false;
        status.Text = sessionExpired
            ? "Timer ended. Windows sleep settings are back in control."
            : "Keep-awake is off. Windows sleep settings are back in control.";
        UpdateTrayText();

        if (!notifyUser)
            return;

        if (keepAwakeCleared == 0)
        {
            trayIcon.ShowBalloonTip(5000, "Lights Off could not confirm the stop request",
                "Windows did not confirm release of the keep-awake request.",
                ToolTipIcon.Warning);
        }
        else if (sessionExpired)
        {
            trayIcon.ShowBalloonTip(3500, "Keep-awake timer ended",
                "Windows sleep settings are back in control.",
                ToolTipIcon.Info);
        }
        else
        {
            trayIcon.ShowBalloonTip(2500, "Keep-awake is off",
                "Windows sleep settings are back in control.",
                ToolTipIcon.Info);
        }
    }

    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
    }

    private void OnFormClosing(object sender, FormClosingEventArgs e)
    {
        if (!exiting && active)
        {
            e.Cancel = true;
            TopMost = false;
            Hide();
            return;
        }

        StopSession(false, false);
        NativeMethods.UnregisterHotKey(Handle, HotkeyMonitorStandby);
        NativeMethods.UnregisterHotKey(Handle, HotkeyToggleKeepAwake);
        offTimer.Dispose();
        sessionTimer.Dispose();
        pulseTimer.Dispose();
        trayIcon.Visible = false;
        trayIcon.Dispose();
        normalSleepIcon.Dispose();
        keepAwakeIcon.Dispose();
    }

    private void UpdatePresetButtons()
    {
        preset30Button.Selected = active && sessionPresetMinutes == 30;
        preset60Button.Selected = active && sessionPresetMinutes == 60;
        preset120Button.Selected = active && sessionPresetMinutes == 120;
        preset30Button.ForeColor = preset30Button.Selected ? Color.FromArgb(30, 25, 15) : Color.FromArgb(237, 239, 244);
        preset60Button.ForeColor = preset60Button.Selected ? Color.FromArgb(30, 25, 15) : Color.FromArgb(237, 239, 244);
        preset120Button.ForeColor = preset120Button.Selected ? Color.FromArgb(30, 25, 15) : Color.FromArgb(237, 239, 244);
    }

    private static Icon LoadIconResource(string resourceName)
    {
        using (System.IO.Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
        {
            if (stream == null)
                throw new InvalidOperationException("Missing embedded icon resource: " + resourceName);
            using (Icon icon = new Icon(stream))
                return (Icon)icon.Clone();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        LoadShortcutSettings();
        RegisterShortcuts();
        try
        {
            int darkMode = 1;
            int result = NativeMethods.DwmSetWindowAttribute(Handle, 20, ref darkMode, sizeof(int));
            if (result != 0)
                NativeMethods.DwmSetWindowAttribute(Handle, 19, ref darkMode, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmHotkey)
        {
            int id = message.WParam.ToInt32();
            if (id == HotkeyMonitorStandby)
                RequestDisplaysStandby(false);
            else if (id == HotkeyToggleKeepAwake)
            {
                if (active) StopSession(false, true);
                else StartKeepAwake(null);
            }
        }
        base.WndProc(ref message);
    }

    private static string ShortcutSettingsPath
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lights Off", "hotkeys.ini"); }
    }

    private void LoadShortcutSettings()
    {
        try
        {
            string[] lines = File.ReadAllLines(ShortcutSettingsPath);
            uint parsedModifiers;
            int parsedKey;
            if (lines.Length >= 4 && UInt32.TryParse(lines[0], out parsedModifiers) && Int32.TryParse(lines[1], out parsedKey))
            { monitorModifiers = parsedModifiers; monitorKey = (Keys)parsedKey; }
            if (lines.Length >= 4 && UInt32.TryParse(lines[2], out parsedModifiers) && Int32.TryParse(lines[3], out parsedKey))
            { awakeModifiers = parsedModifiers; awakeKey = (Keys)parsedKey; }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void RegisterShortcuts()
    {
        monitorHotkeyRegistered = NativeMethods.RegisterHotKey(Handle, HotkeyMonitorStandby, monitorModifiers, (uint)monitorKey);
        awakeHotkeyRegistered = NativeMethods.RegisterHotKey(Handle, HotkeyToggleKeepAwake, awakeModifiers, (uint)awakeKey);
        if (!monitorHotkeyRegistered || !awakeHotkeyRegistered)
            status.Text = "A shortcut is unavailable. Change it below.";
    }

    private void ShowShortcutSettings()
    {
        NativeMethods.UnregisterHotKey(Handle, HotkeyMonitorStandby);
        NativeMethods.UnregisterHotKey(Handle, HotkeyToggleKeepAwake);
        using (Form dialog = new Form())
        {
            dialog.Text = "Keyboard shortcuts";
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialog.MaximizeBox = false;
            dialog.MinimizeBox = false;
            dialog.ClientSize = new Size(370, 205);
            dialog.BackColor = Color.FromArgb(18, 19, 22);
            dialog.ForeColor = Color.FromArgb(237, 239, 244);

            Label hint = new Label { Text = "Click a box, then press the shortcut you want.", Location = new Point(18, 18), Size = new Size(330, 24), ForeColor = Color.FromArgb(165, 170, 184) };
            TextBox monitorBox = CreateShortcutBox(dialog, "Turn monitors off", 58, monitorModifiers, monitorKey);
            TextBox awakeBox = CreateShortcutBox(dialog, "Toggle keep-awake", 103, awakeModifiers, awakeKey);
            Button save = new Button { Text = "Save shortcuts", Location = new Point(220, 155), Size = new Size(130, 32), DialogResult = DialogResult.OK };
            dialog.Controls.Add(hint);
            dialog.Controls.Add(save);
            dialog.AcceptButton = save;
            if (dialog.ShowDialog(this) != DialogResult.OK) { RegisterShortcuts(); return; }

            uint newMonitorMods, newAwakeMods;
            Keys newMonitorKey, newAwakeKey;
            if (!TryParseShortcut(monitorBox.Tag as string, out newMonitorMods, out newMonitorKey) ||
                !TryParseShortcut(awakeBox.Tag as string, out newAwakeMods, out newAwakeKey) ||
                (newMonitorMods == newAwakeMods && newMonitorKey == newAwakeKey))
            {
                RegisterShortcuts();
                MessageBox.Show(this, "Choose two different shortcuts, each with a modifier key.", "Keyboard shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            NativeMethods.UnregisterHotKey(Handle, HotkeyMonitorStandby);
            NativeMethods.UnregisterHotKey(Handle, HotkeyToggleKeepAwake);
            bool first = NativeMethods.RegisterHotKey(Handle, HotkeyMonitorStandby, newMonitorMods, (uint)newMonitorKey);
            bool second = first && NativeMethods.RegisterHotKey(Handle, HotkeyToggleKeepAwake, newAwakeMods, (uint)newAwakeKey);
            if (!first || !second)
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyMonitorStandby);
                NativeMethods.UnregisterHotKey(Handle, HotkeyToggleKeepAwake);
                RegisterShortcuts();
                MessageBox.Show(this, "Windows could not register one of those shortcuts. It may already be used by another app.", "Keyboard shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            monitorModifiers = newMonitorMods; monitorKey = newMonitorKey;
            awakeModifiers = newAwakeMods; awakeKey = newAwakeKey;
            monitorHotkeyRegistered = awakeHotkeyRegistered = true;
            string directory = Path.GetDirectoryName(ShortcutSettingsPath);
            Directory.CreateDirectory(directory);
            File.WriteAllLines(ShortcutSettingsPath, new string[] { monitorModifiers.ToString(), ((int)monitorKey).ToString(), awakeModifiers.ToString(), ((int)awakeKey).ToString() });
        }
    }

    private TextBox CreateShortcutBox(Form dialog, string caption, int y, uint modifiers, Keys key)
    {
        Label label = new Label { Text = caption, Location = new Point(18, y + 3), Size = new Size(150, 24) };
        TextBox box = new TextBox { ReadOnly = true, Text = FormatShortcut(modifiers, key), Location = new Point(175, y), Size = new Size(175, 26), Tag = modifiers + ":" + (int)key };
        box.KeyDown += delegate(object sender, KeyEventArgs e) {
            e.SuppressKeyPress = true;
            Keys mods = e.Modifiers & (Keys.Control | Keys.Alt | Keys.Shift);
            Keys keyCode = e.KeyCode;
            if (keyCode == Keys.ControlKey || keyCode == Keys.Menu || keyCode == Keys.ShiftKey) return;
            uint nativeMods = 0;
            if ((mods & Keys.Control) != 0) nativeMods |= NativeMethods.ModControl;
            if ((mods & Keys.Alt) != 0) nativeMods |= NativeMethods.ModAlt;
            if ((mods & Keys.Shift) != 0) nativeMods |= 0x0004;
            box.Tag = nativeMods + ":" + (int)keyCode;
            box.Text = FormatShortcut(nativeMods, keyCode);
        };
        dialog.Controls.Add(label); dialog.Controls.Add(box);
        return box;
    }

    private static string FormatShortcut(uint modifiers, Keys key)
    {
        string text = String.Empty;
        if ((modifiers & NativeMethods.ModControl) != 0) text += "Ctrl+";
        if ((modifiers & NativeMethods.ModAlt) != 0) text += "Alt+";
        if ((modifiers & 0x0004) != 0) text += "Shift+";
        return text + key.ToString();
    }

    private static bool TryParseShortcut(string value, out uint modifiers, out Keys key)
    {
        modifiers = 0; key = Keys.None;
        if (String.IsNullOrEmpty(value)) return false;
        string[] parts = value.Split(':'); int parsedMods, parsedKey;
        if (parts.Length != 2 || !Int32.TryParse(parts[0], out parsedMods) || !Int32.TryParse(parts[1], out parsedKey)) return false;
        modifiers = (uint)parsedMods; key = (Keys)parsedKey;
        return modifiers != 0 && key != Keys.None;
    }
}

internal static class UiDrawing
{
    internal static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        int diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        int right = bounds.Right - diameter;
        int bottom = bounds.Bottom - diameter;
        GraphicsPath path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(right, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(right, bottom, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bottom, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class RoundedPanel : Panel
{
    internal int Radius { get; set; }
    internal Color BorderColor { get; set; }
    internal Color SurroundingColor { get; set; }

    internal RoundedPanel()
    {
        Radius = 14;
        BorderColor = Color.FromArgb(46, 50, 62);
        SurroundingColor = Color.FromArgb(18, 19, 22);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using (SolidBrush background = new SolidBrush(SurroundingColor))
            e.Graphics.FillRectangle(background, ClientRectangle);
        using (GraphicsPath path = UiDrawing.RoundedRectangle(bounds, Radius))
        using (SolidBrush brush = new SolidBrush(BackColor))
            e.Graphics.FillPath(brush, path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using (GraphicsPath path = UiDrawing.RoundedRectangle(bounds, Radius))
        using (Pen pen = new Pen(BorderColor, 1F))
            e.Graphics.DrawPath(pen, path);
    }
}

internal sealed class ActionButton : Control
{
    private bool hovering;
    internal Color AccentColor { get; set; }
    internal Color HoverColor { get; set; }
    private Color surfaceColor;
    internal Color SurfaceColor
    {
        get { return surfaceColor; }
        set { surfaceColor = value; BackColor = value; Invalidate(); }
    }
    internal bool Selected { get; set; }
    internal bool IsPill { get; set; }
    internal bool ShowMonitorIcon { get; set; }
    internal bool UseAccent { get; set; }

    internal ActionButton()
    {
        AccentColor = Color.FromArgb(57, 60, 72);
        HoverColor = Color.FromArgb(72, 76, 90);
        SurfaceColor = Color.FromArgb(30, 32, 40);
        ForeColor = Color.White;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
    }

    protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovering = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using (SolidBrush brush = new SolidBrush(SurfaceColor))
            e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        int radius = IsPill ? Height / 2 : 10;
        Color fill = Selected ? AccentColor : (hovering ? HoverColor : BackColorForButton());
        if (!Enabled) fill = Color.FromArgb(42, 44, 52);
        using (GraphicsPath path = UiDrawing.RoundedRectangle(bounds, radius))
        using (SolidBrush brush = new SolidBrush(fill))
            e.Graphics.FillPath(brush, path);

        if (ShowMonitorIcon)
        {
            SizeF textSize = e.Graphics.MeasureString(Text, Font);
            float groupWidth = textSize.Width + 36;
            float left = (Width - groupWidth) / 2F;
            float top = (Height - 20) / 2F;
            using (Pen pen = new Pen(ForeColor, 1.7F))
            {
                using (GraphicsPath iconPath = UiDrawing.RoundedRectangle(new Rectangle((int)left, (int)top, 22, 15), 3))
                    e.Graphics.DrawPath(pen, iconPath);
                e.Graphics.DrawLine(pen, left + 11, top + 15, left + 11, top + 19);
                e.Graphics.DrawLine(pen, left + 6, top + 19, left + 16, top + 19);
            }
            using (SolidBrush textBrush = new SolidBrush(Enabled ? ForeColor : Color.Gray))
                e.Graphics.DrawString(Text, Font, textBrush, left + 32, (Height - Font.Height) / 2F);
        }
        else
        {
            TextRenderer.DrawText(e.Graphics, Text, Font, bounds,
                Enabled ? ForeColor : Color.Gray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        if (Focused && ShowFocusCues)
        {
            Rectangle focus = Rectangle.Inflate(bounds, -5, -5);
            ControlPaint.DrawFocusRectangle(e.Graphics, focus, ForeColor, fill);
        }
    }

    private Color BackColorForButton()
    {
        return UseAccent ? AccentColor : Color.FromArgb(43, 46, 57);
    }
}

internal sealed class DarkSwitch : Control
{
    private bool isChecked;
    internal Color OnColor { get; set; }
    internal bool Checked
    {
        get { return isChecked; }
        set { if (isChecked != value) { isChecked = value; Invalidate(); } }
    }
    internal event EventHandler CheckedChanged;

    internal DarkSwitch()
    {
        OnColor = Color.FromArgb(245, 158, 11);
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        AccessibleRole = AccessibleRole.CheckButton;
        AccessibleName = "Keep PC awake until switched off";
    }

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        EventHandler handler = CheckedChanged;
        if (handler != null) handler(this, EventArgs.Empty);
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle track = new Rectangle(0, 2, Width - 1, Height - 5);
        using (GraphicsPath path = UiDrawing.RoundedRectangle(track, track.Height / 2))
        using (SolidBrush brush = new SolidBrush(Checked ? OnColor : Color.FromArgb(67, 70, 82)))
            e.Graphics.FillPath(brush, path);
        int knobX = Checked ? Width - Height + 2 : 2;
        Rectangle knob = new Rectangle(knobX, 4, Height - 9, Height - 9);
        using (SolidBrush brush = new SolidBrush(Color.FromArgb(248, 249, 252)))
            e.Graphics.FillEllipse(brush, knob);
        if (Focused)
            ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
    }
}

internal sealed class NightGlyph : Control
{
    internal NightGlyph()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath path = UiDrawing.RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), 8))
        using (SolidBrush bg = new SolidBrush(Color.FromArgb(41, 43, 60)))
            e.Graphics.FillPath(bg, path);
        using (Pen pen = new Pen(Color.FromArgb(165, 168, 255), 1.7F))
        {
            using (GraphicsPath iconPath = UiDrawing.RoundedRectangle(new Rectangle(6, 8, 18, 13), 2))
                e.Graphics.DrawPath(pen, iconPath);
            e.Graphics.DrawLine(pen, 15, 21, 15, 24);
            e.Graphics.DrawLine(pen, 11, 24, 19, 24);
        }
        using (SolidBrush moon = new SolidBrush(Color.FromArgb(165, 168, 255)))
        using (SolidBrush cutout = new SolidBrush(Color.FromArgb(41, 43, 60)))
        {
            e.Graphics.FillEllipse(moon, 17, 4, 9, 9);
            e.Graphics.FillEllipse(cutout, 20, 3, 8, 8);
        }
    }
}
