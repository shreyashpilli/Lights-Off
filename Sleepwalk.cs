using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Sleepwalk")]
[assembly: AssemblyDescription("Keep Windows awake while displays enter standby")]
[assembly: AssemblyCompany("Sleepwalk")]
[assembly: AssemblyProduct("Sleepwalk")]
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

    internal static bool SetDisplays(int powerState)
    {
        IntPtr result;
        IntPtr sent = SendMessageTimeout(HwndBroadcast, WmSysCommand, ScMonitorPower,
            new IntPtr(powerState), SmtoAbortIfHung, 1500, out result);
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
            NativeMethods.SetDisplays(-1); // Wake only; never turn displays off during a smoke check.
            Console.WriteLine("Sleepwalk startup and Windows power APIs are available.");
            return 0;
        }

        bool createdNew;
        using (Mutex mutex = new Mutex(true, "Local\\Sleepwalk.SingleInstance", out createdNew))
        {
            if (!createdNew)
                return 0;

            NativeMethods.EnablePerMonitorDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SleepwalkForm());
        }
        return 0;
    }
}

internal sealed class SleepwalkForm : Form
{
    private readonly Button startButton;
    private readonly Button stopButton;
    private readonly Label status;
    private readonly ComboBox durationSelector;
    private readonly NumericUpDown customHours;
    private readonly NumericUpDown customMinutes;
    private readonly Label customHoursLabel;
    private readonly Label customMinutesLabel;
    private readonly Label endTimeLabel;
    private readonly DateTimePicker endTimePicker;
    private readonly Label endTimeHint;
    private readonly NotifyIcon trayIcon;
    private readonly System.Windows.Forms.Timer offTimer;
    private readonly System.Windows.Forms.Timer sessionTimer;
    private ToolStripMenuItem sessionMenuItem;
    private DateTime? sessionDeadline;
    private bool active;
    private bool exiting;
    private bool displayRequestFailed;

    internal SleepwalkForm()
    {
        Text = "Sleepwalk";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(500, 366);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        Font = new Font("Segoe UI", 10F);

        Label title = new Label {
            Text = "Keep your PC working. Turn the displays off.",
            Font = new Font("Segoe UI", 13F, FontStyle.Bold),
            Location = new Point(22, 20),
            Size = new Size(456, 32)
        };
        Controls.Add(title);

        Label body = new Label {
            Text = "Your apps and AI agents keep running while Windows stays awake. Displays enter standby; monitor power is not cut completely.",
            Location = new Point(24, 62),
            Size = new Size(450, 42)
        };
        Controls.Add(body);

        Label durationLabel = new Label {
            Text = "Session ends",
            Location = new Point(24, 116),
            Size = new Size(126, 27)
        };
        Controls.Add(durationLabel);

        durationSelector = new ComboBox {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(150, 112),
            Size = new Size(300, 28)
        };
        durationSelector.Items.AddRange(new object[] {
            "Until stopped",
            "30 minutes",
            "1 hour",
            "2 hours",
            "Custom duration",
            "End at a specific time"
        });
        durationSelector.SelectedIndex = 0;
        durationSelector.SelectedIndexChanged += OnDurationModeChanged;
        Controls.Add(durationSelector);

        customHoursLabel = new Label {
            Text = "Hours",
            Location = new Point(24, 147),
            Size = new Size(52, 24),
            Visible = false
        };
        Controls.Add(customHoursLabel);

        customHours = new NumericUpDown {
            Location = new Point(78, 143),
            Size = new Size(82, 26),
            Minimum = 0,
            Maximum = 999,
            Value = 1,
            Visible = false
        };
        Controls.Add(customHours);

        customMinutesLabel = new Label {
            Text = "Minutes",
            Location = new Point(176, 147),
            Size = new Size(68, 24),
            Visible = false
        };
        Controls.Add(customMinutesLabel);

        customMinutes = new NumericUpDown {
            Location = new Point(246, 143),
            Size = new Size(82, 26),
            Minimum = 0,
            Maximum = 59,
            Visible = false
        };
        Controls.Add(customMinutes);

        endTimeLabel = new Label {
            Text = "End time",
            Location = new Point(24, 147),
            Size = new Size(92, 24),
            Visible = false
        };
        Controls.Add(endTimeLabel);

        endTimePicker = new DateTimePicker {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "h:mm tt",
            ShowUpDown = true,
            Location = new Point(120, 143),
            Size = new Size(120, 26),
            Value = DateTime.Now.AddHours(1),
            Visible = false
        };
        Controls.Add(endTimePicker);

        endTimeHint = new Label {
            Text = "Past time = tomorrow.",
            Location = new Point(250, 147),
            Size = new Size(245, 24),
            Visible = false
        };
        Controls.Add(endTimeHint);

        startButton = new Button {
            Text = "Start Sleepwalk",
            Location = new Point(24, 190),
            Size = new Size(175, 42)
        };
        startButton.Click += StartSession;
        Controls.Add(startButton);

        stopButton = new Button {
            Text = "Stop and wake displays",
            Location = new Point(210, 190),
            Size = new Size(188, 42),
            Enabled = false
        };
        stopButton.Click += delegate { StopSession(false, true); };
        Controls.Add(stopButton);

        status = new Label {
            Text = "Ready.",
            Location = new Point(24, 312),
            Size = new Size(450, 43)
        };
        Controls.Add(status);

        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add("Open Sleepwalk", null, delegate { ShowWindow(); });
        sessionMenuItem = new ToolStripMenuItem("Start Sleepwalk");
        sessionMenuItem.Click += delegate { ToggleSession(null, EventArgs.Empty); };
        menu.Items.Add(sessionMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, delegate { exiting = true; StopSession(false, false); Close(); });

        trayIcon = new NotifyIcon {
            Icon = SystemIcons.Application,
            Text = "Sleepwalk is off",
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

    private void StartSession(object sender, EventArgs e)
    {
        DateTime? deadline;
        if (!TryGetSessionDeadline(out deadline))
        {
            status.Text = "Enter a custom session length greater than zero.";
            MessageBox.Show(this,
                "A custom session must be at least 1 minute long.",
                "Sleepwalk", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        uint state = NativeMethods.SetThreadExecutionState(
            NativeMethods.EsContinuous | NativeMethods.EsSystemRequired);
        if (state == 0)
        {
            status.Text = "Windows could not enable keep-awake mode.";
            MessageBox.Show(this,
                "Windows could not enable keep-awake mode. Try closing and reopening Sleepwalk.",
                "Sleepwalk", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        active = true;
        displayRequestFailed = false;
        sessionDeadline = deadline;
        startButton.Enabled = false;
        stopButton.Enabled = true;
        durationSelector.Enabled = false;
        UpdateSessionStatus();
        UpdateTrayText();
        offTimer.Start();
        if (sessionDeadline.HasValue)
            sessionTimer.Start();
        TopMost = false;
        Hide();
    }

    private void ToggleSession(object sender, EventArgs e)
    {
        if (active)
            StopSession(false, true);
        else
            StartSession(sender, e);
    }

    private void OnTrayIconMouseClick(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            ToggleSession(sender, EventArgs.Empty);
    }

    private bool TryGetSessionDeadline(out DateTime? deadline)
    {
        deadline = null;
        switch (durationSelector.SelectedIndex)
        {
            case 1: deadline = DateTime.Now.AddMinutes(30); return true;
            case 2: deadline = DateTime.Now.AddHours(1); return true;
            case 3: deadline = DateTime.Now.AddHours(2); return true;
            case 4:
                int totalCustomMinutes = ((int)customHours.Value * 60) + (int)customMinutes.Value;
                if (totalCustomMinutes < 1)
                    return false;
                deadline = DateTime.Now.AddMinutes(totalCustomMinutes);
                return true;
            case 5:
                DateTime now = DateTime.Now;
                DateTime selectedTime = now.Date.Add(endTimePicker.Value.TimeOfDay);
                if (selectedTime <= now)
                    selectedTime = selectedTime.AddDays(1);
                deadline = selectedTime;
                return true;
            default:
                return true;
        }
    }

    private void OnDurationModeChanged(object sender, EventArgs e)
    {
        bool customDuration = durationSelector.SelectedIndex == 4;
        bool endAtTime = durationSelector.SelectedIndex == 5;
        customHoursLabel.Visible = customDuration;
        customHours.Visible = customDuration;
        customMinutesLabel.Visible = customDuration;
        customMinutes.Visible = customDuration;
        endTimeLabel.Visible = endAtTime;
        endTimePicker.Visible = endAtTime;
        endTimeHint.Visible = endAtTime;
    }

    private void OnDisplayStandbyTimer(object sender, EventArgs e)
    {
        offTimer.Stop();
        if (!active)
            return;

        displayRequestFailed = !NativeMethods.SetDisplays(2);
        UpdateTrayText();
        if (!displayRequestFailed)
        {
            trayIcon.ShowBalloonTip(1800, "Sleepwalk is active",
                "Windows accepted the display standby request. The PC is staying awake.",
                ToolTipIcon.Info);
        }
        else
        {
            trayIcon.ShowBalloonTip(5000, "Display standby request not confirmed",
                "Sleepwalk is keeping the PC awake, but Windows did not confirm the display request. Check the display connection.",
                ToolTipIcon.Warning);
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
            status.Text = "Session active until you stop it.";
            return;
        }

        status.Text = String.Format("Session active. {0} remaining.",
            FormatRemaining(sessionDeadline.Value - DateTime.Now));
    }

    private void UpdateTrayText()
    {
        if (!active)
        {
            trayIcon.Text = "Sleepwalk is off";
            sessionMenuItem.Text = "Start Sleepwalk";
            return;
        }

        sessionMenuItem.Text = "Stop and wake displays";

        if (displayRequestFailed)
        {
            trayIcon.Text = "Sleepwalk: display request failed";
            return;
        }

        string sessionText = sessionDeadline.HasValue
            ? String.Format("Sleepwalk: {0} left", FormatRemaining(sessionDeadline.Value - DateTime.Now))
            : "Sleepwalk is active";
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
        bool wakeSent = NativeMethods.SetDisplays(-1);
        uint keepAwakeCleared = NativeMethods.SetThreadExecutionState(NativeMethods.EsContinuous);
        active = false;
        sessionDeadline = null;
        displayRequestFailed = false;
        startButton.Enabled = true;
        stopButton.Enabled = false;
        durationSelector.Enabled = true;
        status.Text = sessionExpired
            ? "Time limit reached. Windows power settings are back in control."
            : "Session stopped. Windows power settings are back in control.";
        UpdateTrayText();

        if (!notifyUser)
            return;

        if (!wakeSent || keepAwakeCleared == 0)
        {
            string issue = !wakeSent && keepAwakeCleared == 0
                ? "Windows did not confirm display wake or release of the keep-awake request."
                : (!wakeSent
                    ? "Windows did not confirm the display wake request."
                    : "Windows did not confirm release of the keep-awake request.");
            trayIcon.ShowBalloonTip(5000, "Sleepwalk could not confirm every stop request",
                issue + " Press a key or use the monitor's power button if a display stays off.",
                ToolTipIcon.Warning);
        }
        else if (sessionExpired)
        {
            trayIcon.ShowBalloonTip(3500, "Sleepwalk session ended",
                "The time limit was reached. Windows power settings are back in control.",
                ToolTipIcon.Info);
        }
        else
        {
            trayIcon.ShowBalloonTip(2500, "Sleepwalk stopped",
                "Windows power settings are back in control.",
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
