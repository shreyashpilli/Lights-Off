using System;
using System.Drawing;
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
    internal static readonly IntPtr HwndBroadcast = new IntPtr(0xffff);
    internal static readonly IntPtr ScMonitorPower = new IntPtr(0xF170);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint SetThreadExecutionState(uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);

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
    private readonly Button displayButton;
    private readonly Button stopButton;
    private readonly Label status;
    private readonly CheckBox keepAwakeToggle;
    private readonly TextBox customMinutes;
    private readonly NotifyIcon trayIcon;
    private readonly System.Windows.Forms.Timer offTimer;
    private readonly System.Windows.Forms.Timer sessionTimer;
    private ToolStripMenuItem sessionMenuItem;
    private DateTime? sessionDeadline;
    private bool active;
    private bool exiting;
    private bool displayRequestFailed;

    internal LightsOffForm()
    {
        Text = "Lights Off";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 405);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        Font = new Font("Segoe UI", 10F);

        Label title = new Label {
            Text = "Turn monitors off. Keep your PC awake when needed.",
            Font = new Font("Segoe UI", 13F, FontStyle.Bold),
            Location = new Point(22, 20),
            Size = new Size(476, 32)
        };
        Controls.Add(title);

        Label body = new Label {
            Text = "Left-click the tray icon to turn off the monitors. Keep-awake temporarily overrides Windows sleep settings.",
            Location = new Point(24, 58),
            Size = new Size(470, 38)
        };
        Controls.Add(body);

        displayButton = new Button {
            Text = "Turn monitors off now",
            Location = new Point(24, 106),
            Size = new Size(470, 42)
        };
        displayButton.Click += delegate { RequestDisplaysStandby(true); };
        Controls.Add(displayButton);

        Label awakeHeading = new Label {
            Text = "Keep PC awake",
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            Location = new Point(24, 164),
            Size = new Size(200, 26)
        };
        Controls.Add(awakeHeading);

        keepAwakeToggle = new CheckBox {
            Text = "Until I switch it off",
            Location = new Point(24, 192),
            Size = new Size(240, 25)
        };
        keepAwakeToggle.CheckedChanged += OnKeepAwakeToggleChanged;
        Controls.Add(keepAwakeToggle);

        Label presetsLabel = new Label {
            Text = "Or choose a time:",
            Location = new Point(24, 232),
            Size = new Size(130, 24)
        };
        Controls.Add(presetsLabel);

        AddPresetButton("30 minutes", 24, 260, 30);
        AddPresetButton("1 hour", 174, 260, 60);
        AddPresetButton("2 hours", 324, 260, 120);

        Label customLabel = new Label {
            Text = "Custom:",
            Location = new Point(24, 326),
            Size = new Size(74, 28)
        };
        Controls.Add(customLabel);
        customMinutes = new TextBox {
            Text = "90",
            Location = new Point(100, 322),
            Size = new Size(72, 28)
        };
        customMinutes.KeyPress += OnlyAllowDigits;
        Controls.Add(customMinutes);
        Label minutesLabel = new Label {
            Text = "minutes",
            Location = new Point(178, 326),
            Size = new Size(66, 24)
        };
        Controls.Add(minutesLabel);
        Button customStartButton = new Button {
            Text = "Start custom",
            Location = new Point(252, 320),
            Size = new Size(140, 34)
        };
        customStartButton.Click += delegate {
            int minutes;
            if (!Int32.TryParse(customMinutes.Text, out minutes) || minutes < 1 || minutes > 59999)
            {
                MessageBox.Show(this, "Enter a whole number of minutes from 1 to 59999.",
                    "Lights Off", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            StartKeepAwake(DateTime.Now.AddMinutes(minutes));
        };
        Controls.Add(customStartButton);

        stopButton = new Button {
            Text = "Allow normal sleep",
            Location = new Point(24, 360),
            Size = new Size(180, 34),
            Enabled = false
        };
        stopButton.Click += delegate { StopSession(false, true); };
        Controls.Add(stopButton);

        status = new Label {
            Text = "Ready.",
            Location = new Point(220, 358),
            Size = new Size(270, 40)
        };
        Controls.Add(status);

        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add("Open Lights Off", null, delegate { ShowWindow(); });
        sessionMenuItem = new ToolStripMenuItem("Keep PC awake") { CheckOnClick = true };
        sessionMenuItem.Click += OnTrayKeepAwakeClicked;
        menu.Items.Add(sessionMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, delegate { exiting = true; StopSession(false, false); Close(); });

        trayIcon = new NotifyIcon {
            Icon = SystemIcons.Application,
            Text = "Lights Off is off",
            ContextMenuStrip = menu,
            Visible = true
        };
        trayIcon.MouseClick += OnTrayIconMouseClick;

        offTimer = new System.Windows.Forms.Timer { Interval = 900 };
        offTimer.Tick += OnDisplayStandbyTimer;

        sessionTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        sessionTimer.Tick += OnSessionTimer;

        FormClosing += OnFormClosing;
    }

    private void AddPresetButton(string text, int x, int y, int minutes)
    {
        Button button = new Button {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(140, 38)
        };
        button.Click += delegate { StartKeepAwake(DateTime.Now.AddMinutes(minutes)); };
        Controls.Add(button);
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
        sessionMenuItem.Checked = true;
        keepAwakeToggle.Checked = !deadline.HasValue;
        stopButton.Enabled = true;
        UpdateSessionStatus();
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
            status.Text = "PC awake. Windows sleep settings return when you switch this off.";
            return;
        }

        status.Text = String.Format("Session active. {0} remaining.",
            FormatRemaining(sessionDeadline.Value - DateTime.Now));
    }

    private void UpdateTrayText()
    {
        if (!active)
        {
            trayIcon.Text = "Lights Off is off";
            sessionMenuItem.Checked = false;
            return;
        }

        sessionMenuItem.Checked = true;

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
        displayRequestFailed = false;
        stopButton.Enabled = false;
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
        offTimer.Dispose();
        sessionTimer.Dispose();
        trayIcon.Visible = false;
        trayIcon.Dispose();
    }
}
