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
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]

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
    internal static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);

    internal static void SetDisplays(int powerState)
    {
        IntPtr result;
        SendMessageTimeout(HwndBroadcast, WmSysCommand, ScMonitorPower,
            new IntPtr(powerState), SmtoAbortIfHung, 1500, out result);
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
    private readonly NotifyIcon trayIcon;
    private readonly System.Windows.Forms.Timer offTimer;
    private bool active;
    private bool exiting;

    internal SleepwalkForm()
    {
        Text = "Sleepwalk";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(416, 225);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        Font = new Font("Segoe UI", 10F);

        Label title = new Label {
            Text = "Keep your PC working. Turn the displays off.",
            Font = new Font("Segoe UI", 13F, FontStyle.Bold),
            Location = new Point(22, 20),
            Size = new Size(380, 32)
        };
        Controls.Add(title);

        Label body = new Label {
            Text = "Your apps and AI agents keep running while Windows stays awake. Displays enter standby; monitor power is not cut completely.",
            Location = new Point(24, 62),
            Size = new Size(374, 52)
        };
        Controls.Add(body);

        startButton = new Button {
            Text = "Start Sleepwalk",
            Location = new Point(24, 128),
            Size = new Size(175, 42)
        };
        startButton.Click += StartSession;
        Controls.Add(startButton);

        stopButton = new Button {
            Text = "Stop and wake displays",
            Location = new Point(210, 128),
            Size = new Size(188, 42),
            Enabled = false
        };
        stopButton.Click += delegate { StopSession(); };
        Controls.Add(stopButton);

        status = new Label {
            Text = "Ready.",
            Location = new Point(24, 186),
            Size = new Size(374, 28)
        };
        Controls.Add(status);

        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add("Open Sleepwalk", null, delegate { ShowWindow(); });
        menu.Items.Add("Stop and wake displays", null, delegate { StopSession(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, delegate { exiting = true; StopSession(); Close(); });

        trayIcon = new NotifyIcon {
            Icon = SystemIcons.Application,
            Text = "Sleepwalk is off",
            ContextMenuStrip = menu,
            Visible = true
        };
        trayIcon.DoubleClick += delegate { ShowWindow(); };

        offTimer = new System.Windows.Forms.Timer { Interval = 900 };
        offTimer.Tick += delegate {
            offTimer.Stop();
            if (active)
            {
                NativeMethods.SetDisplays(2);
                trayIcon.ShowBalloonTip(1800, "Sleepwalk is active",
                    "The PC is staying awake. Displays were asked to enter standby.",
                    ToolTipIcon.Info);
            }
        };

        FormClosing += OnFormClosing;
    }

    private void StartSession(object sender, EventArgs e)
    {
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
        startButton.Enabled = false;
        stopButton.Enabled = true;
        status.Text = "PC awake. Displays will enter standby in a moment.";
        trayIcon.Text = "Sleepwalk is active";
        offTimer.Start();
        Hide();
    }

    private void StopSession()
    {
        if (!active)
            return;

        NativeMethods.SetDisplays(-1);
        NativeMethods.SetThreadExecutionState(NativeMethods.EsContinuous);
        active = false;
        status.Text = "Session stopped. Windows power settings are back in control.";
        startButton.Enabled = true;
        stopButton.Enabled = false;
        trayIcon.Text = "Sleepwalk is off";
    }

    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void OnFormClosing(object sender, FormClosingEventArgs e)
    {
        if (!exiting && active)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        StopSession();
        offTimer.Dispose();
        trayIcon.Visible = false;
        trayIcon.Dispose();
    }
}
