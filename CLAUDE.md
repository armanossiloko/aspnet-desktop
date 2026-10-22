# AspNetDesktop

Build, test and contribution details are in [CONTRIBUTING.md](CONTRIBUTING.md).

## Commits

- Every commit message and pull request title must follow [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/): `type(scope): description`. See [CONTRIBUTING.md](CONTRIBUTING.md#commit-messages) for the allowed types and scopes.
- Write the description in the imperative, starting lowercase, with no trailing period, and keep the header within 100 characters. Put details in the body.
- Mark breaking changes with `!` before the colon and a `BREAKING CHANGE:` footer.
- commitlint enforces this in the husky commit-msg hook and in the **Conventional commits** CI check. Never bypass the hook with `--no-verify`; reword the message instead.

## Releases

- Commit types drive releases ([docs/releasing.md](docs/releasing.md)): `feat`, `fix`, `perf`, `revert` and breaking changes release the component whose files changed. `src/client` is the npm client; everything else is the NuGet packages. Use `docs`, `chore`, `ci`, `build`, `test`, `refactor` or `style` for changes that shouldn't ship a release.
- Never edit `CHANGELOG.md`, `src/client/CHANGELOG.md`, `.release-please-manifest.json` or package versions by hand, and never push release tags: release-please owns them.
