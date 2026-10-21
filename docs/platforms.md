# Platform notes

`app.RunDesktop()` picks the adapter from your app's target framework. Target the right one per OS (the templates do this for you):

```xml
<TargetFramework Condition="$([MSBuild]::IsOSPlatform('Windows'))">net10.0-windows</TargetFramework>
<TargetFramework Condition="$([MSBuild]::IsOSPlatform('OSX'))">net10.0-macos</TargetFramework>
<TargetFramework Condition="'$(TargetFramework)' == ''">net10.0</TargetFramework>
```

To use your own adapter, register an `IDesktopPlatform` in DI or call `app.RunDesktop(platform)` from `AspNetDesktop.Hosting`.

## Windows: WebView2 (`net10.0-windows`)

- **Runtime**: the [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/). It ships with Windows 11 and current Windows 10 builds. If it's missing, the app shows a message with the download link and exits.
- **Window**: WinForms. `RunDesktop()` runs the message loop on its own STA thread, so `Main` doesn't need `[STAThread]` (top-level statements can't have it anyway).
- **Schemes**: registered through `CoreWebView2CustomSchemeRegistration` before the environment is created (`TreatAsSecure`, `HasAuthorityComponent`, `AllowedOrigins` = the `ipc://` allow-list). Requests are answered from `WebResourceRequested` with a deferral, so ASP.NET Core runs on the thread pool.
- **Profile**: `Desktop:UserDataFolder`, default `%LOCALAPPDATA%\<ApplicationName>\WebView`.
- **App icon**: the window uses the executable's icon (`<ApplicationIcon>` in your project).
- The `AspNetDesktop.Windows` package drops WebView2's WPF assembly reference unless your project sets `UseWPF`, avoiding MSB3277 warnings in WinForms-only apps.

## macOS: WKWebView (`net10.0-macos`)

- **Build**: needs the macOS workload (`dotnet workload install macos`) and, to build app bundles, the Xcode version that workload expects.
- **Window**: `NSWindow` with an app menu (Hide, Quit) and an Edit menu so copy/paste/undo shortcuts reach the WebView. `RunDesktop()` must be called on the main thread (the default for `Main`).
- **Schemes**: one `WKURLSchemeHandler` serves `app://` and `ipc://`; request bodies are read from `HTTPBody` or `HTTPBodyStream`.
- **Quit**: Cmd+Q and closing the window stop the ASP.NET Core host gracefully instead of `exit()`-ing.
- **Dev tools**: in Development, the Web Inspector is enabled (`isInspectable` on macOS 13.3+). Right-click → Inspect Element.

## Linux: WebKitGTK 6 (`net10.0`)

- **Runtime packages**: GTK 4 and WebKitGTK 6.0 (`webkitgtk-6.0`, WebKitGTK 2.40+, which exposes custom-scheme request bodies).
  - Debian/Ubuntu (24.04+): `sudo apt install libgtk-4-1 libwebkitgtk-6.0-4`
  - Fedora: `sudo dnf install gtk4 webkitgtk6.0`
  - Arch: `sudo pacman -S gtk4 webkitgtk-6.0`
- **Bindings**: [GirCore](https://gircore.github.io/) (`GirCore.Gtk-4.0`, `GirCore.WebKit-6.0`), loaded at runtime from the system libraries.
- **Schemes**: registered on the WebKit context with `RegisterUriScheme` and marked secure and CORS-enabled through the security manager.
- **Window**: `Gtk.ApplicationWindow` in a `Gtk.Application` (non-unique, so several instances can run). The GLib synchronization context keeps scheme responses on the GTK thread.
- **Headless/CI**: run under a virtual display, e.g. `xvfb-run -a ./MyApp`. Some CI containers block the unprivileged user namespaces WebKit's sandbox uses; see the CI workflow for the settings we use.

## Behavior shared by all adapters

| | |
|---|---|
| Start URL | `app://localhost/` + `Desktop:StartPath`, or `Desktop:DevServerUrl` in Development |
| `window.__ASPNETDESKTOP__` | `{ ipcBase, appBase, platform, token? }`, injected at document start |
| Navigation | UI origins stay in the WebView; `http(s)`, `mailto:`, `tel:` open in the default app; everything else is blocked |
| New windows | `target=_blank` / `window.open` never opens a second window: UI links load in place, web links open externally |
| Shutdown | closing the window stops the host; stopping the host (`IHostApplicationLifetime.StopApplication`) closes the window |
| Dev tools | enabled in Development by default (`Desktop:EnableDevTools`) |
