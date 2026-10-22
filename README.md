# AspNetDesktop

[![CI](https://github.com/armanossiloko/aspnet-desktop/actions/workflows/ci.yml/badge.svg)](https://github.com/armanossiloko/aspnet-desktop/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/armanossiloko/aspnet-desktop?include_prereleases&logo=github&label=release)](https://github.com/armanossiloko/aspnet-desktop/releases)
[![Packages: GitHub Packages](https://img.shields.io/badge/packages-GitHub%20Packages-24292f?logo=github)](https://github.com/armanossiloko?tab=packages&repo_name=aspnet-desktop)
[![License: MIT with attribution](https://img.shields.io/badge/license-MIT%20with%20attribution-blue.svg)](https://github.com/armanossiloko/aspnet-desktop/blob/master/LICENSE)

Build desktop apps with an **ASP.NET Core backend** and **any JavaScript UI** (Angular, React, Vue, ...), Tauri-style — but with .NET instead of Rust, and with real MVC instead of `invoke('command')`.

The full ASP.NET Core pipeline (controllers, minimal APIs, model binding, filters, auth, ProblemDetails, JSON) runs **in-process with no Kestrel and no TCP port**. The system WebView talks to it over two custom schemes:

| Scheme | Purpose | Who uses it |
|---|---|---|
| `app://localhost` | UI origin: static files + SPA fallback | The WebView loads the app from here |
| `ipc://localhost` | API origin: your controllers / minimal APIs | Only `DesktopHttpClient` |

Nothing on the machine can reach your API: there is no socket to connect to.

```mermaid
flowchart LR
  spa[Angular / React / Vue]
  spa -->|"HttpClient / fetch https://..."| net[Real network]
  spa -->|"DesktopHttpClient ipc://localhost/api/..."| webview[System WebView]
  webview --> adapter[Platform adapter]
  adapter --> server["DesktopServer (IServer)"]
  server --> pipeline[ASP.NET Core pipeline]
  pipeline --> mvc[Controllers & minimal APIs]
```

## Get started

### Install

The packages are on [GitHub Packages](https://github.com/armanossiloko?tab=packages&repo_name=aspnet-desktop) for now; nuget.org and npm will follow. GitHub Packages needs a token even for public packages, so once per machine:

1. Create a [personal access token (classic)](https://github.com/settings/tokens/new?scopes=read:packages&description=AspNetDesktop%20packages) with the `read:packages` scope.
2. Register it with NuGet and npm:

   ```bash
   # On Windows you can leave out --store-password-in-clear-text (the password is encrypted there).
   dotnet nuget add source https://nuget.pkg.github.com/armanossiloko/index.json --name aspnetdesktop --username <github-user> --password <token> --store-password-in-clear-text
   npm config set //npm.pkg.github.com/:_authToken <token>
   ```

### Create an app

**From a template** (Angular, React or Vue, with the build wiring and hot reload set up):

```bash
dotnet new install AspNetDesktop.Templates
dotnet new aspnetdesktop-vue -o MyApp          # or: aspnetdesktop-angular, aspnetdesktop-react
cd MyApp
dotnet run -c Release                           # builds the UI into wwwroot and opens the window
```

**In an existing project**:

```bash
dotnet add package AspNetDesktop

# In your frontend: route the @armanossiloko scope to GitHub Packages, then install under the usual name.
echo "@armanossiloko:registry=https://npm.pkg.github.com" >> .npmrc
npm install aspnetdesktop-client@npm:@armanossiloko/aspnetdesktop-client
```

Apps created from the templates already have that `.npmrc` and dependency.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddDesktopHost();          // replaces Kestrel; no TCP bind
builder.Services.AddControllers();

var app = builder.Build();
app.UseStaticFiles();              // Angular/React/Vue dist served from app://
app.MapControllers();              // real MVC / minimal APIs on ipc://
app.MapFallbackToFile("index.html");
app.RunDesktop();                  // opens the window; returns when it closes
```

The target framework selects the adapter (see [docs/platforms.md](https://github.com/armanossiloko/aspnet-desktop/blob/master/docs/platforms.md) for runtime requirements):

| Target framework | Adapter |
|---|---|
| `net10.0-windows` | WinForms + **WebView2** |
| `net10.0-macos` | `NSWindow` + **WKWebView** (`WKURLSchemeHandler`) |
| `net10.0` (Linux) | GTK 4 + **WebKitGTK 6** via GirCore |

The templates and samples pick the framework automatically:

```xml
<TargetFramework Condition="$([MSBuild]::IsOSPlatform('Windows'))">net10.0-windows</TargetFramework>
<TargetFramework Condition="$([MSBuild]::IsOSPlatform('OSX'))">net10.0-macos</TargetFramework>
<TargetFramework Condition="'$(TargetFramework)' == ''">net10.0</TargetFramework>
```

## Two clients: real HTTP vs the in-process API

The framework never patches `fetch` or Angular's `HttpClient`. Real HTTP stays real; the desktop API gets its own client from [`aspnetdesktop-client`](https://github.com/armanossiloko/aspnet-desktop/tree/master/src/client).

**Angular**

```ts
// app.config.ts
providers: [provideHttpClient(), provideDesktopHttpClient()]

// component
private http = inject(HttpClient);            // https://... over the network
private desktop = inject(DesktopHttpClient);  // ipc://localhost, in-process

this.http.get<Weather>('https://api.weather.example/forecast');
this.desktop.get<Metadata>('/api/metadata');
this.desktop.post<Order>('/api/orders', body);
```

`DesktopHttpClient` *is* an `HttpClient` (same `get/post/put/patch/delete`, generics, `params`, `observe`, `HttpErrorResponse`), constructed with its own backend. Your app's interceptors don't run on it; pass `provideDesktopHttpClient({ interceptors: [...] })` for desktop-only ones.

**React**

```tsx
// main.tsx
import { DesktopHttpProvider } from 'aspnetdesktop-client/react';

createRoot(root).render(<DesktopHttpProvider><App /></DesktopHttpProvider>);

// component
const desktop = useDesktopHttpClient();      // ipc://localhost, in-process
const metadata = await desktop.get<Metadata>('/api/metadata');
await fetch('https://api.weather.example/forecast');   // real HTTP, untouched
```

**Vue**

```ts
// main.ts
import { createDesktopHttp } from 'aspnetdesktop-client/vue';

createApp(App).use(createDesktopHttp()).mount('#app');

// <script setup>
const desktop = useDesktopHttpClient();      // or this.$desktop in the Options API
const metadata = await desktop.get<Metadata>('/api/metadata');
```

**Svelte, Solid, vanilla, ...**

```ts
import { createDesktopHttpClient, DesktopHttpError } from 'aspnetdesktop-client';

const desktop = createDesktopHttpClient();
try {
  await desktop.post<Order>('/api/orders', { product: 'Tea', quantity: 1 });
} catch (e) {
  if (e instanceof DesktopHttpError && e.status === 400) showErrors(e.error); // ValidationProblemDetails
}
```

The React, Vue and vanilla clients share one promise-based API (`get/post/put/patch/delete/request`, `DesktopHttpError`), so they work directly as a TanStack Query or SWR fetcher. The provider and the plugin also take a `client` option, so you can swap in a fake in tests.

All clients resolve against `window.__ASPNETDESKTOP__.ipcBase` (injected by the host) and refuse absolute URLs that aren't on `ipc://`, so real HTTP can't be sent through them by mistake.

## How it works

- **`DesktopServer`** (`AspNetDesktop.Hosting`) implements `IServer`. `StartAsync` binds nothing; it keeps the `IHttpApplication<TContext>`. Each request gets its own `IFeatureCollection` (request, buffered response with `OnStarting`/`OnCompleted`, body, lifetime, identifier) and runs through the normal pipeline. Everything above `IServer` works unchanged.
- **Adapters** turn native WebView requests into `DesktopHttpRequest` and call one method:
  ```csharp
  Task<DesktopHttpResponse> ProcessAsync(DesktopHttpRequest request, CancellationToken ct);
  ```
  - Windows registers `app`/`ipc` with `CoreWebView2CustomSchemeRegistration` *before* the WebView2 environment is created (the piece that makes `fetch('ipc://...')` work) and answers `WebResourceRequested` with a deferral, so ASP.NET Core runs on the thread pool, not the UI thread.
  - macOS uses `WKURLSchemeHandler` for both schemes (method, headers, `HTTPBody`/`HTTPBodyStream`).
  - Linux uses `WebContext.RegisterUriScheme` (`GetHttpMethod`/`GetHttpHeaders`/`GetHttpBody`/`FinishWithResponse`) and marks both schemes secure and CORS-enabled.
- **Scheme-aware routing**: endpoints answer on `ipc://` only; `MapFallback*` endpoints answer on `app://` only. A relative `fetch('/api/x')` from the UI lands on `index.html`, never on a controller, and an unknown `ipc://localhost/api/...` route is a real 404 rather than `index.html`. Override per endpoint with `.RequireDesktopScheme(DesktopScheme.App | DesktopScheme.Ipc)` or `[RequireDesktopScheme(...)]` (useful for `MapStaticAssets()`).
- **`CreateClient()`**: the .NET sibling of `DesktopHttpClient`, for tests or in-process callers. It goes through the same security policy:
  ```csharp
  await app.StartAsync();
  using HttpClient client = app.CreateDesktopClient();   // BaseAddress = ipc://localhost/
  var metadata = await client.GetFromJsonAsync<Metadata>("/api/metadata");
  ```

## Security model

- No loopback socket. `ASPNETCORE_URLS`, `UseUrls()` and launch-profile URLs are ignored; nothing is bound.
- `ipc://` checks `Origin` against an allow-list: `app://localhost` plus `Desktop:AllowedOrigins`, plus `Desktop:DevServerUrl` **in Development only**. Other origins get a 403 without CORS headers.
- CORS headers are emitted by the host for allow-listed origins only (never `*`); preflights are answered before the pipeline.
- `app://` only accepts `GET`/`HEAD`.
- Top-level navigation is restricted to `app://localhost` (and the dev server in Development). `http(s)`/`mailto`/`tel` links open in the system browser; everything else (`file:`, `ipc:`, `javascript:`, ...) is blocked. `target=_blank` never opens a second window.
- Optional defense in depth: `RequireRequestToken = true` generates a per-process token, exposes it only to UI-origin documents as `__ASPNETDESKTOP__.token`, and rejects `ipc://` requests without it. Both clients send it automatically.

## Configuration

Bound from the `Desktop` section of configuration, then from `AddDesktopHost(options => ...)`:

```json
{
  "Desktop": {
    "Title": "My App",
    "Width": 1280,
    "Height": 800,
    "Maximized": false,
    "StartPath": "/",
    "DevServerUrl": "http://localhost:4200",
    "AllowedOrigins": [],
    "AllowRequestsWithoutOrigin": true,
    "RequireRequestToken": false,
    "EnableDevTools": null,
    "UserDataFolder": null
  }
}
```

`DevServerUrl` only applies when the environment is `Development` (put it in `appsettings.Development.json`). `EnableDevTools` defaults to Development only. The WebView profile defaults to `<LocalApplicationData>/<ApplicationName>/WebView`.

If your app is started from another working directory, `AddDesktopHost()` falls back to the `wwwroot` next to the executable. The samples also set `ContentRootPath = AppContext.BaseDirectory` so `appsettings.json` resolves the same way.

## Packages

All packages are published to [GitHub Packages](https://github.com/armanossiloko?tab=packages&repo_name=aspnet-desktop) (see [Install](#install)); the npm client is `@armanossiloko/aspnetdesktop-client` there, installed as `aspnetdesktop-client`.

| Package | |
|---|---|
| `AspNetDesktop` | Metapackage: Hosting + the adapter for your target framework, and `app.RunDesktop()` |
| `AspNetDesktop.Hosting` | `DesktopServer`, options, isolation policy, `AddDesktopHost()`, `CreateClient()`; no UI dependencies |
| `AspNetDesktop.Windows` / `.Mac` / `.Linux` | Platform adapters |
| `AspNetDesktop.Templates` | `dotnet new aspnetdesktop-angular`, `aspnetdesktop-react` and `aspnetdesktop-vue` |
| `aspnetdesktop-client` | `DesktopHttpClient` for Angular, a provider and hook for React, a plugin and composable for Vue, and `createDesktopHttpClient()` for everything else |

## Repository layout

```
src/
  AspNetDesktop.Hosting/      IServer, options, policy, AddDesktopHost, CreateClient, RunDesktop(platform)
  AspNetDesktop.Windows/      net10.0-windows: WinForms + WebView2
  AspNetDesktop.Mac/          net10.0-macos: NSWindow + WKWebView
  AspNetDesktop.Linux/        net10.0: GTK 4 + WebKitGTK 6 (GirCore)
  AspNetDesktop/              metapackage: RunDesktop() picks the adapter for the target framework
  client/                     aspnetdesktop-client (core client + Angular, React and Vue integrations)
samples/
  AngularApp/                 Angular 22 + [ApiController] + minimal API   (also the aspnetdesktop-angular template)
  ReactApp/                   React 19 + Vite + the same API               (also the aspnetdesktop-react template)
  VueApp/                     Vue 3 + Vite + the same API                  (also the aspnetdesktop-vue template)
templates/
  AspNetDesktop.Templates/    packs the samples as dotnet new templates
tests/
  AspNetDesktop.Hosting.Tests/   pipeline + security tests through CreateClient (no WebView)
  AspNetDesktop.SmokeTests/      real window + system WebView on the current OS, fetch() checks against app:// and ipc://
docs/                         platform notes, releasing, original design document
```

## Building from source

Prerequisites: .NET 10 SDK, Node.js 22+. The macOS adapter needs `dotnet workload install macos`; without the workload it is skipped automatically (`-p:AspNetDesktopBuildMac=false|true` to force).

```bash
npm ci                         # npm workspaces: client + both samples
npm run build:client
dotnet build AspNetDesktop.slnx
```

### Running the samples

**Release** (UI served from `app://`): the build runs `npm run build` and copies the output into `wwwroot`.

```bash
dotnet run --project samples/AngularApp -c Release --launch-profile "AngularApp (app://)"
```

**Debug with hot reload**: run the dev server and the desktop process side by side. The window loads the dev server, and `DesktopHttpClient` still calls `ipc://`.

```bash
npm run start:angular          # ng serve on :4200   (or: npm run start:react / start:vue → Vite on :5173)
dotnet run --project samples/AngularApp            # Development profile → Desktop:DevServerUrl
```

### Tests

```bash
dotnet test --solution AspNetDesktop.slnx          # Hosting: MVC, minimal APIs, static files, SPA fallback, CORS, token, no socket
npm run test:client                                # client: vanilla, Angular, React and Vue
dotnet run --project tests/AspNetDesktop.SmokeTests   # real WebView on this OS (Linux: prefix with xvfb-run -a), exit code 0/1
```

CI runs all of these on Windows, Linux and macOS for every push and pull request, then packs the NuGet/npm packages and builds apps from the templates.

## Contributing

Issues and pull requests are welcome. See [CONTRIBUTING.md](https://github.com/armanossiloko/aspnet-desktop/blob/master/CONTRIBUTING.md), and report security issues privately as described in [SECURITY.md](https://github.com/armanossiloko/aspnet-desktop/blob/master/SECURITY.md). Release notes are in [CHANGELOG.md](https://github.com/armanossiloko/aspnet-desktop/blob/master/CHANGELOG.md).

## Not in v1

WebSockets/SignalR (custom schemes don't upgrade to `ws:`; a postMessage transport could come later), streaming responses (responses are buffered before being handed to the WebView), multi-window, native dialogs, installers and auto-update.

## License

MIT with an attribution requirement. See [LICENSE](https://github.com/armanossiloko/aspnet-desktop/blob/master/LICENSE).

You can use, modify and ship AspNetDesktop freely, including in commercial apps. Apps and projects that include it or are derived from it must credit **Arman Ossi Loko** where users can find it: in the documentation, on an About or credits screen, or in a third-party notices file. For example:

> Built with [AspNetDesktop](https://github.com/armanossiloko/aspnet-desktop) by Arman Ossi Loko

Apps created from the templates show this line in their footer.
