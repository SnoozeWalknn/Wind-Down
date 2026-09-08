# Wind Down 1.0 for Windows x64

Wind Down schedules this PC to **Shut Down** or **Sleep** after a duration or at a specific clock time.

## Install

Keep this folder together. Run **Install.ps1** with PowerShell for a permanent per-user installation. Wind Down is copied to `%LOCALAPPDATA%\Programs\Wind Down` and a **Wind Down** Start-menu shortcut is created. Administrator rights and separate runtime installation are not required.

You can also run **Wind Down.exe** directly from this folder without installing it.

## Use

- Choose **Shut Down** or **Sleep**.
- Choose **In** for a duration from 1 minute through exactly 24 hours, or **At** for the next occurrence of a clock time.
- Pick an optional reminder and select the Schedule button.
- Close an active timer to keep it in the notification area. Open Wind Down again to return to the same timer.
- Cancel from the app, notification-area menu, or reminder notification.

Windows owns the scheduled deadline, so the timer remains registered if the visible app closes. Wind Down does not wake the PC and skips missed deadlines or schedules from before a Windows restart. Shutdown does not force applications with unsaved work to close. Sleep availability depends on the PC.

## Remove

Cancel any active schedule, choose **Exit Wind Down**, and run **Uninstall.ps1** from the installed application folder. The uninstaller refuses to remove files while an active schedule still references them.

This release is unsigned. Windows may show its standard warning for software from an unknown publisher.
