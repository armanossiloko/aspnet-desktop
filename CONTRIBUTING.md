# Contributing to AspNetDesktop

Thanks for helping! Issues, ideas and pull requests are all welcome. Please follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Before you start

- **Bugs**: open an issue with the OS, adapter (WebView2 / WKWebView / WebKitGTK), WebView version and a minimal repro.
- **Features or API changes**: open an issue or discussion first so we can agree on the shape before you invest time.
- **Security issues**: don't open a public issue. See [SECURITY.md](SECURITY.md).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (see `global.json`)
- Node.js 22+ and npm
- For the macOS adapter: `dotnet workload install macos` (and Xcode on a Mac to run it). Without the workload the Mac project is skipped automatically.
- For running on Linux: GTK 4 and WebKitGTK 6 (`sudo apt install libgtk-4-1 libwebkitgtk-6.0-4`). See [docs/platforms.md](docs/platforms.md).

## Build and test

```bash
npm ci                         # npm workspaces: the client package and both samples
npm run build:client
npm run test:client            # vitest: vanilla + Angular clients

dotnet build AspNetDesktop.slnx
dotnet test --solution AspNetDesktop.slnx    # Hosting tests: no WebView needed
```

### End-to-end smoke test

`tests/AspNetDesktop.SmokeTests` opens a real window on the current OS, runs `fetch()` checks against `app://` and `ipc://` from JavaScript and exits with `0` (pass) or `1` (fail). CI runs it on Windows, Linux and macOS.

```bash
npm run build:client
dotnet run --project tests/AspNetDesktop.SmokeTests            # Windows, or Linux with a display
xvfb-run -a dotnet run --project tests/AspNetDesktop.SmokeTests # Linux without a display

# macOS: `dotnet run` hands app bundles to `open` and doesn't wait, so run the bundle's executable:
dotnet build tests/AspNetDesktop.SmokeTests
"$(find tests/AspNetDesktop.SmokeTests/bin/Debug/net10.0-macos -maxdepth 2 -name '*.app' | head -n 1)/Contents/MacOS/AspNetDesktop.SmokeTests"
```

If you change an adapter, run the smoke test on that OS.

### Samples and templates

The samples double as the `dotnet new` templates (`samples/*/.template.config`). Inside this repository, `samples/Directory.Build.targets` points them at the source projects and the npm workspace; the templates package (`templates/AspNetDesktop.Templates`) copies them without that file and stamps the package version in.

```bash
dotnet run --project samples/AngularApp -c Release --launch-profile "AngularApp (app://)"
npm run start:react & dotnet run --project samples/ReactApp     # Debug + Vite hot reload
```

## Pull requests

- Keep changes focused; one topic per PR.
- Add or update tests: Hosting behavior goes in `tests/AspNetDesktop.Hosting.Tests`, client behavior in `src/client/test`, WebView behavior in the smoke test.
- Public APIs need XML docs. Builds treat warnings as errors.
- Match the existing style (`.editorconfig`); file-scoped namespaces, explicit types where the codebase uses them.
- Update `CHANGELOG.md` under **Unreleased** for user-visible changes.
- CI must be green on all three operating systems.

## Releasing (maintainers)

See [docs/releasing.md](docs/releasing.md).
