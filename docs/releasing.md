# Releasing

Releases are driven by git tags. Pushing `vX.Y.Z` runs `.github/workflows/release.yml`, which:

1. runs the full CI workflow (build and tests on Windows/Linux/macOS, WebView smoke tests on all three, packing, template checks);
2. packs the NuGet packages (version from the tag via [MinVer](https://github.com/adamralph/minver)) and the npm package (version set from the tag);
3. publishes `AspNetDesktop`, `AspNetDesktop.Hosting`, `.Windows`, `.Mac`, `.Linux` and `.Templates`, and the npm client as `@armanossiloko/aspnetdesktop-client`, to **GitHub Packages**;
4. if enabled, also publishes to **nuget.org** (with symbols) and **npmjs.com** (`aspnetdesktop-client`, with provenance);
5. creates the GitHub Release with the packages attached and the changelog section as notes.

Tags with a pre-release suffix (`v0.2.0-preview.1`) publish NuGet pre-releases, use the `next` npm dist-tag and are marked as pre-releases on GitHub.

## GitHub Packages (default)

Nothing to set up: the workflow publishes with its own `GITHUB_TOKEN`. Packages are linked to this repository through `RepositoryUrl` (NuGet) and `repository` (npm), and are public because the repository is.

- **Consumers need a token** with `read:packages`, even for public packages. The README's [Install](../README.md#install) section shows the one-time setup.
- **npm names must be scoped** on GitHub Packages, so the client is published as `@armanossiloko/aspnetdesktop-client`. Apps install it under its usual name with an alias (`"aspnetdesktop-client": "npm:@armanossiloko/aspnetdesktop-client@^x.y.z"`), so imports don't change. While `PUBLISH_NPMJS` is off, the templates are packed that way, with a `ClientApp/.npmrc` that routes the scope to GitHub Packages.
- **No symbol server**: GitHub Packages doesn't host `.snupkg` files. They're attached to the GitHub Release, and Source Link still maps to the sources.

## nuget.org and npmjs.com (opt-in)

Turn each registry on with a repository variable once its credentials (below) are in place:

```bash
gh variable set PUBLISH_NUGET_ORG --repo armanossiloko/aspnet-desktop --body true
gh variable set PUBLISH_NPMJS --repo armanossiloko/aspnet-desktop --body true
```

With `PUBLISH_NPMJS` on, the templates reference `aspnetdesktop-client` from npmjs.com again (no alias, no `.npmrc`). Afterwards, point the README's Install section and badges at the public registries.

These publish jobs run in the `release` environment, which only accepts `v*` tags. A failure in either one fails the release; a registry that is off is skipped.

### NuGet (pick one)

**Trusted publishing (recommended, no long-lived secrets)**

1. On nuget.org, go to your account → **Trusted Publishing** → **Create**, with:
   - Repository owner: `armanossiloko`
   - Repository: `aspnet-desktop`
   - Workflow file: `release.yml`
   - Environment: `release`
2. Store your nuget.org **username** (not email) as a repository variable:
   ```bash
   gh variable set NUGET_USER --repo armanossiloko/aspnet-desktop --body "<nuget.org username>"
   ```

**API key**

Create a key on nuget.org with *Push new packages and package versions* for the glob `AspNetDesktop*`, then:

```bash
gh secret set NUGET_API_KEY --repo armanossiloko/aspnet-desktop --env release
```

When `NUGET_USER` is set, the workflow uses trusted publishing; otherwise it falls back to `NUGET_API_KEY`.

### npm

npm trusted publishing can only be configured once the package exists, so the first release needs a token:

1. On npmjs.com create a **granular access token** with read and write access to packages and scopes (enable *Bypass two-factor authentication* if your account requires 2FA for publishing), then:
   ```bash
   gh secret set NPM_TOKEN --repo armanossiloko/aspnet-desktop --env release
   ```
2. After the first release, open the package on npmjs.com → **Settings** → **Trusted Publisher** → GitHub Actions with owner `armanossiloko`, repository `aspnet-desktop`, workflow `release.yml`, environment `release`. Then delete the `NPM_TOKEN` secret and, optionally, set the package to require 2FA and disallow tokens.

## Cutting a release

1. Make sure `master` is green.
2. Move the **Unreleased** entries in `CHANGELOG.md` under a new version heading and update the compare links at the bottom.
3. Tag and push:
   ```bash
   git tag v0.1.0
   git push origin v0.1.0
   ```
4. Watch the **Release** workflow. If publishing fails after CI passed, fix the cause and re-run the failed jobs: pushes use `--skip-duplicate`, so packages that already went out are skipped.

## Versions between releases

Untagged commits build as the next patch's preview, e.g. `0.1.1-preview.0.5` (5 commits after `v0.1.0`). CI uploads these packages as workflow artifacts; they are not published.
