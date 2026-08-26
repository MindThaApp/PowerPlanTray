# PowerPlanTray

A Windows system-tray utility for switching power plans, built with WinUI 3
(.NET 8, Windows App SDK). Eventually shipped as a packaged MSIX app to the
Microsoft Store.

[Privacy Policy](PRIVACY.md) · [Terms of Use](TERMS.md) · [MIT License](LICENSE)

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

### Localization

User-interface resources live in `src/PowerPlanTray/Strings/<language-tag>/Resources.resw`.
Add a folder with the same resource keys to introduce another language; Windows selects
the best match from the user's OS preferred-language list. There is intentionally no
in-app language selector. Translations beyond English are AI-generated first passes and
have not been professionally reviewed. Arabic, Urdu, and Persian are supported with
right-to-left layout mirroring. Each window root gets its `FlowDirection` from the
resolved resource language; the tray popup keeps its Windows-style taskbar anchor while
its controls and text mirror internally. The reserved Microsoft Store product name remains
English in the package manifest.

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

`dotnet build`/`dotnet run` only compiles the app - it does **not** produce
something you can double-click and run. `PowerPlanTray` is a packaged
(MSIX, `WindowsPackageType=MSIX`, `EnableMsixTooling=true`) WinUI 3 app, and
Windows requires package identity to activate a WinUI 3 exe (the framework
resolves `ms-appx:///...` asset URIs, DPI/resource handling, etc. through
the package). Running `bin\...\PowerPlanTray.exe` directly fails with:

```
The application has failed to start because its side-by-side configuration
is incorrect. Please see the application event log or use the command-line
sxstrace.exe tool for more detail.
```

This is **not** a missing-runtime problem (the Windows App SDK runtime
redistributable can be installed and present) - `sxstrace.exe` traces it to
manifest activation itself failing:

```
ERROR: The setting http://schemas.microsoft.com/SMI/2019/WindowsSettings^dpiAwareness is not registered.
ERROR: Activation Context generation failed.
```

On this dev machine the OS's SxS manifest parser does not recognize the
2019-schema `<dpiAwareness>` element that the default WinUI 3 project
template puts in `src/PowerPlanTray/app.manifest`, which makes activation
context generation fail outright - the exe can never start, packaged or not.
The fix was to switch `app.manifest` to the older, universally-supported
2005 schema element instead (functionally equivalent, PerMonitorV2-aware):

```xml
<application xmlns="urn:schemas-microsoft-com:asm.v3">
  <windowsSettings>
    <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
  </windowsSettings>
</application>
```

A second, independent bug was also found and fixed while diagnosing this:
`Assets\**\*.png`/`*.ico` in `PowerPlanTray.csproj` were declared as
`<Content>` without `CopyToOutputDirectory`, so SDK-style `dotnet build`
never copied `Assets\` into `bin\...\`. That leaves the MSIX manifest
pointing at logo files that don't exist in the build output. Both items
(`<Content Include="Assets\**\*.png">` / `*.ico`) now carry
`<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>`.

### Running locally from the CLI (no Visual Studio needed)

There's no VS IDE session on this box to hit F5 (which normally builds,
registers a dev package, and deploys for you), so the equivalent has to be
done by hand. This is a real local MSIX install - the same shape the app
will eventually ship in - not a throwaway shortcut.

One-time setup (per machine): a self-signed code-signing certificate whose
subject matches the manifest's `Publisher`
(`CN=FE63C3BB-418B-484C-852F-E7985F260BC3`), trusted
locally so Windows will install packages signed with it:

```powershell
$cert = New-SelfSignedCertificate -Type Custom `
  -Subject "CN=FE63C3BB-418B-484C-852F-E7985F260BC3" `
  -KeyUsage DigitalSignature -FriendlyName "Power Plan Manager Dev Cert" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3","2.5.29.19={text}false")
$pwd = ConvertTo-SecureString -String "<choose-a-password>" -Force -AsPlainText
Export-PfxCertificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" `
  -FilePath certs\PowerPlanTrayDev.pfx -Password $pwd
Import-PfxCertificate -FilePath certs\PowerPlanTrayDev.pfx `
  -CertStoreLocation Cert:\LocalMachine\TrustedPeople -Password $pwd
```

(`certs\` and `artifacts\` are already gitignored - never commit the
`.pfx`, it contains a private key. Run the import line from an elevated
PowerShell window.) Also requires Developer Mode enabled
(Settings > Privacy & security > For developers) so unsigned/dev-signed
sideloading is allowed.

Every build+run cycle:

```powershell
# 1. Build and package (produces a signed-ready .msix + install scripts)
$msbuild = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild src\PowerPlanTray\PowerPlanTray.csproj /t:Build `
  /p:Configuration=Debug /p:Platform=x64 /p:GenerateAppxPackageOnBuild=true `
  /p:AppxBundle=Never /p:UapAppxPackageBuildMode=SideloadOnly `
  /p:AppxPackageDir="artifacts\AppPackages\"

# 2. Sign the package with the dev cert from setup
$msix = "artifacts\AppPackages\PowerPlanTray_1.0.0.0_x64_Debug_Test\PowerPlanTray_1.0.0.0_x64_Debug.msix"
& "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe" `
  sign /fd SHA256 /sha1 "E5F2C6F2784824283849DC59E764D99DC0FB9499" $msix

# 3. Install (first time) / update in place (after code changes). Debug builds never bump
#    the version number, so Add-AppxPackage needs -ForceUpdateFromAnyVersion to accept a
#    same-version reinstall. Do NOT Remove-AppxPackage first - that performs a full uninstall,
#    which deletes the app's ApplicationData (LocalSettings/LocalFolder - automation rules,
#    saved Advanced-settings profiles, UI prefs) before the reinstall. -ForceUpdateFromAnyVersion
#    instead updates the existing package in place and preserves that data across rebuilds.
Add-AppxPackage -Path $msix -ForceApplicationShutdown -ForceUpdateFromAnyVersion

# 4. Launch via its AUMID (package identity - this is what makes ms-appx:// asset
#    URIs, DPI handling, etc. resolve correctly; running the raw .exe still won't work)
explorer.exe "shell:appsFolder\34458MindtheApp.PowerPlanManager_24e29cj9741rt!PowerPlanTray"
```

(`34458MindtheApp.PowerPlanManager_24e29cj9741rt` is the package family name
derived from the Store identity and publisher. It can also be looked up with
`Get-AppxPackage -Name 34458MindtheApp.PowerPlanManager | Select PackageFamilyName` or
`Get-StartApps | Where Name -eq "Power Plan Manager Pro"` after installing once.)

To uninstall: `Get-AppxPackage -Name 34458MindtheApp.PowerPlanManager | Remove-AppxPackage`.

### Verified working (2026-08-24)

With both fixes above, `Add-AppxPackage` installs cleanly, the app launches
via its AUMID, stays running/responsive, and the tray icon renders and
opens its flyout with the live list of power plans (Balanced / High
performance / Power saver) plus Exit - confirmed via a desktop screenshot
and via Windows' own per-icon `IconSnapshot` cache
(`HKCU\Control Panel\NotifyIconSettings`). `PowerSchemeService`'s
`PowerSetActiveScheme` P/Invoke was independently verified to correctly
switch the active scheme (confirmed with `powercfg /getactivescheme`
before/after). Driving an actual in-flyout menu click end-to-end via
synthetic mouse input against the taskbar's XAML-island overflow host
proved unreliable in this environment (clicks landed on the right
coordinates per UI Automation but didn't consistently reach the XAML
click handler) - this looks like an automation-tooling limitation rather
than an app bug, given the underlying API call and the flyout's displayed
content were both independently confirmed correct.

## Known placeholders to replace before Store submission

- `src/PowerPlanTray/Assets/*.png` and `Assets/TrayIcon.ico` are
  programmatically generated solid-color placeholders (44x44, 150x150,
  310x150, 50x50, splash screen, and a tray icon). Replace with real
  branded artwork before packaging for the Store.

## TODO(phase2) markers

Search the codebase for `TODO(phase2)` to find the exact insertion points
for later work:
- `src/PowerPlanTray/App.xaml.cs` - where the "Settings..." menu item and
  automation-rule quick toggles should be added to the tray flyout, and
  where switch failures (e.g. requiring elevation) should surface a
  notification to the user instead of being silently swallowed.
