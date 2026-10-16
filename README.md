# LG Monitor Switcher

Press **Left Ctrl + Right Alt + Enter** to disconnect your LG monitor from Windows. Press the same shortcut again to reconnect it with its original display layout, including its primary display setting.

Run `Launch.cmd`. Closing the window keeps the app running in the notification area; right-click its tray icon to toggle, show the window, or exit. The app must be running for the shortcut to work. It does not currently start automatically with Windows.

This disconnects the PC display output; switch the LG to your Xbox input using the monitor controls. It does not power down the monitor or send Xbox commands. Another active PC display is required. Exactly one LG display must be detected, using its friendly name or LG's GSM monitor identifier.

The original Windows display configuration is saved in `app/saved-layout.json` before disconnecting, so the shortcut can restore it after restarting the app. Keep this file while the LG is disconnected. Reconnect the PC cable if it has been unplugged. Display driver or hardware changes can make a saved configuration unavailable; failures are shown through tray notifications and the app window.

The shortcut uses a global low-level keyboard hook to detect Left Ctrl + Right Alt + Enter at key-down, with repeat suppression. Only the matching Enter press is consumed. Display changes run on a worker thread so the keyboard hook and window stay responsive. Status and errors are logged to app/switcher.log.

## Build

Requires Windows and the .NET 8 SDK. The generated app requires the .NET 8 Desktop Runtime.

```powershell
dotnet publish -c Release -o app
```

For this recovered workspace, the old build cache was also corrupted. Build with a fresh intermediate directory:

```powershell
dotnet publish -c Release -o app -p:BaseIntermediateOutputPath=build-temp/
```

Read-only display diagnostics:

```powershell
app/sam-monitor-switcher.exe --diagnose
```

This writes `app/display-check.json` without changing any displays.

The app uses Windows QueryDisplayConfig and SetDisplayConfig to save and restore the complete active display configuration. Both validation and apply results are checked. If disconnecting fails, it attempts to roll back the original layout. It never deliberately disconnects the final active display.

