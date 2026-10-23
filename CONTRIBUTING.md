# Contributing to AspNetDesktop

Thanks for helping! Issues, ideas and pull requests are all welcome. Please follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Before you start

- **Bugs**: open an issue with the OS, adapter (WebView2 / WKWebView / WebKitGTK), WebView version and a minimal repro.
- **Features or API changes**: open an issue or discussion first so we can agree on the shape before you invest time.
- **Security issues**: don't open a public issue. See [SECURITY.md](SECURITY.md).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (see `global.json`)
- Node.js 22+ and npm
- For the macOS adapter: `dotnet workload install macos` (and Xcode on a Mac to run it). Without the workload the Mac project is skipped automatically (`-p:AspNetDesktopBuildMac=false|true` to force).
- For running on Linux: GTK 4 and WebKitGTK 6 (`sudo apt install libgtk-4-1 libwebkitgtk-6.0-4`). See [docs/platforms.md](docs/platforms.md).

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

## Build and test

```bash
npm ci                         # npm workspaces: the client package and the sample frontends
npm run build:client
npm run test:client            # vitest: vanilla, Angular, React and Vue clients

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
dotnet run --project samples/AngularApp -c Release              # built UI from app://
npm run start:react & dotnet run --project samples/ReactApp     # Debug + Vite hot reload (same for start:vue / VueApp)
```

`dotnet new` evaluates `#if` in `.cs` files as its own template conditions and strips the directives from the generated app. Wrap real preprocessor directives in `//-:cnd:noEmit` and `//+:cnd:noEmit` lines (see `Program.cs`), and don't write that marker anywhere else in a template file, comments included. To check the output, pack the templates and instantiate one:

```bash
dotnet pack templates/AspNetDesktop.Templates -c Release -o artifacts -p:SkipClientBuild=true
dotnet new install ./artifacts/AspNetDesktop.Templates.*.nupkg --debug:custom-hive /tmp/template-hive
dotnet new aspnetdesktop-vue -o /tmp/MyApp --debug:custom-hive /tmp/template-hive
```

## Commit messages

Commit messages and pull request titles follow [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/):

```text
<type>(<optional scope>): <description>

<optional body>

<optional footers>
```

- **Types**: `feat`, `fix`, `docs`, `perf`, `refactor`, `test`, `build`, `ci`, `chore`, `style`, `revert`.
- **Scopes** (optional): `hosting`, `windows`, `mac`, `linux`, `client`, `samples`, `templates`, `deps`.
- Write the description in the imperative, starting lowercase, without a trailing period: `fix(linux): read request bodies natively`. Keep the header within 100 characters.
- Mark breaking changes with `!` before the colon (`feat(hosting)!: rename DesktopOptions`) and a `BREAKING CHANGE:` footer.
- The type decides what gets released: `feat`, `fix`, `perf` and breaking changes trigger a release of the component they touch (the npm client for `src/client`, the NuGet packages for everything else), and become its changelog entry. See [docs/releasing.md](docs/releasing.md).

`npm ci` installs a commit-msg hook ([husky](https://typicode.github.io/husky/) + [commitlint](https://commitlint.js.org/)) that rejects messages that aren't Conventional Commits and warns about style (subject case, header length). The **Conventional commits** check applies the same rules to pull requests. Squash merges use the pull request title as the commit message, so the title must follow the format too. Renaming a pull request doesn't start CI; to check the new title, re-run the failed jobs of its last CI run. `npm run lint:commits` checks every commit on your branch against `origin/master`.

## Pull requests

- Keep changes focused; one topic per PR.
- Add or update tests: Hosting behavior goes in `tests/AspNetDesktop.Hosting.Tests`, client behavior in `src/client/test`, WebView behavior in the smoke test.
- Public APIs need XML docs. Builds treat warnings as errors.
- Match the existing style (`.editorconfig`); file-scoped namespaces, explicit types where the codebase uses them.
- Don't edit `CHANGELOG.md`: release-please writes the changelogs from commit messages, so describe user-visible changes in a `feat` or `fix` commit. See [docs/releasing.md](docs/releasing.md).
- **CI result** must pass. CI runs only the jobs your changes affect: for example, a docs-only change runs just the commit check, and a change to the npm client skips the .NET builds. [`.github/scripts/affected-jobs.sh`](.github/scripts/affected-jobs.sh) decides, and files it doesn't recognise run everything.

## Releasing (maintainers)

See [docs/releasing.md](docs/releasing.md).
