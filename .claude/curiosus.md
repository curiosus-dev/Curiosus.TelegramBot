# Curiosus repository conventions

<!-- Synced from https://github.com/curiosus-dev/dotnet-tools, do not edit in place. -->

Shared by all Curiosus .NET libraries. Repository specifics live in the repository `CLAUDE.md`.

## Stack

- .NET libraries published to nuget.org as `Curiosus.*` by the [curiosus-dev](https://www.nuget.org/profiles/curiosus-dev)
  organization; the latest .NET SDK builds every target framework.
- C# with nullable reference types; `LangVersion` is lowered only where old target frameworks require it.
- Tests: xUnit, Moq, FluentAssertions; integration tests run real infrastructure with Testcontainers (Docker).
- Build: Cake (`build.cake` + synced `build/curiosus.cake`), the same pipeline locally and on CI.
- CI/CD: GitHub Actions, NuGet Trusted Publishing, Dependabot security updates.
- Documentation: `README.md` files; larger libraries also have MkDocs sites on Read the Docs built from `docs/`.

## Use Curiosus packages first

Before writing a helper or adding a third-party dependency, check whether a Curiosus package already solves it
(`Curiosus.Tools`, `Curiosus.Configuration`, `Curiosus.DAL`, `Curiosus.EMail`, `Curiosus.Migrations`, ... — see the
Available packages tables of [Curiosus.Utils](https://github.com/curiosus-dev/Curiosus.Utils) and the other repositories).
If it almost fits, improve the package instead of working around it. Third-party packages come in only when no Curiosus
package covers the need.

## Build, test, pack

Everything goes through Cake, locally and on CI, so a local run reproduces CI:

```bash
dotnet tool restore                                   # once after clone: Cake and ReportGenerator
dotnet cake                                           # Default: clean, build, all tests
dotnet cake --target=Build                            # clean + build
dotnet cake --target=UnitTests                        # unit tests only
dotnet cake --target=IntegrationTests                 # integration tests only (may need Docker)
dotnet cake --target=Test --framework=net10.0         # all tests for one target framework
dotnet cake --target=CoverageReport                   # Default + coverage report in artifacts/coverage-report
dotnet cake --target=Pack                             # packages in artifacts/packages
dotnet cake --target=GitHubReleases --githubReleaseDryRun   # preview release notes after Pack
```

For a focused loop on one test, plain `dotnet test <project> --filter "FullyQualifiedName~Name"` is fine.

Tests run on every supported runtime: test projects target all .NET versions the libraries support and CI installs
(currently `net8.0`, `net9.0`, `net10.0`); `netstandard` targets are covered by the lowest of them.
A change is done when tests pass on all of them, not only on the one used locally.

CI runs `CoverageReport`: the coverage summary is in the pull request run summary, coverage badges
(`coverage.json` and `<PackageId>.json` in the `badges` branch) are updated on pushes to the default branch.
Coverage comes from Microsoft Code Coverage of `Microsoft.NET.Test.Sdk`, test projects need no coverlet packages.
The minimum line coverage is set in the repository `build.cake` (`minLineCoverage = 70;` after `#load`):
`CoverageReport`, and so the pull request build, fails below it. Raise it when coverage grows, don't lower it
to make a pull request pass.

## Repository layout

- A single `*.sln`/`*.slnx` in the root.
- `src/` — packable libraries only; every project there is packed and published.
- `tests/` — test projects; integration tests live under `tests/IntegrationTests/` or end with `.IntegrationTests`.
- `CHANGELOG.md` — per package project (multi-package repositories) or in the root (single-package ones),
  in [Keep a Changelog](https://keepachangelog.com) format with `## [x.y.z] - yyyy-mm-dd` sections.
- `README.md` — in the root, and in every package project of a multi-package repository (see README below).

## Versioning and releasing

Packages follow [Semantic Versioning](https://semver.org): major for incompatible changes (public API, behavior
consumers rely on, dropped target frameworks, stored identifiers such as cookie or message type names), minor for
backward-compatible features, patch for backward-compatible fixes. Pre-releases are `x.y.z-alpha.N`/`-beta.N`/`-rc.N`.

`.github/workflows/release-packages.yml` runs on every push to the default branch: build, tests, pack, push to nuget.org
via NuGet Trusted Publishing (no API keys), then a tag and a GitHub release for every package version that has none.
Versions already on nuget.org are skipped, so to release a package:

1. Bump its version in the csproj (`<PackageVersion>` or `<Version>`).
2. Add the matching `## [x.y.z]` section to its CHANGELOG — the section becomes the release notes.
   Breaking changes get a **Breaking** note with the migration steps.

Tags are `v<version>` in single-package repositories and `<PackageId>.v<version>` otherwise.

## Commits and pull requests

Commit messages follow [Conventional Commits](https://www.conventionalcommits.org):

```
<type>(<scope>): <summary in the imperative, lower case, no period>

<body: why, not what>

Refs #123
BREAKING CHANGE: <what breaks and how to migrate>
```

- Types: `feat`, `fix`, `docs`, `test`, `refactor`, `perf`, `build`, `ci`, `chore`, `revert`.
- Scope: the package without the `Curiosus.` prefix (`feat(email.smtp): ...`), omitted for repository-wide changes.
- Breaking changes: `!` after the type/scope (`feat(dal)!: ...`) plus a `BREAKING CHANGE:` footer.
- A task (GitHub issue) is referenced in the footer: `Refs #123`, or `Closes #123` when the commit completes it.
  Issues of another repository: `Refs curiosus-dev/Curiosus.Utils#123`.

Pull request titles use the same format: merge and squash commits take the pull request title.
One pull request is one logical change; the type of its title matches the version bump it causes.

## README

The root `README.md` of every repository has these sections, in this order:

1. Title and a one-line description of what the library does.
2. Badges: build (`release-packages.yml` on the default branch), license, NuGet downloads (of the main package
   when there are several), code coverage (`coverage.json`), documentation status when there is a docs site.
3. **Why use it** — the problems it solves and what sets it apart.
4. **Features** — a short list, linking to the documentation where it exists.
5. **Quick start** — `dotnet add package` and the smallest working example.
6. The longer part, specific to the library: concepts, configuration, comparison with alternatives, etc.
7. **Available packages** — a table: package (linking to its README), version, downloads, coverage badges
   (`<PackageId>.json`); multi-package repositories group it by area.
8. License.

Every package of a multi-package repository has its own `README.md` next to the csproj, packed as the package readme
on nuget.org by `build/Curiosus.props`: title, one-line description, NuGet/coverage badges, installation, a short usage
example, links to the repository README and the documentation. nuget.org renders it outside GitHub, so links and images
use absolute URLs. Single-package repositories pack the root `README.md` instead.

## Code style

- 4 spaces, max 130 characters per line, LF line endings — `.editorconfig` is the source of truth.
- Private fields are `_camelCase`; constants and static readonly fields are PascalCase.
- Nullable reference types on; public APIs have XML docs.
- Async methods accept a `CancellationToken`.
- Comment only non-obvious reasoning.
- Package versions are central (`Directory.Packages.props`) where the repository uses central package management.
- Don't bump dependencies of the libraries without a reason (a security fix or a needed feature): a dependency
  version is the minimum version for consumers. Test-only dependencies can be updated freely.

## Shared files

These files are synced from [curiosus-dev/dotnet-tools](https://github.com/curiosus-dev/dotnet-tools) by pull requests.
Change them there, local edits are overwritten by the next sync:

- `.editorconfig`, `nuget.config`, `.config/dotnet-tools.json`
- `build/curiosus.cake`, `build/Curiosus.props`
- `.github/workflows/build.yml`, `.github/workflows/release-packages.yml`, `.github/dependabot.yml`
- `.claude/curiosus.md`

Repository settings (rulesets, allowed actions, security features) are managed from `dotnet-tools/settings`,
don't change them in the repository settings UI. Actions in workflows must be pinned to a commit SHA.

Repository-specific build steps go to the repository `build.cake` (after `#load "build/curiosus.cake"`),
repository-specific MSBuild settings go to `Directory.Build.props` (after importing `build/Curiosus.props`).
