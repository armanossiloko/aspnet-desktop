#!/usr/bin/env bash
# Works out which CI jobs a change needs. Reads the changed paths (one per line) on stdin and prints GITHUB_OUTPUT
# lines: client, build, smoke and pack (true/false) and codeql (a JSON array of CodeQL languages).
#
# Paths that no job reads (docs, changelogs, release-please state, repository settings) need nothing. Paths this
# script doesn't recognise run everything, so new kinds of files are never skipped by accident.
set -euo pipefail

client=false build=false smoke=false pack=false
csharp=false javascript=false actions=false
lockfile=false manifests=false

everything() {
  client=true build=true smoke=true pack=true
  csharp=true javascript=true actions=true
}

every_npm_job() {
  client=true build=true smoke=true pack=true
}

while IFS= read -r path; do
  path="${path%$'\r'}"
  [ -n "$path" ] || continue

  case "$path" in
    package.json | */package.json) manifests=true ;;
  esac

  case "$path" in
    *.md | LICENSE | docs/* | .release-please-manifest.json | release-please-config.json | \
      .github/ISSUE_TEMPLATE/* | .github/CODEOWNERS | .github/dependabot.yml | .gitignore | .husky/* | \
      commitlint.config.mjs)
      ;;

    # CI itself.
    .github/workflows/ci.yml | .github/workflows/codeql.yml | .github/scripts/*)
      everything ;;

    .github/workflows/* | .github/actionlint.yaml)
      ;;

    # The npm client: its tests, the sample frontends built against it, the smoke test (which loads its build in a
    # real WebView) and the npm package (whose version the templates pin).
    src/client/*)
      client=true smoke=true pack=true ;;

    # The sample frontends are built by the client job and become the templates.
    samples/*/ClientApp/*)
      client=true pack=true ;;

    # The root manifest (workspaces, overrides): every job that runs npm.
    package.json)
      every_npm_job ;;

    # A lockfile change counts for the workspaces whose package.json changed with it (see below).
    package-lock.json)
      lockfile=true ;;

    tests/AspNetDesktop.SmokeTests/*)
      build=true smoke=true ;;

    tests/*)
      build=true ;;

    samples/* | templates/*)
      build=true pack=true ;;

    # The libraries and the build settings they share.
    src/* | *.slnx | Directory.Build.props | Directory.Packages.props | global.json | .editorconfig)
      build=true smoke=true pack=true ;;

    assets/*)
      pack=true ;;

    *)
      everything ;;
  esac

  case "$path" in
    *.cs | *.csproj | *.cshtml | *.razor | Directory.Packages.props)
      csharp=true ;;
    *.js | *.mjs | *.cjs | *.ts | *.mts | *.cts | *.tsx | *.jsx | *.vue | *.html | *.htm)
      javascript=true ;;
    .github/workflows/* | .github/actions/*)
      actions=true ;;
  esac
done

# A lockfile change without any package.json change (e.g. a transitive update) can affect any npm build.
if [ "$lockfile" = true ] && [ "$manifests" = false ]; then
  every_npm_job
fi

languages=()
[ "$actions" = false ] || languages+=('"actions"')
[ "$csharp" = false ] || languages+=('"csharp"')
[ "$javascript" = false ] || languages+=('"javascript-typescript"')

echo "client=$client"
echo "build=$build"
echo "smoke=$smoke"
echo "pack=$pack"
echo "codeql=[$(IFS=,; echo "${languages[*]}")]"
