Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class LightsOffNative {
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint SetThreadExecutionState(uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);
}
'@

$script:active = $false
$script:exiting = $false
$script:ES_CONTINUOUS = [uint32]0x80000000
$script:ES_SYSTEM_REQUIRED = [uint32]0x00000001
$script:HWND_BROADCAST = [IntPtr]0xffff
$script:WM_SYSCOMMAND = [uint32]0x0112
$script:SC_MONITORPOWER = [IntPtr]0xF170

function Request-DisplaysStandby {
    $result = [IntPtr]::Zero
    [void][LightsOffNative]::SendMessageTimeout(
        $script:HWND_BROADCAST,
        $script:WM_SYSCOMMAND,
        $script:SC_MONITORPOWER,
        [IntPtr]::new(2),
        [uint32]0x0002,
        [uint32]1500,
        [ref]$result)
}

function Stop-LightsOff {
    if (-not $script:active) { return }
    [void][LightsOffNative]::SetThreadExecutionState($script:ES_CONTINUOUS)
    $script:active = $false
    $status.Text = 'Session stopped. Windows power settings are back in control.'
    $startButton.Enabled = $true
    $stopButton.Enabled = $false
    $sessionItem.Checked = $false
    $notifyIcon.Text = 'Lights Off is off'
}

$form = New-Object System.Windows.Forms.Form
$form.Text = 'Lights Off'
$form.StartPosition = 'CenterScreen'
$form.Size = New-Object System.Drawing.Size(440, 270)
$form.MinimumSize = $form.Size
$form.MaximumSize = $form.Size
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
$form.MaximizeBox = $false
$form.Font = New-Object System.Drawing.Font('Segoe UI', 10)

$title = New-Object System.Windows.Forms.Label
$title.Text = 'Keep your PC working. Turn the displays off.'
$title.Font = New-Object System.Drawing.Font('Segoe UI', 13, [System.Drawing.FontStyle]::Bold)
$title.AutoSize = $false
$title.Location = New-Object System.Drawing.Point(22, 20)
$title.Size = New-Object System.Drawing.Size(380, 32)
$form.Controls.Add($title)

$body = New-Object System.Windows.Forms.Label
$body.Text = 'Your apps and AI agents keep running while Windows stays awake. Displays enter standby; monitor power is not cut completely.'
$body.Location = New-Object System.Drawing.Point(24, 62)
$body.Size = New-Object System.Drawing.Size(374, 52)
$form.Controls.Add($body)

$startButton = New-Object System.Windows.Forms.Button
$startButton.Text = 'Start Lights Off'
$startButton.Location = New-Object System.Drawing.Point(24, 128)
$startButton.Size = New-Object System.Drawing.Size(175, 42)
$startButton.FlatStyle = [System.Windows.Forms.FlatStyle]::System
$form.Controls.Add($startButton)

$stopButton = New-Object System.Windows.Forms.Button
$stopButton.Text = 'Allow normal sleep'
$stopButton.Location = New-Object System.Drawing.Point(210, 128)
$stopButton.Size = New-Object System.Drawing.Size(188, 42)
$stopButton.Enabled = $false
$form.Controls.Add($stopButton)

$status = New-Object System.Windows.Forms.Label
$status.Text = 'Ready.'
$status.Location = New-Object System.Drawing.Point(24, 186)
$status.Size = New-Object System.Drawing.Size(374, 28)
$form.Controls.Add($status)

$notifyIcon = New-Object System.Windows.Forms.NotifyIcon
$notifyIcon.Icon = [System.Drawing.SystemIcons]::Application
$notifyIcon.Text = 'Lights Off is off'
$notifyIcon.Visible = $true
$menu = New-Object System.Windows.Forms.ContextMenuStrip
$showItem = $menu.Items.Add('Open Lights Off')
$sessionItem = $menu.Items.Add('Keep PC awake')
$sessionItem.CheckOnClick = $true
$sessionItem.Add_Click({
    if ($sessionItem.Checked -and -not $script:active) { $startButton.PerformClick() }
    elseif (-not $sessionItem.Checked -and $script:active) { Stop-LightsOff }
})
[void]$menu.Items.Add('-')
$exitItem = $menu.Items.Add('Exit')
$notifyIcon.ContextMenuStrip = $menu

$showItem.Add_Click({ $form.Show(); $form.WindowState = [System.Windows.Forms.FormWindowState]::Normal; $form.Activate() })
$exitItem.Add_Click({ $script:exiting = $true; Stop-LightsOff; $form.Close() })
$notifyIcon.Add_DoubleClick({ $form.Show(); $form.WindowState = [System.Windows.Forms.FormWindowState]::Normal; $form.Activate() })
$notifyIcon.Add_MouseClick({
    if ($_.Button -eq [System.Windows.Forms.MouseButtons]::Left) {
        Request-DisplaysStandby
    }
})

$offTimer = New-Object System.Windows.Forms.Timer
$offTimer.Interval = 900
$offTimer.Add_Tick({
    $offTimer.Stop()
    if ($script:active) {
        Request-DisplaysStandby
        $notifyIcon.ShowBalloonTip(1800, 'Lights Off is active', 'The PC is staying awake. Displays were asked to enter standby.', [System.Windows.Forms.ToolTipIcon]::Info)
    }
})

$startButton.Add_Click({
    $state = [LightsOffNative]::SetThreadExecutionState($script:ES_CONTINUOUS -bor $script:ES_SYSTEM_REQUIRED)
    if ($state -eq 0) {
        $status.Text = 'Windows could not enable keep-awake mode.'
        return
    }
    $script:active = $true
    $startButton.Enabled = $false
    $stopButton.Enabled = $true
    $sessionItem.Checked = $true
    $status.Text = 'PC awake. Displays will enter standby in a moment.'
    $notifyIcon.Text = 'Lights Off is active'
    $offTimer.Start()
    $form.Hide()
})

$stopButton.Add_Click({ Stop-LightsOff })
$form.Add_FormClosing({
    if (-not $script:exiting -and $script:active) {
        $_.Cancel = $true
        $form.Hide()
        return
    }
    Stop-LightsOff
    $offTimer.Dispose()
    $notifyIcon.Visible = $false
    $notifyIcon.Dispose()
})

[void][System.Windows.Forms.Application]::Run($form)
