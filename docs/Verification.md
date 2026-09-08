# Verification record

Tested on this Windows 11 x64 PC, September 4, 2026. Release configuration, self-contained .NET 8 and Windows App SDK 1.8.260804001. Tests exercise the published executable and real Windows Task Scheduler, not just a mock UI.

## Targeted visual and packaging pass — September 5, 2026

- Built the final self-contained x64 distribution and ran seven inert smoke checks: the 24:00 limit, rejection above 24:00, Windows initialization and cancellation for both Shutdown and Sleep, and cancellation receipt handling. No power API was called.
- Inspected Midnight Dark in the running WinUI app, switched to Light and confirmed the approved light interface remained intact, then restored System and confirmed it followed the current Windows dark-app setting.
- Displayed the full accepted-request banner in Midnight at 24:00. The complete banner, Schedule button, and footer remained visible in the 880-DIP window.
- Confirmed the crescent icon at all nine embedded ICO sizes, in the running taskbar button, and in the installed Start search result.
- Installed from the friend distribution, verified the installed executable and icon hashes matched the distribution, launched through the Wind Down Start-menu shortcut, and confirmed no source, XAML, project, PDB, or test files were installed.
- Ran the shipped uninstaller and confirmed it removed the installation folder, Start-menu shortcut, protocol registration, and all product task identities.
- Inspected the distribution and ZIP contents for legacy branding, source/debug/test clutter, manifest consistency, and expected end-user files.

The earlier 33-check scheduling suite was intentionally not repeated because this pass changed only theme resources, icon assets, documentation, and packaging. Previously verified timing, crash recovery, warning execution, notification cancellation, tray recovery, DST, and physical power behavior were preserved. The seven-check smoke pass covered the scheduling boundary and both action-registration paths most relevant to packaging integrity.

## Automated checks — 33 passed

Duration boundaries include 00:01, 23:59, the exact 24:00 maximum, rejection of 24:01 and 25:00, minute overflow, and negative/zero input. Clock-time rollover, midnight, fractional-second countdown rounding, overdue clamping, and daylight-saving gaps and repeated times also passed. Native boot identity and shutdown privilege availability were read without prompting for elevation.

Real isolated Windows tasks verified legacy task migration and cleanup, registration/read-back, atomic replacement, rejection of stale and duplicate cancellation, deletion, simulated restart invalidation, early-worker rejection, timed launch of the independent worker, durable execution result, and duplicate-worker suppression. The independent worker executed in diagnostic mode. No test called a power API. The final regression run passed all 33 checks.

## Native window checks

- Launched the published app and inspected its rendered WinUI interface and accessibility tree.
- Reviewed setup and active states in dark and light appearance. Confirmed theme restoration after relaunch.
- Typed directly in a clock-hour dial and verified regional AM/PM behavior, including 12 PM.
- Entered 99 minutes: explanatory validation appeared and Schedule was disabled. Up-arrow restored a valid value; scrolling changed the minute value.
- Created a real two-hour Shutdown schedule from the UI. Confirmed power and warning task identities in Windows.
- Closed the active window, reopened it, confirmed the same process and accurate continuing countdown.
- Cancelled through the main button. Confirmed both Windows tasks were removed.
- Created a real two-hour Sleep schedule, inspected its active view, forcibly terminated only Wind Down, and relaunched. The schedule and theme restored correctly.
- Launched the warning task independently without running the power task. Exercised the exact Windows protocol cancellation link used by the notification button. Cancellation reused the existing app and removed both tasks.
- Checked the corrected idle target preview against the current system clock.
- Confirmed the countdown digits are exposed in the accessibility tree after the label correction.
- Replaced a live two-hour schedule with the one-hour preset, verified the replacement countdown, and cancelled it.
- Verified a warning-only launch exits successfully without showing a window.
- Verified direct entry at 23:59, 24:00, 24:01, and 00:01. Selecting hour 24 clamps minutes to 00; direct 24:01 entry disables scheduling; stepping above hour 24 wraps safely.
- Inspected the running 880-DIP scheduling window with the full accepted-request banner. The complete banner, setup controls, primary Schedule button, and footer were visible simultaneously with the approved sizing and spacing unchanged.
- Rechecked the renamed dark and light interfaces, restored the system-theme default, and verified the countdown value remains exposed by accessibility automation.
- Confirmed a hidden active window reopens into the same process, then cancelled the schedule and verified the action and warning tasks were both removed.
- Confirmed no Wind Down power, warning, legacy, or diagnostic tasks remained after testing.

Windows accepted notification submissions, and the warning task and cancellation activation path were verified. Visual toast presentation could not be independently captured by the available window automation. Windows settings and Do Not Disturb can suppress banners; notification acceptance is not claimed as proof of a visible banner.

## Remaining device acceptance checks

The owner physically verified both scheduled Shutdown and scheduled Sleep on this PC before this rename and boundary pass. This pass did not repeat either physical power action. A real OS restart, physical touch input, Narrator speech, high-contrast mode, and moving across monitors with different DPI were not repeated; the previously verified implementations were preserved.

Before a physical power test, save work. Test Sleep first, then Shutdown, using a short timer. Check cancellation separately before allowing the deadline to arrive. Confirm Windows blocks shutdown normally for unsaved work and that Wind Down never forces another user's session to close.

## Shipping notes

The release is unsigned. Public distribution should use the publisher's signing certificate and an appropriate signed installer/package. The supplied per-user installation scripts are included; they do not require administrator rights. Source, tests, documentation, and repeatable build tools are isolated in `Wind-Down-1.0-Project`; the friend-ready `Wind-Down-1.0-Windows-x64` folder contains only runtime and end-user files. The final build writes `BUILD-MANIFEST.json` with SHA-256 hashes of source inputs, shipped binaries, and the icon before creating the ZIP package.
