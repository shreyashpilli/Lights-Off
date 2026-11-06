using System;
using System.Collections.Generic;
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
    internal const int WmApplication = 0x8000;
    internal const int WmReactivate = WmApplication + 41;
    internal const int WmWakeSelectedDisplays = WmApplication + 42;
    internal const int SwRestore = 9;
    internal const uint ModAlt = 0x0001;
    internal const uint ModControl = 0x0002;
    internal static readonly IntPtr HwndBroadcast = new IntPtr(0xffff);
    internal static readonly IntPtr ScMonitorPower = new IntPtr(0xF170);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PhysicalMonitor
    {
        internal IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string Description;
    }

    internal delegate IntPtr LowLevelHookProc(int code, IntPtr wParam, IntPtr lParam);

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

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    internal static extern IntPtr FindWindow(string className, string windowName);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SendMessageTimeout(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("dxva2.dll", SetLastError = true)]
    internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

    [DllImport("dxva2.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count,
        [Out] PhysicalMonitor[] physicalMonitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    internal static extern bool DestroyPhysicalMonitors(uint count, [In] PhysicalMonitor[] physicalMonitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    internal static extern bool SetVCPFeature(IntPtr monitor, byte vcpCode, uint newValue);

    [DllImport("dxva2.dll", SetLastError = true)]
    internal static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte vcpCode,
        out uint valueType, out uint currentValue, out uint maximumValue);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int hookId, LowLevelHookProc callback,
        IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string moduleName);

    internal static bool RequestDisplaysStandby()
    {
        IntPtr result;
        IntPtr sent = SendMessageTimeout(HwndBroadcast, WmSysCommand, ScMonitorPower,
            new IntPtr(2), SmtoAbortIfHung, 1500, out result);
        return sent != IntPtr.Zero;
    }

    internal static bool SetMonitorPower(Screen screen, uint value, out string error)
    {
        error = null;
        Point center = new Point(screen.Bounds.Left + screen.Bounds.Width / 2,
            screen.Bounds.Top + screen.Bounds.Height / 2);
        IntPtr monitor = MonitorFromPoint(center, 2);
        uint count;
        if (monitor == IntPtr.Zero || !GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out count) || count == 0)
        {
            error = "Windows could not find the physical display for " + screen.DeviceName + ".";
            return false;
        }

        PhysicalMonitor[] physicalMonitors = new PhysicalMonitor[(int)count];
        if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, physicalMonitors))
        {
            error = "Windows could not open the physical display for " + screen.DeviceName + ".";
            return false;
        }

        bool success = true;
        try
        {
            for (int i = 0; i < physicalMonitors.Length; i++)
            {
                uint valueType, currentValue, maximumValue;
                if (!GetVCPFeatureAndVCPFeatureReply(physicalMonitors[i].Handle, 0xD6,
                    out valueType, out currentValue, out maximumValue) ||
                    (currentValue != value && !SetVCPFeature(physicalMonitors[i].Handle, 0xD6, value)))
                    success = false;
            }
            if (!success && value != 1)
                for (int i = 0; i < physicalMonitors.Length; i++)
                    SetVCPFeature(physicalMonitors[i].Handle, 0xD6, 1);
        }
        finally
        {
            DestroyPhysicalMonitors(count, physicalMonitors);
        }

        if (!success)
            error = "" + screen.DeviceName + " did not accept the DDC/CI power command. Check that DDC/CI is enabled in the display settings.";
        return success;
    }

    internal static string GetPhysicalMonitorName(Screen screen, int displayNumber)
    {
        string fallback = "Display " + displayNumber + " (" + screen.DeviceName + ")";
        try
        {
            Point center = new Point(screen.Bounds.Left + screen.Bounds.Width / 2,
                screen.Bounds.Top + screen.Bounds.Height / 2);
            IntPtr monitor = MonitorFromPoint(center, 2);
            uint count;
            if (monitor == IntPtr.Zero || !GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out count) || count == 0)
                return fallback;

            PhysicalMonitor[] physicalMonitors = new PhysicalMonitor[(int)count];
            if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, physicalMonitors))
                return fallback;

            try
            {
                for (int i = 0; i < physicalMonitors.Length; i++)
                {
                    string description = (physicalMonitors[i].Description ?? String.Empty).Trim();
                    if (!String.IsNullOrEmpty(description))
                        return description;
                }
            }
            finally
            {
                DestroyPhysicalMonitors(count, physicalMonitors);
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        catch (ExternalException) { }

        return fallback;
    }

    internal static bool ActivateExistingWindow()
    {
        IntPtr hwnd = FindWindow(null, "Lights Off");
        if (hwnd == IntPtr.Zero)
            return false;
        IntPtr result;
        SendMessageTimeout(hwnd, WmReactivate, IntPtr.Zero, IntPtr.Zero, SmtoAbortIfHung, 1000, out result);
        ShowWindow(hwnd, SwRestore);
        SetForegroundWindow(hwnd);
        return true;
    }

    internal static void EnableDarkTitleBar(IntPtr hwnd)
    {
        try
        {
            int darkMode = 1;
            int result = DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));
            if (result != 0)
                DwmSetWindowAttribute(hwnd, 19, ref darkMode, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
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
            {
                NativeMethods.ActivateExistingWindow();
                return 0;
            }

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
    private readonly Label displayStatus;
    private readonly Label durationError;
    private readonly DarkSwitch keepAwakeToggle;
    private readonly TextBox customMinutes;
    private readonly Icon normalSleepIcon;
    private readonly Icon keepAwakeIcon;
    private readonly NotifyIcon trayIcon;
    private readonly System.Windows.Forms.Timer displayFeedbackTimer;
    private readonly System.Windows.Forms.Timer sessionTimer;
    private readonly System.Windows.Forms.Timer pulseTimer;
    private readonly RoundedPanel displayCard;
    private readonly RoundedPanel awakeCard;
    private readonly RoundedPanel targetPanel;
    private readonly RoundedPanel customInputGroup;
    private readonly RoundedPanel statusPanel;
    private readonly ActionButton targetModeButton;
    private readonly ContextMenuStrip targetModeMenu;
    private readonly ToolStripMenuItem[] targetModeItems;
    private readonly Label targetModeHelp;
    private readonly MonitorArrangementControl monitorArrangement;
    private readonly ActionButton targetSummaryButton;
    private readonly ActionButton customDurationButton;
    private readonly ActionButton settingsButton;
    private readonly Label footer;
    private ToolStripMenuItem sessionMenuItem;
    private DateTime? sessionDeadline;
    private int? sessionPresetMinutes;
    private bool active;
    private bool exiting;
    private bool displayRequestFailed;
    private bool pulseBright;
    private bool displaySelectionExpanded;
    private bool customDurationExpanded;
    private volatile bool targetedDisplaysAsleep;
    private int wakeMessageQueued;
    private int monitorTargetMode;
    private readonly List<string> selectedMonitorNames = new List<string>();
    private Screen[] lastTargetedScreens = new Screen[0];
    private IntPtr mouseHook;
    private IntPtr keyboardHook;
    private NativeMethods.LowLevelHookProc mouseHookProc;
    private NativeMethods.LowLevelHookProc keyboardHookProc;

    internal LightsOffForm()
    {
        normalSleepIcon = LoadIconResource("LightsOff.TrayNormal.ico");
        keepAwakeIcon = LoadIconResource("LightsOff.TrayAwake.ico");
        Text = "Lights Off";
        Icon = normalSleepIcon;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 514);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        Font = new Font("Segoe UI", 9.5F);
        BackColor = Color.FromArgb(18, 19, 22);
        ForeColor = Color.FromArgb(237, 239, 244);
        LoadMonitorSettings();

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

        displayCard = new RoundedPanel {
            Location = new Point(18, 64),
            Size = new Size(484, 157),
            BackColor = Color.FromArgb(30, 32, 40),
            SurroundingColor = Color.FromArgb(18, 19, 22)
        };
        Controls.Add(displayCard);
        Label displayHeading = new Label {
            Text = "Displays",
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(165, 170, 184),
            Location = new Point(18, 13),
            Size = new Size(200, 22)
        };
        displayCard.Controls.Add(displayHeading);

        displayStatus = new Label {
            Text = "Displays active",
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = Color.FromArgb(125, 170, 140),
            Location = new Point(18, 34),
            Size = new Size(398, 20)
        };
        displayCard.Controls.Add(displayStatus);

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
        trayHint.SetToolTip(infoButton, "Turn displays off to save light and power. Keep PC awake only prevents Windows from sleeping.");
        displayCard.Controls.Add(infoButton);

        displayButton = new ActionButton {
            Text = "Turn displays off now",
            ShowMonitorIcon = true,
            AccentColor = Color.FromArgb(99, 102, 241),
            HoverColor = Color.FromArgb(119, 122, 255),
            UseAccent = true,
            ForeColor = Color.White,
            SurfaceColor = Color.FromArgb(30, 32, 40),
            Font = new Font("Segoe UI Semibold", 11F),
            Location = new Point(16, 57),
            Size = new Size(452, 50)
        };
        displayButton.Click += delegate { RequestDisplaysStandby(true); };
        displayCard.Controls.Add(displayButton);

        targetSummaryButton = new ActionButton {
            Text = "All displays included  |  Change",
            AccentColor = Color.FromArgb(43, 46, 57),
            HoverColor = Color.FromArgb(61, 65, 78),
            SurfaceColor = Color.FromArgb(30, 32, 40),
            ForeColor = Color.FromArgb(194, 198, 208),
            Font = new Font("Segoe UI", 8.5F),
            Location = new Point(16, 112),
            Size = new Size(452, 31),
            IsPill = true
        };
        targetSummaryButton.Click += delegate { ToggleDisplaySelection(); };
        targetSummaryButton.AccessibleName = "Display selection";
        trayHint.SetToolTip(targetSummaryButton, "Choose whether the standby action targets all, selected, or only the primary display.");
        displayCard.Controls.Add(targetSummaryButton);

        targetPanel = new RoundedPanel {
            Location = new Point(18, 228),
            Size = new Size(484, 176),
            BackColor = Color.FromArgb(30, 32, 40),
            SurroundingColor = Color.FromArgb(18, 19, 22),
            Visible = false
        };
        Controls.Add(targetPanel);
        Label targetHeading = new Label {
            Text = "Display arrangement",
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = Color.FromArgb(165, 170, 184),
            Location = new Point(16, 10),
            Size = new Size(200, 22)
        };
        targetPanel.Controls.Add(targetHeading);
        targetModeButton = new ActionButton {
            Text = GetTargetModeName(monitorTargetMode) + "  v",
            AccentColor = Color.FromArgb(43, 46, 57),
            HoverColor = Color.FromArgb(61, 65, 78),
            SurfaceColor = Color.FromArgb(25, 27, 34),
            ForeColor = Color.FromArgb(220, 222, 230),
            Font = new Font("Segoe UI", 8.5F),
            Location = new Point(254, 8),
            Size = new Size(214, 28),
            IsPill = true
        };
        targetModeButton.AccessibleName = "Display targeting mode";
        targetModeButton.AccessibleDescription = "Choose all displays, selected displays, or only the primary display.";
        targetModeButton.Click += OnTargetModeButtonClick;
        targetPanel.Controls.Add(targetModeButton);
        targetModeMenu = new ContextMenuStrip {
            BackColor = Color.FromArgb(25, 27, 34),
            ForeColor = Color.FromArgb(237, 239, 244),
            Font = new Font("Segoe UI", 9F),
            ShowImageMargin = false,
            Padding = new Padding(4),
            Renderer = new DarkContextMenuRenderer()
        };
        targetModeItems = new ToolStripMenuItem[] {
            new ToolStripMenuItem("All displays"),
            new ToolStripMenuItem("Selected displays"),
            new ToolStripMenuItem("Primary display")
        };
        for (int modeIndex = 0; modeIndex < targetModeItems.Length; modeIndex++)
        {
            targetModeItems[modeIndex].Tag = modeIndex;
            targetModeItems[modeIndex].Click += OnTargetModeMenuItemClick;
            targetModeMenu.Items.Add(targetModeItems[modeIndex]);
        }
        RefreshTargetModeMenu();
        targetModeHelp = new Label {
            Name = "targetModeHelp",
            Text = "All connected displays are included. Arrangement is informational.",
            Font = new Font("Segoe UI", 8F),
            ForeColor = Color.FromArgb(165, 170, 184),
            Location = new Point(16, 39),
            Size = new Size(450, 18)
        };
        targetPanel.Controls.Add(targetModeHelp);
        monitorArrangement = new MonitorArrangementControl {
            Location = new Point(16, 64),
            Size = new Size(452, 100),
            BackColor = Color.FromArgb(30, 32, 40),
            SelectedMonitorNames = selectedMonitorNames
        };
        trayHint.SetToolTip(targetModeHelp,
            "All displays uses Windows standby. Selected and Primary use DDC/CI, which must be enabled on each display.");
        monitorArrangement.SelectionChanged += OnMonitorSelectionChanged;
        targetPanel.Controls.Add(monitorArrangement);
        RefreshMonitorSelection();

        awakeCard = new RoundedPanel {
            Location = new Point(18, 232),
            Size = new Size(484, 228),
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
            OnColor = Color.FromArgb(34, 197, 94)
        };
        keepAwakeToggle.CheckedChanged += OnKeepAwakeToggleChanged;
        awakeCard.Controls.Add(keepAwakeToggle);
        Label awakeCaption = new Label {
            Text = "Prevents Windows from automatically putting your PC to sleep. Your displays remain unaffected.",
            ForeColor = Color.FromArgb(165, 170, 184),
            Location = new Point(18, 42),
            Size = new Size(448, 30),
            Font = new Font("Segoe UI", 8.5F),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft
        };
        awakeCard.Controls.Add(awakeCaption);

        preset30Button = AddPresetButton(awakeCard, "30 min", 18, 80, 30);
        preset60Button = AddPresetButton(awakeCard, "1 hour", 161, 80, 60);
        preset120Button = AddPresetButton(awakeCard, "2 hours", 304, 80, 120);

        customDurationButton = new ActionButton {
            Text = "Custom duration  +",
            AccentColor = Color.FromArgb(43, 46, 57),
            HoverColor = Color.FromArgb(61, 65, 78),
            SurfaceColor = Color.FromArgb(30, 32, 40),
            ForeColor = Color.FromArgb(194, 198, 208),
            Font = new Font("Segoe UI", 8.5F),
            Location = new Point(18, 122),
            Size = new Size(448, 26),
            IsPill = true
        };
        customDurationButton.Click += delegate { ToggleCustomDuration(); };
        awakeCard.Controls.Add(customDurationButton);
        customInputGroup = new RoundedPanel {
            Location = new Point(18, 155),
            Size = new Size(448, 38),
            BackColor = Color.FromArgb(18, 19, 22),
            BorderColor = Color.FromArgb(46, 50, 62),
            SurroundingColor = Color.FromArgb(30, 32, 40),
            Radius = 9,
            Visible = false
        };
        awakeCard.Controls.Add(customInputGroup);
        customMinutes = new TextBox {
            Text = "90",
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(18, 19, 22),
            ForeColor = Color.FromArgb(242, 243, 247),
            Font = new Font("Segoe UI Semibold", 10F),
            Location = new Point(12, 10),
            Size = new Size(72, 20)
        };
        customMinutes.KeyPress += OnlyAllowDigits;
        customMinutes.TextChanged += delegate { if (durationError != null) durationError.Visible = false; };
        customInputGroup.Controls.Add(customMinutes);
        Label minutesLabel = new Label {
            Text = "minutes",
            ForeColor = Color.FromArgb(150, 155, 170),
            Location = new Point(83, 8),
            Size = new Size(64, 22)
        };
        customInputGroup.Controls.Add(minutesLabel);
        customStartButton = new ActionButton {
            Text = "Start",
            AccentColor = Color.FromArgb(99, 102, 241),
            HoverColor = Color.FromArgb(119, 122, 255),
            UseAccent = true,
            SurfaceColor = Color.FromArgb(18, 19, 22),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 9F),
            Location = new Point(354, 3),
            Size = new Size(88, 32)
        };
        customStartButton.Click += delegate {
            int minutes;
            if (!Int32.TryParse(customMinutes.Text, out minutes) || minutes < 1 || minutes > 59999)
            {
                durationError.Text = "Enter 1 to 59999 whole minutes.";
                durationError.Visible = true;
                return;
            }
            durationError.Visible = false;
            StartTimedKeepAwake(minutes, null);
        };
        customInputGroup.Controls.Add(customStartButton);

        durationError = new Label {
            Text = "",
            Font = new Font("Segoe UI", 8F),
            ForeColor = Color.FromArgb(251, 191, 36),
            Location = new Point(18, 196),
            Size = new Size(448, 18),
            Visible = false
        };
        awakeCard.Controls.Add(durationError);

        statusPanel = new RoundedPanel {
            Location = new Point(18, 155),
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
            Size = new Size(250, 30)
        };
        statusPanel.Controls.Add(status);
        stopButton = new ActionButton {
            Text = "Allow normal sleep",
            AccentColor = Color.FromArgb(185, 28, 28),
            HoverColor = Color.FromArgb(220, 38, 38),
            SurfaceColor = Color.FromArgb(25, 27, 34),
            ForeColor = Color.White,
            UseAccent = true,
            Font = new Font("Segoe UI Semibold", 8.5F),
            Location = new Point(300, 17),
            Size = new Size(132, 34),
            Enabled = false,
            Visible = false
        };
        stopButton.Click += delegate { StopSession(false, true); };
        statusPanel.Controls.Add(stopButton);

        settingsButton = new ActionButton {
            Text = "Settings",
            AccentColor = Color.FromArgb(43, 46, 57),
            HoverColor = Color.FromArgb(61, 65, 78),
            SurfaceColor = Color.FromArgb(18, 19, 22),
            ForeColor = Color.FromArgb(194, 198, 208),
            Font = new Font("Segoe UI Semibold", 9F),
            Location = new Point(404, 19),
            Size = new Size(98, 30),
            IsPill = false
        };
        settingsButton.Click += delegate { ShowSettings(); };
        Controls.Add(settingsButton);
        footer = new Label {
            Text = "Display standby uses normal low-power mode.",
            ForeColor = Color.FromArgb(126, 131, 145),
            Font = new Font("Segoe UI", 8.5F),
            Location = new Point(18, 476),
            Size = new Size(484, 20)
        };
        Controls.Add(footer);

        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add("Open Lights Off", null, delegate { ShowWindow(); });
        menu.Items.Add("Settings", null, delegate { ShowSettings(); });
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

        displayFeedbackTimer = new System.Windows.Forms.Timer { Interval = 3500 };
        displayFeedbackTimer.Tick += delegate {
            displayFeedbackTimer.Stop();
            displayStatus.Text = GetDisplayStatusText();
            displayStatus.ForeColor = Color.FromArgb(125, 170, 140);
        };

        sessionTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        sessionTimer.Tick += OnSessionTimer;

        pulseTimer = new System.Windows.Forms.Timer { Interval = 520 };
        pulseTimer.Tick += delegate {
            pulseBright = !pulseBright;
            statusDot.ForeColor = active
                ? (pulseBright ? Color.FromArgb(74, 222, 128) : Color.FromArgb(22, 101, 52))
                : Color.FromArgb(122, 126, 140);
        };

        FormClosing += OnFormClosing;
        LayoutHome();
        RefreshDisplaySummary();
    }

    private void LayoutHome()
    {
        int awakeY = 232 + (displaySelectionExpanded ? 184 : 0);
        int awakeHeight = customDurationExpanded ? 294 : 237;
        targetPanel.Visible = displaySelectionExpanded;
        awakeCard.Location = new Point(18, awakeY);
        awakeCard.Height = awakeHeight;
        customInputGroup.Visible = customDurationExpanded;
        durationError.Visible = customDurationExpanded && !String.IsNullOrEmpty(durationError.Text);
        customDurationButton.Text = customDurationExpanded ? "Custom duration  -" : "Custom duration  +";
        statusPanel.Location = new Point(18, customDurationExpanded ? 217 : 155);
        statusPanel.Size = new Size(448, customDurationExpanded ? 64 : 68);
        statusDot.Location = new Point(14, (statusPanel.Height - 24) / 2);
        status.Location = new Point(36, Math.Max(10, (statusPanel.Height - 30) / 2));
        stopButton.Location = new Point(300, (statusPanel.Height - stopButton.Height) / 2);
        int footerY = awakeY + awakeHeight + 12;
        footer.Location = new Point(18, footerY + 4);
        ClientSize = new Size(520, footerY + 42);
    }

    private void ToggleDisplaySelection()
    {
        displaySelectionExpanded = !displaySelectionExpanded;
        LayoutHome();
        RefreshDisplaySummary();
    }

    private void ToggleCustomDuration()
    {
        customDurationExpanded = !customDurationExpanded;
        if (!customDurationExpanded)
            durationError.Visible = false;
        LayoutHome();
        if (customDurationExpanded)
            customMinutes.Focus();
    }

    private void OnTargetModeButtonClick(object sender, EventArgs e)
    {
        targetModeMenu.Show(targetModeButton, new Point(0, targetModeButton.Height));
    }

    private void OnTargetModeMenuItemClick(object sender, EventArgs e)
    {
        ToolStripMenuItem item = sender as ToolStripMenuItem;
        if (item == null)
            return;
        monitorTargetMode = (int)item.Tag;
        if (monitorTargetMode == 1 && selectedMonitorNames.Count == 0)
            foreach (Screen screen in Screen.AllScreens)
                selectedMonitorNames.Add(screen.DeviceName);
        RefreshTargetModeMenu();
        SaveMonitorSettings();
        RefreshMonitorSelection();
    }

    private void RefreshTargetModeMenu()
    {
        if (targetModeButton != null)
        {
            targetModeButton.Text = GetTargetModeName(monitorTargetMode) + "  v";
            targetModeButton.Invalidate();
        }
        if (targetModeItems != null)
            for (int i = 0; i < targetModeItems.Length; i++)
                targetModeItems[i].Checked = i == monitorTargetMode;
    }

    private static string GetTargetModeName(int mode)
    {
        if (mode == 1)
            return "Selected displays";
        if (mode == 2)
            return "Primary display";
        return "All displays";
    }

    private void OnMonitorSelectionChanged(object sender, EventArgs e)
    {
        SaveMonitorSettings();
        RefreshDisplaySummary();
    }

    private void RefreshMonitorSelection()
    {
        Screen[] screens = Screen.AllScreens;
        for (int i = selectedMonitorNames.Count - 1; i >= 0; i--)
        {
            bool found = false;
            foreach (Screen screen in screens)
                if (String.Equals(screen.DeviceName, selectedMonitorNames[i], StringComparison.OrdinalIgnoreCase))
                    found = true;
            if (!found)
                selectedMonitorNames.RemoveAt(i);
        }
        if (monitorArrangement != null)
            monitorArrangement.SetScreens(screens, selectedMonitorNames, monitorTargetMode);
        if (targetModeHelp != null)
            targetModeHelp.Text = GetTargetModeHelpText();
        if (targetSummaryButton != null)
            RefreshDisplaySummary();
        SaveMonitorSettings();
    }

    private void RefreshDisplaySummary()
    {
        int count = Screen.AllScreens.Length;
        if (displayStatus != null)
        {
            displayStatus.Text = String.Format("{0} display{1} connected", count, count == 1 ? "" : "s");
            displayStatus.ForeColor = Color.FromArgb(125, 170, 140);
        }
        if (targetSummaryButton == null)
            return;
        string summary;
        if (monitorTargetMode == 1)
            summary = String.Format("{0} of {1} displays selected", selectedMonitorNames.Count, count);
        else if (monitorTargetMode == 2)
            summary = "Primary display only";
        else
            summary = String.Format("All {0} display{1} included", count, count == 1 ? "" : "s");
        targetSummaryButton.Text = summary + "  \u00B7  " + (displaySelectionExpanded ? "Hide" : "Change");
        targetSummaryButton.AccessibleDescription = summary + ". Activate to " +
            (displaySelectionExpanded ? "hide" : "change") + " display targeting options.";
        targetSummaryButton.Invalidate();
    }

    private string GetDisplayStatusText()
    {
        int count = Screen.AllScreens.Length;
        return String.Format("{0} display{1} connected", count, count == 1 ? "" : "s");
    }

    private string GetTargetModeHelpText()
    {
        if (monitorTargetMode == 1)
            return "Click displays to include or exclude them. Only included displays are targeted.";
        if (monitorTargetMode == 2)
            return "Only the Windows primary display is targeted.";
        return "All connected displays are included. Arrangement is informational.";
    }

    private Screen[] GetTargetScreens()
    {
        Screen[] screens = Screen.AllScreens;
        if (monitorTargetMode == 0)
            return screens;
        List<Screen> targets = new List<Screen>();
        foreach (Screen screen in screens)
        {
            if (monitorTargetMode == 1 && selectedMonitorNames.Contains(screen.DeviceName))
                targets.Add(screen);
            else if (monitorTargetMode == 2 && screen.Primary)
                targets.Add(screen);
        }
        return targets.ToArray();
    }

    private void LoadMonitorSettings()
    {
        string path = MonitorSettingsPath;
        try
        {
            if (!File.Exists(path))
                return;
            string[] lines = File.ReadAllLines(path);
            int parsedMode;
            if (lines.Length > 0 && Int32.TryParse(lines[0], out parsedMode) && parsedMode >= 0 && parsedMode <= 2)
                monitorTargetMode = parsedMode;
            for (int i = 1; i < lines.Length; i++)
                try { selectedMonitorNames.Add(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(lines[i]))); }
                catch (FormatException) { }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void SaveMonitorSettings()
    {
        try
        {
            string directory = Path.GetDirectoryName(MonitorSettingsPath);
            Directory.CreateDirectory(directory);
            List<string> lines = new List<string> { monitorTargetMode.ToString() };
            foreach (string name in selectedMonitorNames)
                lines.Add(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(name)));
            File.WriteAllLines(MonitorSettingsPath, lines.ToArray());
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string MonitorSettingsPath
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lights Off", "monitor-target.ini"); }
    }

    private ActionButton AddPresetButton(Control parent, string text, int x, int y, int minutes)
    {
        ActionButton button = new ActionButton {
            Text = text,
            AccentColor = Color.FromArgb(34, 197, 94),
            HoverColor = Color.FromArgb(53, 220, 114),
            SurfaceColor = Color.FromArgb(30, 32, 40),
            ForeColor = Color.FromArgb(237, 239, 244),
            Location = new Point(x, y),
            Size = new Size(130, 36),
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

    private void RequestDisplaysStandby(bool showSuccess)
    {
        displayFeedbackTimer.Stop();
        if (targetedDisplaysAsleep)
            WakeTargetedDisplays();

        string error = null;
        if (monitorTargetMode == 0)
        {
            displayRequestFailed = !NativeMethods.RequestDisplaysStandby();
            displayStatus.Text = displayRequestFailed
                ? "Windows did not confirm the standby request."
                : "Standby request sent. Displays should turn off.";
        }
        else
        {
            Screen[] targets = GetTargetScreens();
            if (targets.Length == 0)
            {
                displayRequestFailed = true;
                displayStatus.Text = "Select at least one display first.";
                displayStatus.ForeColor = Color.FromArgb(251, 191, 36);
                UpdateTrayText();
                return;
            }
            if (!EnsureWakeHooks())
            {
                displayRequestFailed = true;
                displayStatus.Text = "Could not install the input wake listener.";
                displayStatus.ForeColor = Color.FromArgb(251, 191, 36);
                UpdateTrayText();
                return;
            }

            List<Screen> turnedOff = new List<Screen>();
            displayRequestFailed = false;
            foreach (Screen screen in targets)
            {
                if (!NativeMethods.SetMonitorPower(screen, 4, out error))
                {
                    displayRequestFailed = true;
                    break;
                }
                turnedOff.Add(screen);
            }
            if (displayRequestFailed)
            {
                foreach (Screen screen in turnedOff)
                {
                    string ignored;
                    NativeMethods.SetMonitorPower(screen, 1, out ignored);
                }
                UnhookWakeHooks();
                displayStatus.Text = error ?? "The selected display did not accept the power command.";
            }
            else
            {
                lastTargetedScreens = targets;
                targetedDisplaysAsleep = true;
                displayStatus.Text = String.Format("{0} display{1} entering standby. Move the mouse or press a key to wake.",
                    targets.Length, targets.Length == 1 ? "" : "s");
            }
        }

        displayStatus.ForeColor = displayRequestFailed
            ? Color.FromArgb(251, 191, 36)
            : Color.FromArgb(165, 168, 255);
        displayFeedbackTimer.Start();
        UpdateTrayText();
        if (displayRequestFailed)
        {
            trayIcon.ShowBalloonTip(5000, "Display standby request not confirmed",
                error ?? "Windows did not confirm the display standby request. Check the display connection and DDC/CI setting.",
                ToolTipIcon.Warning);
        }
        else if (showSuccess)
        {
            trayIcon.ShowBalloonTip(1800, "Lights Off",
                monitorTargetMode == 0 ? "Windows accepted the request to turn off the displays." : "The selected displays are entering standby.",
                ToolTipIcon.Info);
        }
    }

    private bool EnsureWakeHooks()
    {
        if (mouseHook != IntPtr.Zero && keyboardHook != IntPtr.Zero)
            return true;
        mouseHookProc = OnGlobalInput;
        keyboardHookProc = OnGlobalInput;
        IntPtr module = NativeMethods.GetModuleHandle(null);
        mouseHook = NativeMethods.SetWindowsHookEx(14, mouseHookProc, module, 0);
        keyboardHook = NativeMethods.SetWindowsHookEx(13, keyboardHookProc, module, 0);
        if (mouseHook != IntPtr.Zero && keyboardHook != IntPtr.Zero)
            return true;
        UnhookWakeHooks();
        return false;
    }

    private IntPtr OnGlobalInput(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && targetedDisplaysAsleep && IsHandleCreated &&
            Interlocked.CompareExchange(ref wakeMessageQueued, 1, 0) == 0)
        {
            if (!NativeMethods.PostMessage(Handle, NativeMethods.WmWakeSelectedDisplays, IntPtr.Zero, IntPtr.Zero))
                Interlocked.Exchange(ref wakeMessageQueued, 0);
        }
        return NativeMethods.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private void WakeTargetedDisplays()
    {
        if (!targetedDisplaysAsleep)
            return;
        bool success = true;
        string error = null;
        foreach (Screen screen in lastTargetedScreens)
        {
            string monitorError;
            if (!NativeMethods.SetMonitorPower(screen, 1, out monitorError))
            {
                success = false;
                if (error == null) error = monitorError;
            }
        }
        if (success)
        {
            targetedDisplaysAsleep = false;
            lastTargetedScreens = new Screen[0];
            UnhookWakeHooks();
            displayRequestFailed = false;
            displayStatus.Text = "Displays are active again.";
            displayStatus.ForeColor = Color.FromArgb(74, 222, 128);
            displayFeedbackTimer.Stop();
            displayFeedbackTimer.Start();
        }
        else
        {
            displayStatus.Text = error ?? "Windows could not wake a selected display.";
            displayStatus.ForeColor = Color.FromArgb(251, 191, 36);
            trayIcon.ShowBalloonTip(5000, "Could not wake a selected display",
                error ?? "Try the display's power button or check its DDC/CI setting.", ToolTipIcon.Warning);
        }
        UpdateTrayText();
    }

    private void UnhookWakeHooks()
    {
        if (mouseHook != IntPtr.Zero)
            NativeMethods.UnhookWindowsHookEx(mouseHook);
        if (keyboardHook != IntPtr.Zero)
            NativeMethods.UnhookWindowsHookEx(keyboardHook);
        mouseHook = IntPtr.Zero;
        keyboardHook = IntPtr.Zero;
        mouseHookProc = null;
        keyboardHookProc = null;
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
            status.Text = "PC awake until you switch it off";
            status.ForeColor = Color.FromArgb(74, 222, 128);
            customStartButton.Text = "Active";
            return;
        }

        status.Text = String.Format("PC awake  \u00B7  {0} left", FormatRemaining(sessionDeadline.Value - DateTime.Now));
        status.ForeColor = Color.FromArgb(74, 222, 128);
        customStartButton.Text = FormatRemaining(sessionDeadline.Value - DateTime.Now);
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
            customStartButton.Text = "Start";
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
        status.ForeColor = Color.FromArgb(177, 181, 193);
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
        if (targetedDisplaysAsleep)
            WakeTargetedDisplays();
        UnhookWakeHooks();
        NativeMethods.UnregisterHotKey(Handle, HotkeyMonitorStandby);
        NativeMethods.UnregisterHotKey(Handle, HotkeyToggleKeepAwake);
        displayFeedbackTimer.Dispose();
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
        preset30Button.Text = preset30Button.Selected ? "30 min  ·  " + FormatRemaining(sessionDeadline.Value - DateTime.Now) : "30 min";
        preset60Button.Text = preset60Button.Selected ? "1 hour  ·  " + FormatRemaining(sessionDeadline.Value - DateTime.Now) : "1 hour";
        preset120Button.Text = preset120Button.Selected ? "2 hours  ·  " + FormatRemaining(sessionDeadline.Value - DateTime.Now) : "2 hours";
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
        NativeMethods.EnableDarkTitleBar(Handle);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmReactivate)
        {
            ShowWindow();
            return;
        }
        if (message.Msg == NativeMethods.WmWakeSelectedDisplays)
        {
            Interlocked.Exchange(ref wakeMessageQueued, 0);
            WakeTargetedDisplays();
            return;
        }
        if (message.Msg == 0x007E)
        {
            RefreshMonitorSelection();
        }
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
            status.Text = "A shortcut is unavailable. Change it in Settings.";
    }

    private void ShowSettings()
    {
        NativeMethods.UnregisterHotKey(Handle, HotkeyMonitorStandby);
        NativeMethods.UnregisterHotKey(Handle, HotkeyToggleKeepAwake);
        using (Form dialog = new Form())
        {
            dialog.Text = "Settings";
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialog.MaximizeBox = false;
            dialog.MinimizeBox = false;
            dialog.ClientSize = new Size(390, 238);
            dialog.BackColor = Color.FromArgb(18, 19, 22);
            dialog.ForeColor = Color.FromArgb(237, 239, 244);
            NativeMethods.EnableDarkTitleBar(dialog.Handle);

            Label heading = new Label { Text = "Keyboard shortcuts", Font = new Font("Segoe UI", 12F, FontStyle.Bold), Location = new Point(18, 16), Size = new Size(330, 26), ForeColor = Color.FromArgb(242, 243, 247) };
            Label hint = new Label { Text = "Click a key box, then press the shortcut you want.", Location = new Point(18, 48), Size = new Size(350, 24), ForeColor = Color.FromArgb(165, 170, 184) };
            TextBox monitorBox = CreateShortcutBox(dialog, "Turn displays off", 88, monitorModifiers, monitorKey);
            TextBox awakeBox = CreateShortcutBox(dialog, "Toggle keep-awake", 132, awakeModifiers, awakeKey);
            Button save = new Button { Text = "Save shortcuts", Location = new Point(238, 187), Size = new Size(132, 32), DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(99, 102, 241), ForeColor = Color.White };
            dialog.Controls.Add(heading);
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
        TextBox box = new TextBox { ReadOnly = true, Text = FormatShortcut(modifiers, key), Location = new Point(175, y), Size = new Size(175, 26), Tag = modifiers + ":" + (int)key, BackColor = Color.FromArgb(30, 32, 40), ForeColor = Color.FromArgb(237, 239, 244), BorderStyle = BorderStyle.FixedSingle };
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

internal sealed class MonitorArrangementControl : Control
{
    private Screen[] screens = new Screen[0];
    private readonly List<string> emptySelection = new List<string>();
    private readonly Dictionary<string, string> friendlyNameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly ToolTip displayToolTip;
    private List<string> selectedNames;
    private Rectangle[] displayRects = new Rectangle[0];
    private string[] displayNames = new string[0];
    private int targetMode;
    private bool selectionEnabled;
    private int hoveredDisplay = -1;

    internal List<string> SelectedMonitorNames
    {
        get { return selectedNames; }
        set { selectedNames = value ?? emptySelection; Invalidate(); }
    }

    internal bool SelectionEnabled
    {
        get { return selectionEnabled; }
        set { selectionEnabled = value; Cursor = value ? Cursors.Hand : Cursors.Default; Invalidate(); }
    }

    internal event EventHandler SelectionChanged;

    internal MonitorArrangementControl()
    {
        selectedNames = emptySelection;
        displayToolTip = new ToolTip { AutoPopDelay = 10000, InitialDelay = 350, ReshowDelay = 100 };
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AccessibleRole = AccessibleRole.Grouping;
    }

    internal void SetScreens(Screen[] availableScreens, List<string> selected, int mode)
    {
        screens = availableScreens ?? new Screen[0];
        selectedNames = selected ?? emptySelection;
        targetMode = mode;
        displayNames = new string[screens.Length];
        for (int i = 0; i < screens.Length; i++)
        {
            string name;
            if (!friendlyNameCache.TryGetValue(screens[i].DeviceName, out name))
            {
                name = NativeMethods.GetPhysicalMonitorName(screens[i], i + 1);
                friendlyNameCache[screens[i].DeviceName] = name;
            }
            displayNames[i] = name;
        }
        SelectionEnabled = mode == 1;
        hoveredDisplay = -1;
        displayToolTip.Hide(this);
        displayToolTip.SetToolTip(this, String.Empty);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        displayRects = new Rectangle[screens.Length];
        if (screens.Length == 0)
        {
            TextRenderer.DrawText(e.Graphics, "No displays detected", Font, ClientRectangle,
                Color.FromArgb(165, 170, 184), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        int minX = screens[0].Bounds.Left;
        int minY = screens[0].Bounds.Top;
        int maxX = screens[0].Bounds.Right;
        int maxY = screens[0].Bounds.Bottom;
        foreach (Screen screen in screens)
        {
            minX = Math.Min(minX, screen.Bounds.Left);
            minY = Math.Min(minY, screen.Bounds.Top);
            maxX = Math.Max(maxX, screen.Bounds.Right);
            maxY = Math.Max(maxY, screen.Bounds.Bottom);
        }
        float scale = Math.Min((Width - 12F) / Math.Max(1, maxX - minX), (Height - 8F) / Math.Max(1, maxY - minY));
        int totalWidth = Math.Max(1, (int)Math.Ceiling((maxX - minX) * scale));
        int totalHeight = Math.Max(1, (int)Math.Ceiling((maxY - minY) * scale));
        int originX = (Width - totalWidth) / 2;
        int originY = (Height - totalHeight) / 2;

        for (int i = 0; i < screens.Length; i++)
        {
            Screen screen = screens[i];
            int x = originX + (int)Math.Round((screen.Bounds.Left - minX) * scale);
            int y = originY + (int)Math.Round((screen.Bounds.Top - minY) * scale);
            int width = Math.Max(30, (int)Math.Floor(screen.Bounds.Width * scale) - 2);
            int height = Math.Max(24, (int)Math.Floor(screen.Bounds.Height * scale) - 2);
            displayRects[i] = new Rectangle(x, y, width, height);
            bool selected = IsTargeted(screen);
            Color fill = selected ? Color.FromArgb(50, 47, 104) : Color.FromArgb(25, 27, 34);
            Color border = selected ? Color.FromArgb(129, 132, 255) : Color.FromArgb(88, 92, 105);
            using (GraphicsPath path = UiDrawing.RoundedRectangle(displayRects[i], 5))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(border, 1F))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
            Rectangle box = displayRects[i];
            using (Font numberFont = new Font("Segoe UI", 8.5F, FontStyle.Bold))
            using (Font detailFont = new Font("Segoe UI", 5.5F))
            using (Font stateFont = new Font("Segoe UI", 6F, FontStyle.Bold))
            {
                Rectangle numberRect = new Rectangle(box.X + 5, box.Y + 3, Math.Max(1, box.Width - 10), 15);
                TextRenderer.DrawText(e.Graphics, (i + 1).ToString(), numberFont, numberRect,
                    Color.FromArgb(237, 239, 244),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

                if (screen.Primary && box.Width >= 45 && box.Height >= 23)
                {
                    string primaryText = box.Width >= 105 ? "PRIMARY" : "P";
                    Size primarySize = TextRenderer.MeasureText(e.Graphics, primaryText, stateFont);
                    Rectangle badge = new Rectangle(box.Right - primarySize.Width - 10, box.Y + 3,
                        primarySize.Width + 6, Math.Min(15, primarySize.Height));
                    using (SolidBrush badgeBrush = new SolidBrush(Color.FromArgb(63, 66, 83)))
                    using (GraphicsPath badgePath = UiDrawing.RoundedRectangle(badge, badge.Height / 2))
                        e.Graphics.FillPath(badgeBrush, badgePath);
                    TextRenderer.DrawText(e.Graphics, primaryText, stateFont, badge,
                        Color.FromArgb(221, 223, 232),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }

                if (box.Width >= 42 && box.Height >= 36)
                {
                    string resolution = screen.Bounds.Width + "x" + screen.Bounds.Height;
                    Rectangle resolutionRect = new Rectangle(box.X + 3, box.Y + 17,
                        Math.Max(1, box.Width - 6), 12);
                    TextRenderer.DrawText(e.Graphics, resolution, detailFont, resolutionRect,
                        Color.FromArgb(197, 200, 211),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

                    if (box.Height >= 40)
                    {
                        string stateText = selected ? (box.Width >= 82 ? "Included" : "IN") : (box.Width >= 82 ? "Excluded" : "OUT");
                        Size stateSize = TextRenderer.MeasureText(e.Graphics, stateText, stateFont);
                        int pillWidth = Math.Min(box.Width - 8, stateSize.Width + 10);
                        Rectangle statePill = new Rectangle(box.X + (box.Width - pillWidth) / 2,
                            box.Bottom - 15, pillWidth, 12);
                        Color stateFill = selected ? Color.FromArgb(31, 74, 57) : Color.FromArgb(55, 57, 65);
                    Color stateColor = selected ? Color.FromArgb(151, 214, 178) : Color.FromArgb(190, 192, 200);
                        using (SolidBrush stateBrush = new SolidBrush(stateFill))
                        using (GraphicsPath statePath = UiDrawing.RoundedRectangle(statePill, statePill.Height / 2))
                            e.Graphics.FillPath(stateBrush, statePath);
                        TextRenderer.DrawText(e.Graphics, stateText, stateFont, statePill, stateColor,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    }
                }
            }
        }
    }

    private bool IsTargeted(Screen screen)
    {
        if (targetMode == 0)
            return true;
        if (targetMode == 2)
            return screen.Primary;
        return selectedNames.Contains(screen.DeviceName);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (!selectionEnabled)
            return;
        for (int i = 0; i < displayRects.Length; i++)
        {
            if (!displayRects[i].Contains(e.Location))
                continue;
            string name = screens[i].DeviceName;
            if (selectedNames.Contains(name))
                selectedNames.Remove(name);
            else
                selectedNames.Add(name);
            Invalidate();
            ShowDisplayToolTip(i, e.Location);
            EventHandler handler = SelectionChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
            return;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = -1;
        for (int i = 0; i < displayRects.Length; i++)
            if (displayRects[i].Contains(e.Location))
            {
                index = i;
                break;
            }
        if (index == hoveredDisplay)
            return;

        hoveredDisplay = index;
        ShowDisplayToolTip(index, e.Location);
    }

    private void ShowDisplayToolTip(int index, Point location)
    {
        if (index < 0)
        {
            displayToolTip.Hide(this);
            displayToolTip.SetToolTip(this, String.Empty);
            return;
        }

        Screen screen = screens[index];
        string state = IsTargeted(screen) ? "Included" : "Excluded";
        string primary = screen.Primary ? "Windows primary display" : "Secondary display";
        string tip = String.Format("{0}\n{1} x {2}\n{3} - {4}",
            displayNames[index], screen.Bounds.Width, screen.Bounds.Height, primary, state);
        displayToolTip.SetToolTip(this, tip);
        displayToolTip.Show(tip, this, location.X + 10, location.Y + 10, 8000);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hoveredDisplay = -1;
        displayToolTip.Hide(this);
        displayToolTip.SetToolTip(this, String.Empty);
        base.OnMouseLeave(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && displayToolTip != null)
            displayToolTip.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class DarkContextMenuRenderer : ToolStripProfessionalRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using (SolidBrush brush = new SolidBrush(Color.FromArgb(25, 27, 34)))
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        Rectangle bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        using (Pen pen = new Pen(Color.FromArgb(58, 62, 75)))
            e.Graphics.DrawRectangle(pen, bounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        ToolStripMenuItem menuItem = e.Item as ToolStripMenuItem;
        Color fill = e.Item.Selected || (menuItem != null && menuItem.Checked)
            ? Color.FromArgb(53, 56, 72)
            : Color.FromArgb(25, 27, 34);
        Rectangle bounds = new Rectangle(Point.Empty, e.Item.Size);
        using (SolidBrush brush = new SolidBrush(fill))
            e.Graphics.FillRectangle(brush, bounds);
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
