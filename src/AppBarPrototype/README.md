# AppBarPrototype (experimental, separate from FloatingDock)

Run `dotnet run --project src/AppBarPrototype/AppBarPrototype.csproj -c Release` to open the explicit test window. **Registrar AppBar** requests a 72-DIP strip on the primary monitor's left edge; Escape, the Exit button, or closing the window removes the AppBar registration. The original taskbar is neither hidden nor moved. `--smoke` registers for about 1.8 seconds, prints read-only taskbar/work-area measurements before and after, removes the registration, and exits. Run only on a desktop session where a brief work-area change is acceptable.

## Protocol and scope

`AppBarRegistration` implements `ABM_NEW`, then `ABM_QUERYPOS` and `ABM_SETPOS`, applies the final rectangle returned by the Shell, reports `ABM_WINDOWPOSCHANGED`, and calls `ABM_REMOVE` on normal shutdown. Any failed initial negotiation attempts removal before reporting failure. The UI also responds to AppBar position/state notifications and display, DPI, and setting changes. The prototype is a separate process/window and has no reference from FloatingDock.

The calls and data structure are documented Shell32 APIs. Normal maximized windows should use the reduced work area supplied by the Shell; borderless/full-screen applications can cover an ordinary AppBar. `ABN_FULLSCREENAPP` is observed and reported; this sample does not try to control another application's z-order. Multi-monitor monitor selection beyond the monitor containing the prototype, auto-hide behavior, and a production-quality DPI/multi-monitor policy are not implemented.

The Microsoft API documentation describes AppBars generically and does not specify a Windows 11 build 26200 exception. The older `NativeTrial` note about build 26200 concerns its separate taskbar auto-hide-state experiment, not this prototype. Validate actual placement on each supported Windows build.

## Functions a replacement would still need

- **Start and Search:** keyboard shortcuts remain OS-owned, but public desktop APIs do not offer a supported way for this AppBar to host the built-in Start/Search taskbar flyouts. Keep the Windows taskbar accessible for parity.
- **Notification area:** `Shell_NotifyIcon` supports icons owned by this application; it is not a supported API for reproducing every third-party tray icon and its interactions.
- **Clock/calendar:** a custom clock/calendar can be drawn, but it does not reproduce the shell's integrated calendar and notification flyouts.
- **Notifications and Quick Settings:** apps can use documented app notification APIs for their own notifications; the system Action Center/Quick Settings surfaces are shell UI, not public AppBar extensions. Keep the original taskbar accessible.
- **Maximized/full-screen windows:** AppBar reservation informs the Shell work area for ordinary maximized windows. Exclusive/borderless fullscreen and some games may cover the bar; this sample does not override that behavior.

## Recovery

Normal close/Exit/Escape performs `ABM_REMOVE`; the close is canceled if the API reports failure so the user can retry. A forced process termination bypasses managed cleanup. The reservation is associated with the registered window and the Shell normally releases it when that window/process is destroyed, but this experiment does not claim crash-proof cleanup. To inspect, compare the taskbar rectangle (`ABM_GETTASKBARPOS`), auto-hide state (`ABM_GETSTATE`), and work area (`SPI_GETWORKAREA`) against the values before launch. First ensure AppBarPrototype has exited; wait briefly for the Shell's geometry notification and re-read. If a discrepancy remains, sign out and back in to rebuild the desktop session; do not alter the taskbar registry or use undocumented Shell internals. This prototype never changes the taskbar's own position or auto-hide state.
