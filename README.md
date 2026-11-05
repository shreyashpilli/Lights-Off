# Lights Off

A small Windows tray utility for sending connected displays into standby and optionally keeping the PC awake. Its settings window uses a dark theme, with separate cards for monitor standby and keep-awake sessions. It runs without administrator access and does not install a service.

## Start it

For a Lights Off build, extract the Windows ZIP, then double-click **Start Lights Off.cmd** or **LightsOff.exe**. Left-click the tray icon to ask the monitors to enter standby. This does not change Windows' normal sleep settings. Right-click the icon and turn on **Keep PC awake** to prevent the PC from sleeping; turn it off to return to normal sleep settings. In the app window, choose **30 minutes**, **1 hour**, **2 hours**, or enter a custom number of minutes. The timer returns the PC to its normal sleep settings when it ends. Monitor standby is not a hard power-off. The published v0.1.0 archive is historical and still has the older filenames.

The keep-awake request is released if Lights Off exits. Closing the window while a session is active hides it to the tray; right-click the tray icon to open settings, toggle **Keep PC awake**, or exit. The ZIP is portable and does not require an installer.

## Session length

Keep-awake can be indefinite or timed. The window has three preset durations and a custom whole-minute entry. Timed sessions show the remaining time in the tray tooltip. When the time runs out, Lights Off releases the keep-awake request and returns the PC to its normal sleep settings without changing the displays' current state.

Lights Off reports whether Windows accepted the keep-awake and display requests. Windows accepting a display request does not guarantee every monitor followed it; monitor firmware and connection type still affect standby and wake behavior.

## Redlight

The separate **Redlight** app applies a color matrix through the Windows full-screen Magnification API. It is designed to recolor the whole desktop, including other applications, without placing a tinted window over the screen. See [Redlight/README.md](Redlight/README.md) for its build and run instructions, recovery shortcut, and API limitations.

## Build from source

On Windows with .NET Framework 4.x, run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`. The build script compiles `LightsOff.cs`, runs a smoke check that does not alter display power, and prints the executable's SHA-256 hash. After building, **Start Lights Off.cmd** launches `dist\LightsOff.exe`. The PowerShell prototype remains as a fallback for checkouts that have not been built; use the compiled app for timed sessions and the full tray controls.

## Limits

- This keeps Windows in its normal running state. It does not make CPU or GPU work continue through Sleep.
- Display standby is not the same as cutting power to the monitor. The monitor may use a small amount of standby power.
- Windows and monitor firmware control display wake behavior. Keyboard or mouse activity wakes the displays; Lights Off does not force them on when a session stops.
- The PC must stay signed in with this app running for its keep-awake request to remain active.

Lights Off uses only Windows PowerShell, Windows Forms, and Windows APIs included with Windows. It does not install a service or require administrator access.

The executable is currently unsigned. Windows may show an unfamiliar-app warning because the file has no publisher reputation yet. Verify the SHA-256 hash published with each release before running it.
