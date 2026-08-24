# PowerPlanTray

A Windows system-tray utility for switching power plans, built with WinUI 3
(.NET 8, Windows App SDK). Eventually shipped as a packaged MSIX app to the
Microsoft Store.

## Phase 1 (this checkpoint)

Scope: a working solution that builds and shows a tray icon listing the
system's power plans, letting the user switch the active one by clicking.

Out of scope for phase 1 (left for later phases — see `TODO(phase2)` markers
in code):
- Settings window
- Automation rules (e.g. auto-switch on AC/battery, process-based rules)
- Elevation / UAC handling for restricted operations

## Solution layout

```
PowerPlanTray.sln
src/PowerPlanTray.Core/    Plain net8.0 class library. PowrProf.dll P/Invoke
                            wrapper (PowerSchemeService) for enumerating and
                            switching Windows power schemes. No WinRT/UI
                            dependencies so it stays easily testable.
src/PowerPlanTray/          WinUI 3 packaged app (net8.0-windows10.0.19041.0).
                            No visible main window - all interaction happens
                            through a tray icon (H.NotifyIcon.WinUI) whose
                            flyout menu lists power plans and an Exit item.
```

## Building

Requires Visual Studio 2022 with the ".NET Desktop Development" and
"Windows application development" (WinUI/Windows App SDK, "Universal
Windows Platform development" workload includes the needed Windows SDK
components) workloads, or the equivalent standalone Windows SDK + Windows
App SDK tooling for the CLI.

- Visual Studio: open `PowerPlanTray.sln` and build/run the `PowerPlanTray`
  startup project (set it as the startup project if it isn't already).
- CLI: `dotnet build PowerPlanTray.sln` from the solution root once the
  Windows App SDK / WinUI workload tooling is installed. `PowerPlanTray.Core`
  alone builds today with a plain `dotnet build` since it has no Windows-only
  dependencies.

## Known placeholders to replace before Store submission

- `src/PowerPlanTray/Assets/*.png` and `Assets/TrayIcon.ico` are
  programmatically generated solid-color placeholders (44x44, 150x150,
  310x150, 50x50, splash screen, and a tray icon). Replace with real
  branded artwork before packaging for the Store.
- `Package.appxmanifest` uses a placeholder `Publisher` identity
  (`CN=PowerPlanTray`) and unsigned test identity. Replace with the real
  Partner Center publisher identity when configuring Store association.

## TODO(phase2) markers

Search the codebase for `TODO(phase2)` to find the exact insertion points
for later work:
- `src/PowerPlanTray/App.xaml.cs` - where the "Settings..." menu item and
  automation-rule quick toggles should be added to the tray flyout, and
  where switch failures (e.g. requiring elevation) should surface a
  notification to the user instead of being silently swallowed.
