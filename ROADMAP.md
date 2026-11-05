# Lights Off roadmap proposal

## Current release

Lights Off v0.1.0 is a portable Windows tray app. It keeps Windows awake, asks connected displays to enter standby, and provides a tray action to stop the session and wake the displays.

## v0.2: Safer sessions

1. Report whether Windows accepted the keep-awake and display power requests. Show a clear tray notification when a request fails.
2. Add session lengths: 30 minutes, 1 hour, 2 hours, custom duration, end at a selected clock time, and until stopped.
3. Show the remaining time in the tray tooltip. When a timed session ends, release the keep-awake request and return Windows to its normal power settings. Do not send a display-on command when stopping.

Redlight is a separate Windows accessibility app in `Redlight/`. It uses the Windows full-screen Magnification API instead of a desktop overlay.

Cross-platform planning for Lights Off is in [PLATFORM_SUPPORT.md](PLATFORM_SUPPORT.md). The first targets are a native macOS menu bar app and Linux X11 plus explicitly detected Wayland compositor support. Mac and Linux builds remain experimental until tested with real displays.

### Acceptance checks

- A test workload continues running while Lights Off is active and the displays are in standby.
- The tray state clearly distinguishes an active session from a failed display request.
- A timed session ends at the selected time and releases the keep-awake request without sending a display-on command.
- An indefinite session continues until the user stops it or exits Lights Off.
- A custom duration and selected clock time produce the expected end time, including the next-day case.

## Redlight application

- Use the Windows Magnification API for a full-desktop color transform.
- Provide an intensity slider, two red color modes, global shortcuts, a tray icon, and startup controls.
- Restore the previous magnification and color settings on disable and exit, with a recovery helper for an unexpected app exit.
- Verify system-wide behavior on the target Windows version before claiming compatibility.

## v0.3: Faster controls

- Add a configurable keyboard shortcut to start or stop a session.
- Keep the tray menu as a visible fallback when the shortcut is unavailable or conflicts with another app.
- Add a small settings screen for the shortcut and default session length.

## Later distribution work

- Implement and verify Mac and Linux power-control adapters using the platform plan.
- Add a custom app icon and improve first-run instructions.
- Automate versioned GitHub releases from tags.
- Consider code signing if distribution beyond a small group makes Windows reputation warnings a problem.

## Out of scope for now

- Sending vendor-specific DDC/CI power commands to force monitor power-off. Support and wake behavior vary by monitor, and the tested standby request already works on the target setup.
- Detecting whether a particular AI agent is running. Lights Off should remain useful for any long-running workload.
