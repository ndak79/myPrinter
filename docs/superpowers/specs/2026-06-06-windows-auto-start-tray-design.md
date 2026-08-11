# Windows Auto Start Tray Design

## Goal

Smart Printer starts automatically when the current Windows user signs in, defaults to running hidden in the system tray, and exposes a right-click tray menu option to turn that behavior on or off.

## Architecture

The desktop app owns this feature. A new `WindowsStartupService` manages one per-user Task Scheduler entry named `SmartPrinterAutoStart` through `schtasks.exe`. Task Scheduler is preferred over Registry `Run` or Startup-folder shortcuts because the app manifest requests administrator privileges, and the task can be created with highest privileges to avoid repeated login-time UAC prompts.

`Program.Main` reads a `--start-hidden` command-line switch and passes it to `MainForm`. `MainForm` suppresses only the first automatic show when that switch is present; later tray clicks, double-clicks, and secondary-instance activation still show the window normally.

## Tray UX

The tray menu adds a checked item labelled `Start with Windows`. It reflects the scheduled task state when the menu is created. Clicking it enables or disables the task and updates the checkmark immediately. Errors are shown as concise Windows message boxes because the tray menu has no richer surface.

## Default Behavior

On normal startup, after the desktop host has initialized, the app ensures the scheduled task exists unless the user has explicitly turned the tray option off. The task launches the current executable with `--start-hidden`, so Windows login starts Smart Printer in the tray without displaying the main window or the startup balloon tip.

When the user turns auto-start off, the desktop app writes a small opt-out preference under the current user's application data folder. That prevents the next manual launch from immediately recreating the scheduled task.

## Testing

Unit tests cover command construction through an injectable process runner, default enablement behavior without executing `schtasks.exe`, and source-level compatibility checks for the tray toggle and `--start-hidden` startup flow. Manual verification covers the compiled app because actual Task Scheduler mutation is OS-local and privileged.

## References

- Microsoft `schtasks` docs: `ONLOGON` schedules tasks at user logon and `/RL HIGHEST` requests the highest run level.
