# Wind Down 1.0 for Windows x64

Wind Down schedules this PC to **Shut Down** or **Sleep** after a duration or at a specific clock time.

## Install or run portably

You can run **Wind Down.exe** directly from this folder without installing it. Keep the entire folder together.

For a permanent per-user installation, run **Install.ps1** with PowerShell. Wind Down is copied to `%LOCALAPPDATA%\Programs\Wind Down`, added to the Start menu and Windows Installed Apps, and registered as the `wind-down` command. Administrator rights and separate runtime installation are not required.

After installation:

```text
wind-down             Open Wind Down
wind-down repair      Restore its Windows integration
wind-down uninstall   Uninstall Wind Down
```

## Use

- Choose **Shut Down** or **Sleep**.
- Choose **In** for a duration from 1 minute through exactly 24 hours, or **At** for the next occurrence of a clock time.
- Pick an optional reminder and select the Schedule button.
- Close an active timer to keep it in the notification area. Open Wind Down again to return to the same timer.
- Cancel from the app, notification-area menu, or reminder notification.

Windows owns the scheduled deadline, so the timer remains registered if the visible app closes. Wind Down does not wake the PC and skips missed deadlines or schedules from before a Windows restart. Shutdown does not force applications with unsaved work to close. Sleep availability depends on the PC.

## Remove

Cancel any active schedule, choose **Exit Wind Down**, and run `wind-down uninstall`. You can also uninstall Wind Down from **Settings → Apps → Installed apps** or run **Uninstall.ps1** directly. The uninstaller refuses removal while an active Wind Down schedule still references the installation.

This release is unsigned. Windows may show its standard warning for software from an unknown publisher.
