# Lights Off for macOS

This repository currently contains the implementation plan. The macOS app has not been written or tested on a Mac.

## Planned first version

- A native AppKit menu bar app with a simple settings window.
- A one-click action that asks attached displays to enter standby.
- An I/O Kit power assertion while **Keep Mac awake** is active. Release the assertion when the session stops or the app exits.
- A clear status for whether each request was accepted. Do not claim that a display physically powered down unless that was observed.

## Display power investigation

Prototype `pmset displaysleepnow` as the display-off request. Test whether it works without administrator access, how it behaves with internal and external displays, and how each display wakes. Do not use `caffeinate -d` for this feature because it prevents the display from sleeping.

The first target is a signed-in desktop session with the Mac lid open. Do not promise that closing a laptop lid leaves background work running. Lid-close behavior is a separate forced-sleep case.

## Verification required

Before calling a macOS build verified, test on a real Mac with one and multiple displays. Check display standby, input wake, app exit, sleep assertion cleanup, timers, and failed requests. Until those checks pass, builds must be labeled experimental.

## References

- [I/O Kit power assertions](https://developer.apple.com/documentation/iokit/iopmlib_h/iopmassertiontypes)
- [AppKit status items](https://developer.apple.com/documentation/appkit/nsstatusitem)
- [`pmset` manual](https://manp.gs/mac/8/pmset)
