# Lights Off for Linux (experimental)

This is an experimental first version for Linux **X11** sessions. It is a separate Python/Tkinter front end with two independent actions:

- **Turn monitors off now** asks the X11 server to enter DPMS standby through `xset`. Mouse or keyboard activity normally wakes the display. Lights Off does not send a wake command.
- **Keep PC awake** takes a logind `idle:sleep` inhibitor over D-Bus. It does not turn off or wake displays. The inhibitor file descriptor belongs to this app, so logind releases it when the app exits.

Wayland is detected and reported as unsupported in this prototype. XWayland does not give an application authority over physical monitor power. Do not claim Wayland support until an output-power protocol adapter has been implemented and tested against named compositors.

## Requirements

- Python 3.10 or newer
- Tkinter (`python3-tk` on Debian and Ubuntu)
- `xset` (`x11-xserver-utils` on Debian and Ubuntu)
- A systemd-logind session for the keep-awake action
- `dbus-next` from `requirements.txt`

On Debian or Ubuntu:

```sh
sudo apt install python3 python3-tk python3-venv x11-xserver-utils
python3 -m venv .venv
.venv/bin/pip install -r requirements.txt
./lights-off-linux
```

To add it to the desktop application menu, copy the entire `linux/` folder to `~/.local/lib/lights-off/`, change `Exec=lights-off-linux` in `lights-off.desktop` to `Exec=/home/USER/.local/lib/lights-off/lights-off-linux` (using your actual home path), then copy the desktop file to `~/.local/share/applications/`.

## Status and limits

- This prototype has not been built or tested on a Linux machine or real display.
- It currently provides a small window and desktop launcher, not a tray icon. Linux desktop tray support differs by environment.
- It only targets all displays through the X11 DPMS request; per-monitor selection is not implemented.
- The status reports that the request was accepted by X11, not that every physical monitor powered down.
- Keep-awake is indefinite in this prototype. Timers and global shortcuts are not implemented yet.
- If logind is absent or denies the inhibitor, the app shows an error and does not claim that sleep is blocked.

Use a local X11 session for initial testing. This build is an experimental prototype, not a verified Linux release.
