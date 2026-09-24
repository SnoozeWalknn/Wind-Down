# Wind Down 1.0 for Linux x64

Wind Down schedules this PC to **Shut Down** or **Sleep** after a duration or at a specific clock time. It is built for CachyOS and works on other systemd-based desktops (Arch, Fedora, openSUSE, Ubuntu, …).

## Install

From this extracted folder, run:

```bash
./install.sh
```

Wind Down is copied to `~/.local/lib/wind-down`, added to your app menu, and registered as the `wind-down` command in `~/.local/bin`. Root access is not needed. The build is self-contained, so you don't need a separate .NET runtime. For reminder notifications, install libnotify: `sudo pacman -S --needed libnotify`.

You can also run `./WindDown` directly from this folder without installing it.

```text
wind-down             Open Wind Down
wind-down status      Show the active schedule
wind-down cancel      Cancel the active schedule
wind-down repair      Restore the command, app-menu entry, and icon
wind-down uninstall   Uninstall Wind Down
```

## Use

- Choose **Shut Down** or **Sleep**.
- Choose **In** for a duration from 1 minute through exactly 24 hours, or **At** for the next occurrence of a clock time.
- Pick an optional reminder, then select the Schedule button.
- If you close the window while a timer is active, Wind Down stays in the system tray. Open Wind Down again to return to the same timer.
- Cancel from the app, the tray menu, the reminder notification, or `wind-down cancel`.

A systemd user timer owns the deadline, so the schedule stays registered if the app closes. Wind Down doesn't wake the PC. It skips missed deadlines and any schedule made before a restart. For Shutdown, Wind Down asks your desktop session (KDE Plasma, GNOME or Xfce) to log out and power off, so apps with unsaved work can object. On other desktops it uses `systemctl poweroff`, which respects inhibitors. Sleep availability depends on the PC.

## Remove

Cancel any active schedule, choose **Exit Wind Down**, then run `wind-down uninstall`. Your preferences stay in `~/.local/share/Wind Down`.
