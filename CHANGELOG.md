# Changelog

## [2.0.0] - 2026-09-28

### Changed

- **Breaking:** package renamed from `Markeli.TelegramBot` to `Curiosus.TelegramBot` and now published by the [curiosus-dev](https://www.nuget.org/profiles/curiosus-dev) organization. Namespaces, assemblies and types were renamed accordingly (`Markeli.TelegramBot` → `Curiosus.TelegramBot`): replace the namespace in your code to migrate.
- Build, CI and release setup is shared with the other Curiosus libraries and synced from [curiosus-dev/dotnet-tools](https://github.com/curiosus-dev/dotnet-tools): `build/curiosus.cake`, `build/Curiosus.props`, `.editorconfig`, `nuget.config`, `build.yml` and `release-packages.yml`.
- Publishing moved from the `v*` tag workflow (`release.yml`, long-lived `NUGET_API_KEY` secret) to `release-packages.yml`: every push to `main` publishes a new `<Version>` via NuGet Trusted Publishing (OIDC) and creates the `v<Version>` tag and GitHub release automatically.
- `ci.yml` replaced by the shared `build.yml` (all target frameworks in one run) and `coverage.yml`; the Cake `Coverage-Report` target is now `CoverageReport` and always collects coverage.
- Indentation switched from tabs to 4 spaces, as in the other Curiosus libraries.

### Removed

- Explicit `Microsoft.SourceLink.GitHub` package reference: Source Link is part of the .NET 8+ SDK. This also removes the `NU1902` warning for the transitive `Microsoft.Build.Tasks.Git` 10.0.301.

## [1.0.0] - 2026-08-11

### Removed

- **Breaking:** dropped `net6.0` and `net7.0` target frameworks (both out of support). Supported targets are now `net8.0`, `net9.0` and `net10.0`.

### Added

- Repository-level `NuGet.config` restricting package restore to nuget.org: inherited sources are cleared, `disabledPackageSources` is reset, and every package pattern is mapped to nuget.org via `packageSourceMapping`.
- Rich message support (Bot API 10.1):
  - `TelegramBotExtensions.GetMessageText()` — reads the message text with a fallback that flattens `Message.RichMessage` to plain text (blocks joined with newlines, inline formatting dropped). Used for command resolution and password verification, so rich messages now route to handlers instead of being answered with *"Unsupported command"* — previously such a message also abandoned an in-progress multi-step state.
  - `TelegramBotExtensions.GetRichBlocks()` — exposes the structured `RichBlock[]` of a rich message.
  - `TelegramBotMessageTypes.TextOrRich` — shared `SupportedMessageTypes` set covering `MessageType.Text` and `MessageType.RichMessage`. Adopted by `HelpCommandHandler`.
- `AddTelegramBotInfrastructure` now rejects a malformed `ApiToken` at registration time instead of failing when `ITelegramBotClient` is first resolved.

### Changed

- Updated `Telegram.Bot` 19.0.0 → 22.10.2.1. Renamed API usages: `SendTextMessageAsync` → `SendMessage`, `DeleteMessageAsync` → `DeleteMessage`; `ITelegramBotClient.MakeRequestAsync` → `SendRequest`.
- Updated package versions: `Moq` 4.20.72, `coverlet.msbuild` 10.0.1, `Microsoft.NET.Test.Sdk` 18.8.1 (single version, per-framework condition removed), `Microsoft.SourceLink.GitHub` 10.0.301, `Microsoft.Extensions.*` 9.0.18 / 10.0.10.
- Updated build tools: `Cake.Tool` 6.2.0, `dotnet-reportgenerator-globaltool` 5.5.11.
- CI matrix and release workflow SDK list reduced to 8.0.x / 9.0.x / 10.0.x.

### Fixed

- `NullReferenceException` in `TelegramUpdateProcessor` when an unauthenticated chat sent an update without a `Message` (e.g. a callback query). The exception was swallowed by the surrounding handler, so the chat silently received no reply and could never authenticate.

## [0.3.0] - 2026-03-22

### Added

- HTTP proxy support via `TelegramBotOptions.HttpProxy` (`HttpProxyOptions`) — when set, the bot client routes all Telegram API traffic through the specified proxy. Supports optional username/password authentication.

## [0.2.0] - 2026-02-23

### Added

- Multi-target framework support: `net6.0`, `net7.0`, `net8.0`, `net9.0`, `net10.0`.
- Built-in `/help` command handler that lists all registered commands, auto-registered via `addDefaultHelpCommand` parameter in `AddTelegramBotInfrastructure`.
- CI/CD workflows: `ci.yml` with matrix testing across all target frameworks and separate coverage job; `release.yml` with multi-framework SDK setup and automated GitHub release creation.
- Cake build targets: `Coverage-Report` and `Pack`; `--framework` and `--coverage` arguments for granular control.
- SourceLink support, deterministic builds, and NuGet package metadata for CI.
- Comprehensive README with prerequisites, architecture overview, configuration reference, authentication flow, and usage examples.
- Unit tests for `SimpleCommandState`, `TelegramBotCommandProcessingResult`, `TelegramBotExtensions`, `TelegramBotServiceCollectionExtensions`, `TelegramBotUpdateDispatcher`, `TelegramUpdateProcessor`, `TelegramUpdateQueue`, and `HelpCommandHandler`.
- Cake build tool configuration in `dotnet-tools.json`.
- Per-framework `Microsoft.Extensions.*` dependency versioning in `Directory.Packages.props`.

### Changed

- Restructured `Core` namespace: command interfaces moved to `Core.Commands`, state types moved to `Core.Commands.States`.
- Authentication prompt now includes the chat ID so users can easily share it with the bot administrator.
- Centralized dependency management via `Directory.Build.props` / `Directory.Packages.props`; removed shared `TargetFramework` from `Directory.Build.props` in favour of per-project `TargetFrameworks`.
- `Pack` Cake target now depends on `Build` instead of `Coverage-Report`.
- Updated package versions: `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.5, `coverlet.msbuild` 8.0.0, `Microsoft.NET.Test.Sdk` 18.0.1.
- Enabled `CS1591` warning (removed `NoWarn` suppression) to enforce XML documentation.

### Removed

- Legacy `build.sh` script (replaced by Cake).

## [0.1.0] - 2026-02-22

### Added

- `ITelegramBotCommandHandler` interface for implementing bot commands.
- `ITelegramBotCommandState` / `TelegramBotCommandStateBase` for multi-step conversational state.
- `TelegramBotCommandStateCache` — in-memory state cache with 1-hour expiration.
- `TelegramBotUpdateDispatcher` — hosted service for parallel update dispatching with lock support.
- `TelegramUpdateProcessor` — update processing with authentication and message type validation.
- `TelegramUpdateQueue` — thread-safe update queue with optional disk persistence.
- `TelegramBotOptions` — configuration with token, password, allowed chats, parallelism, and persistence path.
- `AddTelegramBotInfrastructure` / `AddTelegramBotCommandHandler<T>` DI extensions.
- `SimpleCommandState` for stateless command results.
- Cake build script (`build.cake`) with Clean, Build, Test, Pack, and Push targets.
- xUnit test project with Moq.
