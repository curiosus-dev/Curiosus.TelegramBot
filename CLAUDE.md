# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

@.claude/curiosus.md

## Release

Single-package repository: releases are tagged `v<Version>`, notes come from the root `CHANGELOG.md`.

## Build notes

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

- C# latest, nullable reference types enabled, implicit usings enabled.
- XML doc comments (`///`) on public APIs; `CS1591` warning enforced.
- Null checks via `ArgumentNullException.ThrowIfNull()`.
- Async methods take `CancellationToken`.
- Central package versioning via `Directory.Packages.props` with per-framework `Microsoft.Extensions.*` versions.
- Tests use xUnit with Moq; naming convention: `MethodName_Context_ExpectedResult`.
