# Curiosus repository conventions

<!-- Synced from https://github.com/curiosus-dev/dotnet-tools, do not edit in place. -->

Shared by all Curiosus .NET libraries. Repository specifics live in the repository `CLAUDE.md`.

## Build, test, pack

Everything goes through Cake, locally and on CI, so a local run reproduces CI:

```bash
dotnet tool restore                                   # once after clone: Cake and ReportGenerator
dotnet cake                                           # Default: clean, build, all tests
dotnet cake --target=Build                            # clean + build
dotnet cake --target=UnitTests                        # unit tests only
dotnet cake --target=IntegrationTests                 # integration tests only (may need Docker)
dotnet cake --target=Test --framework=net10.0         # all tests for one target framework
dotnet cake --target=CoverageReport                   # coverage report in artifacts/coverage-report
dotnet cake --target=Pack                             # packages in artifacts/packages
dotnet cake --target=GitHubReleases --githubReleaseDryRun   # preview release notes after Pack
```

For a focused loop on one test, plain `dotnet test <project> --filter "FullyQualifiedName~Name"` is fine.

## Repository layout

- A single `*.sln`/`*.slnx` in the root.
- `src/` — packable libraries only; every project there is packed and published.
- `tests/` — test projects; integration tests live under `tests/IntegrationTests/` or end with `.IntegrationTests`.
- `CHANGELOG.md` — per package project (multi-package repositories) or in the root (single-package ones),
  in [Keep a Changelog](https://keepachangelog.com) format with `## [x.y.z] - yyyy-mm-dd` sections.

## Releasing

`.github/workflows/release-packages.yml` runs on every push to the default branch: build, tests, pack, push to nuget.org
via NuGet Trusted Publishing (no API keys), then a tag and a GitHub release for every package version that has none.
Versions already on nuget.org are skipped, so to release a package:

1. Bump its version in the csproj (`<PackageVersion>` or `<Version>`), following SemVer; changing target frameworks
   or public API in an incompatible way is a major bump.
2. Add the matching `## [x.y.z]` section to its CHANGELOG — the section becomes the release notes.

Tags are `v<version>` in single-package repositories and `<PackageId>.v<version>` otherwise.

## Code style

- 4 spaces, max 130 characters per line, LF line endings — `.editorconfig` is the source of truth.
- Private fields are `_camelCase`; constants and static readonly fields are PascalCase.
- Nullable reference types on; public APIs have XML docs.
- Async methods accept a `CancellationToken`.
- Comment only non-obvious reasoning.
- Package versions are central (`Directory.Packages.props`) where the repository uses central package management.

## Shared files

These files are synced from [curiosus-dev/dotnet-tools](https://github.com/curiosus-dev/dotnet-tools) by pull requests.
Change them there, local edits are overwritten by the next sync:

- `.editorconfig`, `nuget.config`, `.config/dotnet-tools.json`
- `build/curiosus.cake`, `build/Curiosus.props`
- `.github/workflows/build.yml`, `.github/workflows/release-packages.yml`
- `.claude/curiosus.md`

Repository-specific build steps go to the repository `build.cake` (after `#load "build/curiosus.cake"`),
repository-specific MSBuild settings go to `Directory.Build.props` (after importing `build/Curiosus.props`).
