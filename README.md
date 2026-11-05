# Quiet Run

A small Windows tray utility for leaving the PC awake while sending connected displays into standby.

## Start it

Double-click **Start Quiet Run.cmd**. Click **Start quiet run**. The app stays in the notification area and requests that Windows remain awake. After a short delay, it asks displays to enter standby. Click the Quiet Run tray icon and choose **Stop and wake displays** to end the session.

The keep-awake request is released if Quiet Run exits. Closing the window while a session is active hides it to the tray; use the tray menu's **Exit** command to close the app.

## Limits

- This keeps Windows in its normal running state. It does not make CPU or GPU work continue through Sleep.
- Display standby is not the same as cutting power to the monitor. The monitor may use a small amount of standby power.
- Windows and monitor firmware control display wake behavior. Keyboard or mouse activity may wake the displays, and some monitors may not respond to the software wake request.
- The PC must stay signed in with this app running for its keep-awake request to remain active.

Quiet Run uses only Windows PowerShell, Windows Forms, and Windows APIs included with Windows. It does not install a service or require administrator access.
