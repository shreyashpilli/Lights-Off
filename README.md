# Lights Off

A small Windows tray utility for leaving the PC awake while sending connected displays into standby. It runs without administrator access and does not install a service.

## Start it

For a Lights Off build, extract the Windows ZIP, then double-click **Start Lights Off.cmd** or **LightsOff.exe**. Click **Start Lights Off**. The app stays in the notification area and requests that Windows remain awake. After a short delay, it asks displays to enter standby. Left-click the tray icon again or choose **Stop session** to release the keep-awake request. Lights Off does not send a display-on command when you stop; displays respond to your input normally. The published v0.1.0 archive is historical and still has the older filenames.

The keep-awake request is released if Lights Off exits. Left-click the Lights Off tray icon to start or stop a session. Closing the window while a session is active hides it to the tray; right-click the tray icon for settings or the **Exit** command. The ZIP is portable and does not require an installer.

## Session length

Choose **Until stopped**, **30 minutes**, **1 hour**, **2 hours**, **Custom duration**, or **End at a specific time** before starting. A custom duration can be up to 999 hours and 59 minutes, and must be at least 1 minute. For a clock time that has already passed today, Lights Off schedules the end for tomorrow. Timed sessions show the remaining time in the tray tooltip. When the time runs out, Lights Off asks Windows to wake the displays and returns the PC to its normal power settings. The default is **Until stopped**, which matches the behavior of v0.1.0.

Lights Off reports whether Windows accepted the keep-awake and display requests. Windows accepting a display request does not guarantee every monitor followed it; monitor firmware and connection type still affect standby and wake behavior.

## Redlight

The separate **Redlight** app applies a color matrix through the Windows full-screen Magnification API. It is designed to recolor the whole desktop, including other applications, without placing a tinted window over the screen. See [Redlight/README.md](Redlight/README.md) for its build and run instructions, recovery shortcut, and API limitations.

## Build from source

On Windows with .NET Framework 4.x, run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`. The build script compiles `LightsOff.cs`, runs a smoke check that does not alter display power, and prints the executable's SHA-256 hash. After building, **Start Lights Off.cmd** launches `dist\LightsOff.exe`. The PowerShell prototype remains as a fallback for checkouts that have not been built; use the compiled app for session timers and request status feedback.

## Limits

- This keeps Windows in its normal running state. It does not make CPU or GPU work continue through Sleep.
- Display standby is not the same as cutting power to the monitor. The monitor may use a small amount of standby power.
- Windows and monitor firmware control display wake behavior. Keyboard or mouse activity wakes the displays; Lights Off does not force them on when a session stops.
- The PC must stay signed in with this app running for its keep-awake request to remain active.

Lights Off uses only Windows PowerShell, Windows Forms, and Windows APIs included with Windows. It does not install a service or require administrator access.

The executable is currently unsigned. Windows may show an unfamiliar-app warning because the file has no publisher reputation yet. Verify the SHA-256 hash published with each release before running it.
