# Sleepwalk

A small Windows tray utility for leaving the PC awake while sending connected displays into standby. It runs without administrator access and does not install a service.

## Start it

Download `Sleepwalk-Windows.zip` from the GitHub Releases page, extract it, then double-click **Start Sleepwalk.cmd** or **Sleepwalk.exe**. Click **Start Sleepwalk**. The app stays in the notification area and requests that Windows remain awake. After a short delay, it asks displays to enter standby. Click the Sleepwalk tray icon and choose **Stop and wake displays** to end the session.

The keep-awake request is released if Sleepwalk exits. Closing the window while a session is active hides it to the tray; use the tray menu's **Exit** command to close the app. The ZIP is portable and does not require an installer.

## Session length

Choose **Until stopped**, **30 minutes**, **1 hour**, **2 hours**, **Custom duration**, or **End at a specific time** before starting. A custom duration can be up to 999 hours and 59 minutes, and must be at least 1 minute. For a clock time that has already passed today, Sleepwalk schedules the end for tomorrow. Timed sessions show the remaining time in the tray tooltip. When the time runs out, Sleepwalk asks Windows to wake the displays and returns the PC to its normal power settings. The default is **Until stopped**, which matches the behavior of v0.1.0.

Sleepwalk reports whether Windows accepted the keep-awake and display requests. Windows accepting a display request does not guarantee every monitor followed it; monitor firmware and connection type still affect standby and wake behavior.

## Red Light

The separate **Red Light** app applies a color matrix through the Windows full-screen Magnification API. It is designed to recolor the whole desktop, including other applications, without placing a tinted window over the screen. See [RedLight/README.md](RedLight/README.md) for its build and run instructions, recovery shortcut, and API limitations.

## Build from source

On Windows with .NET Framework 4.x, run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`. The build script compiles `Sleepwalk.cs`, runs a smoke check that does not turn displays off, and prints the executable's SHA-256 hash. After building, **Start Sleepwalk.cmd** launches `dist\Sleepwalk.exe`. The legacy PowerShell prototype remains as a fallback for checkouts that have not been built; use the compiled app for session timers and request status feedback.

## Limits

- This keeps Windows in its normal running state. It does not make CPU or GPU work continue through Sleep.
- Display standby is not the same as cutting power to the monitor. The monitor may use a small amount of standby power.
- Windows and monitor firmware control display wake behavior. Keyboard or mouse activity may wake the displays, and some monitors may not respond to the software wake request.
- The PC must stay signed in with this app running for its keep-awake request to remain active.

Sleepwalk uses only Windows PowerShell, Windows Forms, and Windows APIs included with Windows. It does not install a service or require administrator access.

The executable is currently unsigned. Windows may show an unfamiliar-app warning because the file has no publisher reputation yet. Verify the SHA-256 hash published with each release before running it.
