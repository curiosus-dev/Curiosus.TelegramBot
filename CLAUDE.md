# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

```bash
# Restore Cake tool (required once)
dotnet tool restore

# Build
dotnet cake --target=Build

# Run all tests (all target frameworks)
dotnet test

# Run tests for a specific framework
dotnet test tests/Markeli.TelegramBot.Tests/Markeli.TelegramBot.Tests.csproj --framework=net8.0

# Run a single test by fully qualified name
dotnet test --filter "FullyQualifiedName~HelpCommandHandlerTests.BuildHelpMessage_ExcludesHelpCommandItself" --framework=net8.0

# Tests with coverage via Cake
dotnet cake --target=Test --coverage=true --framework=net10.0

# Generate HTML coverage report (requires --coverage=true)
dotnet cake --target=Coverage-Report --framework=net10.0 --coverage=true

# Create NuGet package (artifacts/packages)
dotnet cake --target=Pack

# Preview GitHub release notes for the packed version (artifacts/release-notes)
dotnet cake --target=GitHubReleases --exclusive --githubReleaseDryRun
```

## Release

`.github/workflows/nuget-publish.yml` runs on every push to `main`: tests, packs, publishes to nuget.org via NuGet Trusted
Publishing (OIDC, `NuGet/login`, environment `nuget`, secret `NUGET_USER`) and creates a `v<Version>` tag and GitHub release
with notes from the matching `## [x.y.z]` section of `CHANGELOG.md`. An already published version is skipped. To release:
bump `<Version>` in the csproj and add the CHANGELOG section. Publishing logic lives in `build/publish.cake`, shared verbatim
with the other Curiosus repositories — do not change it here only.

`DotNetClean` (the Cake `Clean` target) does not remove stale per-framework output. After changing a
package version in `Directory.Packages.props`, wipe `bin`/`obj` before trusting a test run — leftover
assets from the previous version can make the same tests execute twice:

```bash
find src tests -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
```

## Architecture

NuGet library for building Telegram bots on .NET. Single solution, single library project + test project. Targets `net8.0` through `net10.0`.

**Update processing pipeline:**

```
Telegram API (polling)
  → TelegramBotUpdateDispatcher (IHostedService, concurrency via SemaphoreSlim)
    → TelegramUpdateQueue (thread-safe BlockingCollection, optional disk persistence)
      → TelegramUpdateProcessor (auth gate → message type guard → state lookup → command dispatch)
        → ITelegramBotCommandHandler.ProcessCommandAsync()
```

**Key abstractions:**
- `ITelegramBotCommandHandler` — command contract: `CommandName`, `CommandText`, `SupportedUpdateTypes`, `SupportedMessageTypes`, `ProcessCommandAsync()`, optional `TryGetLockKey()` for exclusive per-key execution.
- `ITelegramBotCommandState` / `TelegramBotCommandStateBase` — multi-step conversational state stored in `TelegramBotCommandStateCache` (IMemoryCache, 1-hour expiry).
- `TelegramBotCommandProcessingResult` — record returned by handlers; `WithoutState()` or `WithSimpleState()`.
- `TelegramBotOptions` — configuration record with self-validation (`ApiToken`, `Password`, `AllowedChatIds`, `MaxDegreeOfParallelism`, `QueuePersistenceFilePath`). Token *format* is not validated here — `AddTelegramBotInfrastructure` builds `TelegramBotClientOptions` eagerly so a malformed token throws at registration time without duplicating Telegram.Bot's undocumented format rule.
- `TelegramBotMessageTypes.TextOrRich` — shared `SupportedMessageTypes` set covering `MessageType.Text` and `MessageType.RichMessage`.
- `TelegramBotExtensions.GetMessageText()` / `GetRichBlocks()` — read message text with a rich-message fallback (rich messages leave `Message.Text` null), or reach the structured blocks.

**DI registration:**
```csharp
services.AddTelegramBotInfrastructure(options, addDefaultHelpCommand: true);
services.AddTelegramBotCommandHandler<MyCommandHandler>();
```
All services registered as singletons. Options validated eagerly at registration time.

## Code Conventions

- **Tabs** for indentation, max ~130 character line length.
- C# latest, nullable reference types enabled, implicit usings enabled.
- XML doc comments (`///`) on public APIs; `CS1591` warning enforced.
- Null checks via `ArgumentNullException.ThrowIfNull()`.
- Async methods take `CancellationToken`.
- Central package versioning via `Directory.Packages.props` with per-framework `Microsoft.Extensions.*` versions.
- Tests use xUnit with Moq; naming convention: `MethodName_Context_ExpectedResult`.
