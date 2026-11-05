# macOS and Linux support plan

## Status

Planning document. Sleepwalk has been tested on Windows. There is no Mac or Linux machine available for hands-on testing right now, so builds for those platforms must be described as prototypes until display-off and wake behavior has been checked on real hardware.

## User-facing behavior

The cross-platform feature should keep the existing simple model:

1. One click on the tray or menu bar button starts a session.
2. Sleepwalk asks the operating system to keep the computer awake, then asks every active display to enter its real standby or power-save state.
3. A second click stops the session, requests that displays wake, and releases the keep-awake request.
4. A timer may stop the session automatically. Closing or crashing Sleepwalk must release the keep-awake request when the operating system permits it.
5. Show whether the keep-awake request and display request were accepted. Do not report that a panel physically powered down unless that was actually observed.

The display request must not be a black window or a rendered black image. Sleepwalk should use the operating system's display power path. “Off” means the display's supported standby or power-save mode, not a guarantee that all power has been physically disconnected from the monitor.

## Backend boundary

Keep the session behavior separate from platform calls. Each platform backend should expose the same small set of operations:

- `CheckSupport`: identify the session type, display interface, and whether required OS services are available.
- `BeginKeepAwake`: acquire a session-owned sleep-prevention handle and return a clear error if it cannot be acquired.
- `RequestDisplaysOff`: request standby for all active outputs and report per-output success or failure when the platform exposes it.
- `RequestDisplaysOn`: request wake for outputs Sleepwalk previously asked to turn off.
- `EndKeepAwake`: release the handle. This must also happen when the app exits normally, and should happen automatically when the OS cleans up resources owned by a crashed process.

The app should track separate states for the keep-awake handle and display requests. If turning the displays off fails, release the keep-awake handle instead of silently leaving the computer held awake. If only some outputs fail, report that partial result and provide a way to restore the outputs that did change.

Do not change the user's global power settings. Do not use fake screen activity to keep the machine awake.

## macOS adapter

- Use an AppKit `NSStatusItem` for the menu bar control. A small SwiftUI settings window is optional.
- Use an I/O Kit power assertion to prevent idle system sleep while a session is active. Release the assertion when the session stops. The OS can still sleep for reasons such as low battery or thermal emergencies, and an app cannot prevent forced sleep such as the user choosing Sleep.
- Prototype a separate display-off request using `pmset displaysleepnow`. Test whether it works without administrator permission, across internal and external displays, and how input wakes each display. Do not use `caffeinate -d` for this feature because that assertion prevents the display from sleeping.
- Do not promise that closing a Mac laptop lid leaves background work running. Lid-close behavior is a separate forced-sleep case and depends on Apple's supported clamshell conditions.

References: [Apple I/O Kit power assertions](https://developer.apple.com/documentation/iokit/iopmlib_h/iopmassertiontypes), [Apple sleep and wake guidance](https://developer.apple.com/library/archive/qa/qa1340/_index.html), [AppKit status items](https://developer.apple.com/documentation/appkit/nsstatusitem), [caffeinate manual](https://manp.gs/mac/8/caffeinate).

## Linux adapter

### Keep-awake request

Prefer a logind inhibitor for `idle` and `sleep` while a session is active. A small prototype can use `systemd-inhibit`; a packaged app should consider calling logind over D-Bus directly so the app owns and releases the inhibitor handle. If logind is absent or refuses the request, show that the PC may still suspend. A blocking sleep inhibitor can also stop normal user sleep requests while active, so make the stop action easy to reach.

Reference: [systemd-inhibit manual](https://man7.org/linux/man-pages/man1/systemd-inhibit.1.html).

### Display-off request by session type

- **X11:** use the X.Org DPMS extension to request `off` for the active display server. The `xset dpms force off` command is useful for a first prototype. Production code should detect whether DPMS is available and report failure instead of assuming every display server supports it.
- **Wayland:** first check whether the compositor advertises `zwlr_output_power_manager_v1`. If it does, use that protocol to request each output off or on. The protocol is explicitly unstable, and compositor support varies by release. If it is absent, add and test a named desktop-specific adapter before claiming support for that desktop. Do not treat X11 compatibility tools as a universal Wayland solution.
- **Tray integration:** Linux desktop environments do not all provide the same status-icon behavior. Keep the same Start/Stop action, but allow a keyboard shortcut or launcher window as a fallback when the desktop does not show a tray icon.

References: [X.Org DPMS extension](https://www.x.org/releases/current/doc/xextproto/dpms.html), [X.Org `xset` manual](https://xorg.freedesktop.org/archive/X11R6.7.0/doc/xset.1.html), [Wayland output power protocol and compositor support list](https://wayland.app/protocols/wlr-output-power-management-unstable-v1).

## Code structure recommendation

Keep the current Windows application working as it is. Do not replace the Windows app or its interface as part of the first Mac/Linux prototype.

Define the behavior and backend boundary first. Then create native platform hosts around it:

- Windows: existing C# WinForms app and its current Windows power calls.
- macOS: a small AppKit menu bar app with the I/O Kit assertion and a separately tested display-off adapter.
- Linux: a small GTK or Qt tray/indicator host with logind plus X11 DPMS and a runtime-detected Wayland adapter.

Avoid choosing a shared UI framework until the Mac display-off command and Linux Wayland support have been proven. A UI rewrite cannot solve missing operating-system display controls.

## Build and verification stages

1. Keep the Windows implementation as the behavior reference. Add unit tests around session state transitions and mocked backend results.
2. Add a macOS build job on a macOS CI runner. This can verify compilation and power-assertion API usage, but not physical display standby or wake.
3. Add Linux build jobs for X11 and Wayland libraries. Use virtual displays or compositor test environments to check protocol discovery, error handling, and cleanup.
4. Before calling either platform verified, test on a real Mac or Linux desktop with one and multiple monitors. Check off, wake, timer expiry, disconnect/reconnect, user input, app exit, and failed display requests.
5. Until that hardware pass is complete, label the platform build experimental and list the exact tested OS, desktop, compositor, and display setup. Never generalize one compositor result to all Linux systems.

## Initial support targets

- macOS: native Mac app, foreground user session, desktop open. Laptop lid-closed work is outside the first target.
- Linux X11: a desktop session with a working X.Org DPMS extension and logind sleep inhibition.
- Linux Wayland: only compositor versions that advertise the output-power protocol or have a separately tested adapter.

These are engineering targets, not compatibility claims. Hardware testing is still required before a verified release.
