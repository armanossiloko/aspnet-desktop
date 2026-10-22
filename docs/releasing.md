# Releasing

Releases are automated with [release-please](https://github.com/googleapis/release-please) and driven by [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/). Nobody picks version numbers or writes tags by hand.

## Components

The repository releases two components, each with its own version, tag, changelog and packages:

| Component | Packages | Changes counted | Tag | Changelog |
|---|---|---|---|---|
| `AspNetDesktop` | `AspNetDesktop`, `.Hosting`, `.Windows`, `.Mac`, `.Linux`, `.Templates` (NuGet) | everything outside `src/client` | `AspNetDesktop-v1.2.3` | [`CHANGELOG.md`](../CHANGELOG.md) |
| `aspnetdesktop-client` | `aspnetdesktop-client` (npm) | `src/client` | `aspnetdesktop-client-v1.2.3` | [`src/client/CHANGELOG.md`](../src/client/CHANGELOG.md) |

A change to the npm client releases only the npm package, and a change to the .NET side (including the samples the templates are built from) releases only the NuGet packages. A commit that touches both counts for both.

## How a release happens

1. Commits land on `master`. Only `feat`, `fix`, `perf`, `revert` and breaking changes (`!` or a `BREAKING CHANGE:` footer) are releasable; `docs`, `chore`, `ci`, `build`, `test`, `refactor` and `style` never trigger a release on their own.
2. On every push to `master`, the **Release** workflow runs release-please. For each component with releasable changes, it opens or updates a release PR (`chore(master): release AspNetDesktop 0.2.0`) that bumps the version and adds the changelog entry.
3. Merge the release PR when you want to ship. You can edit its changelog first.
4. release-please tags the merge commit and creates the GitHub Release. The same workflow run then runs CI and publishes that component's packages, and attaches them to the release.

Versions follow [SemVer](https://semver.org/spec/v2.0.0.html). Before 1.0, `feat` and breaking changes bump the minor version and `fix` the patch; from 1.0, breaking changes bump the major. To force a version, add a `Release-As: 1.0.0` footer to a commit.

Dependabot's commits use the `build(deps)` and `ci(deps)` prefixes, so dependency updates ship with the next release rather than triggering one. To release a dependency update on its own (e.g. a security fix), merge it and push a `fix(deps): ...` commit describing it.

Between releases, CI builds the NuGet packages as previews of the next patch (`0.1.1-preview.0.5` is five commits after `AspNetDesktop-v0.1.0`, via [MinVer](https://github.com/adamralph/minver)) and uploads them as workflow artifacts. They are not published.

## Release PRs and CI

Release PRs are opened by `github-actions[bot]` with the workflow's own `GITHUB_TOKEN`. GitHub creates their CI runs but holds them until a maintainer approves: on the PR, choose **Approve workflows to run** (or approve the runs from the Actions tab), then merge once the checks pass. Merging with *bypass rules* without running them is also safe: the Release workflow runs the full CI again before it publishes anything.

To skip the approval step, give release-please a token whose PRs trigger workflows normally. Create a [fine-grained personal access token](https://github.com/settings/personal-access-tokens/new) for this repository with **Contents**, **Pull requests** and **Issues** set to read and write, then:

```bash
gh secret set RELEASE_PLEASE_TOKEN --repo armanossiloko/aspnet-desktop
```

## Where packages go

### GitHub Packages (always)

Nothing to set up: the workflow publishes with its own `GITHUB_TOKEN`. Packages are linked to this repository through `RepositoryUrl` (NuGet) and `repository` (npm), and are public because the repository is.

- **Consumers need a token** with `read:packages`, even for public packages. The README's [Install](../README.md#install) section shows the one-time setup.
- **npm names must be scoped** on GitHub Packages, so the client is published as `@armanossiloko/aspnetdesktop-client`. Apps install it under its usual name with an alias (`"aspnetdesktop-client": "npm:@armanossiloko/aspnetdesktop-client@^x.y.z"`), so imports don't change. While `PUBLISH_NPMJS` is off, the templates are packed that way, with a `ClientApp/.npmrc` that routes the scope to GitHub Packages.
- **No symbol server**: GitHub Packages doesn't host `.snupkg` files. They're attached to the GitHub Release, and Source Link still maps to the sources.

### nuget.org and npmjs.com (opt-in)

Turn each registry on with a repository variable once its credentials (below) are in place:

```bash
gh variable set PUBLISH_NUGET_ORG --repo armanossiloko/aspnet-desktop --body true
gh variable set PUBLISH_NPMJS --repo armanossiloko/aspnet-desktop --body true
```

With `PUBLISH_NPMJS` on, the templates reference `aspnetdesktop-client` from npmjs.com again (no alias, no `.npmrc`). Afterwards, point the README's Install section and badges at the public registries.

These publish jobs run in the `release` environment, which only accepts deployments from `master`.

#### NuGet (pick one)

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

#### npm

npm trusted publishing can only be configured once the package exists, so the first npmjs.com release needs a token:

1. On npmjs.com create a **granular access token** with read and write access to packages and scopes (enable *Bypass two-factor authentication* if your account requires 2FA for publishing), then:
   ```bash
   gh secret set NPM_TOKEN --repo armanossiloko/aspnet-desktop --env release
   ```
2. After the first release, open the package on npmjs.com → **Settings** → **Trusted Publisher** → GitHub Actions with owner `armanossiloko`, repository `aspnet-desktop`, workflow `release.yml`, environment `release`. Then delete the `NPM_TOKEN` secret and, optionally, set the package to require 2FA and disallow tokens.

## When something fails

The GitHub Release and tag exist as soon as the release PR is merged; packages follow once CI passes. If a later job fails, fix the cause and re-run the failed jobs from the workflow run. Pushes use `--skip-duplicate`, so packages that already went out are skipped. Don't delete the tag: release-please uses it to find the last release.

## Configuration files

- `release-please-config.json`: the components and versioning rules.
- `.release-please-manifest.json`: the last released version of each component. release-please updates it in every release PR; don't edit it by hand.
