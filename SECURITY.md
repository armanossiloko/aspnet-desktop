# Security Policy

AspNetDesktop's job is isolation: the ASP.NET Core API must only be reachable by the app's own WebView, never by other processes or by pages outside the app's origins. Bugs that break that promise are security issues.

## Reporting a vulnerability

Please **do not** open a public issue. Report privately through GitHub:
[**Report a vulnerability**](https://github.com/armanossiloko/aspnet-desktop/security/advisories/new).

Include the OS and adapter (WebView2 / WKWebView / WebKitGTK), the AspNetDesktop version, and a proof of concept if you have one. You'll get an acknowledgement within a few days. A fix and advisory are coordinated with you before anything is disclosed.

## In scope

- Reaching the `ipc://` API from an origin that isn't allow-listed, or from outside the app process
- Bypassing the request token when `RequireRequestToken` is enabled, or leaking it to a non-UI origin
- Navigation-policy bypasses that load untrusted pages into the app's WebView with access to `ipc://`
- Anything that makes the host bind a network socket
- Vulnerabilities in the published packages (`AspNetDesktop*` on NuGet, `aspnetdesktop-client` on npm)

## Out of scope

- Vulnerabilities in your application's own endpoints, or in the system WebView itself (report those to Microsoft, Apple or WebKitGTK)
- Issues in the sample apps' development tooling (`ng serve`, Vite) that don't affect built apps

## Supported versions

Until 1.0, only the latest release receives security fixes.
