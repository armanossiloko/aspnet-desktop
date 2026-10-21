# Changelog

All notable changes to this project are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0]

First release.

### Added

- `AspNetDesktop.Hosting`: `DesktopServer`, an `IServer` that runs the full ASP.NET Core pipeline in-process without binding a port; `builder.AddDesktopHost()`, `host.RunDesktop(platform)`, `CreateClient()` / `CreateDesktopClient()` for tests.
- `app://localhost` (UI) and `ipc://localhost` (API) schemes with scheme-aware routing: APIs answer on `ipc://` only and `MapFallback*` on `app://` only; `RequireDesktopScheme(...)` to override.
- Isolation policy: `ipc://` origin allow-list, host-managed CORS, `app://` restricted to GET/HEAD, optional per-process request token, navigation policy that keeps the WebView on UI origins and opens web links in the system browser.
- Adapters: Windows (WinForms + WebView2), macOS (NSWindow + WKWebView), Linux (GTK 4 + WebKitGTK 6 via GirCore).
- `AspNetDesktop` metapackage: `app.RunDesktop()` picks the adapter from the target framework.
- `aspnetdesktop-client` npm package: `createDesktopHttpClient()` for vanilla/React and `provideDesktopHttpClient()` / `DesktopHttpClient` for Angular.
- `AspNetDesktop.Templates`: `dotnet new aspnetdesktop-angular` and `dotnet new aspnetdesktop-react`.

[Unreleased]: https://github.com/armanossiloko/aspnet-desktop/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/armanossiloko/aspnet-desktop/releases/tag/v0.1.0
