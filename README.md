# Curiosus.TelegramBot

> **Renamed:** formerly `Markeli.TelegramBot`. Since 2.0.0 the package is published as `Curiosus.TelegramBot`
> by [curiosus-dev](https://www.nuget.org/profiles/curiosus-dev). To migrate, update the package reference and replace the `Markeli.TelegramBot` namespace.

[![Release](https://github.com/curiosus-dev/Curiosus.TelegramBot/actions/workflows/release-packages.yml/badge.svg?branch=main)](https://github.com/curiosus-dev/Curiosus.TelegramBot/actions/workflows/release-packages.yml)
[![NuGet](https://img.shields.io/nuget/v/Curiosus.TelegramBot)](https://www.nuget.org/packages/Curiosus.TelegramBot)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Curiosus.TelegramBot)](https://www.nuget.org/packages/Curiosus.TelegramBot)
[![Coverage](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/curiosus-dev/Curiosus.TelegramBot/badges/coverage.json)](https://github.com/curiosus-dev/Curiosus.TelegramBot/actions/workflows/coverage.yml)

Infrastructure library for building Telegram bots on .NET: command dispatching, multi-step state management, update queue with persistence, and simple chat authentication.

## Prerequisites

- [.NET 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Telegram Bot API token — create one via [BotFather](https://core.telegram.org/bots#botfather)

## Features

- **Command dispatching** — register handlers via `ITelegramBotCommandHandler`, route updates by command text and supported update/message types.
- **State management** — multi-step conversational commands with in-memory state cache (`TelegramBotCommandStateBase`).
- **Update queue** — thread-safe queue with configurable parallelism and optional disk persistence on shutdown.
- **Authentication** — simple password-based chat verification with allowed chat ID filtering.
- **Built-in `/help` command** — opt-in handler that lists all registered commands via `AddHelpCommand()`.
- **Rich message support** — rich formatted messages (Bot API 10.1) are routed like plain text via `Update.GetMessageText()`, with structured blocks available through `Update.GetRichBlocks()`.
- **DI integration** — `AddTelegramBotInfrastructure` / `AddTelegramBotCommandHandler<T>` extensions for `IServiceCollection`.

## Architecture

```
Telegram API
    │ polling via Telegram.Bot
    ▼
TelegramBotUpdateDispatcher          (IHostedService — starts polling, runs dispatch loop)
    ├─ on receive ──► TelegramUpdateQueue.Enqueue()
    └─ dispatch loop
         ├─ TelegramUpdateQueue.Take()
         ├─ ResolveCommand()           (state-cache aware routing)
         ├─ TryAcquireLock()           (optional per-key exclusive lock)
         ├─ SemaphoreSlim              (MaxDegreeOfParallelism)
         └─► TelegramUpdateProcessor.ProcessAsync()
              ├─ Auth gate             (AllowedChatIds / password challenge)
              ├─ Message type guard
              ├─ State lookup          (TelegramBotCommandStateCache)
              ├─ ITelegramBotCommandHandler.ProcessCommandAsync()
              └─ State update/remove   (based on result.State)
```

Updates are polled, enqueued into a thread-safe `BlockingCollection<Update>`, and dispatched to handlers with configurable concurrency (`MaxDegreeOfParallelism`, default 10). If a handler returns state, the next message from that chat is routed to the same handler automatically.

## Installation

```bash
dotnet add package Curiosus.TelegramBot
```

## Quick start

Register the infrastructure and command handlers in your DI container:

```csharp
builder.Services.AddTelegramBotInfrastructure(new TelegramBotOptions
{
    ApiToken = "BOT_TOKEN",
    Password = "secret",
    AllowedChatIds = new[] { 123456L }
});

builder.Services.AddTelegramBotCommandHandler<PingCommandHandler>();
builder.Services.AddHelpCommand();
```

Implement a command handler:

```csharp
public class PingCommandHandler : ITelegramBotCommandHandler
{
    public string CommandName => "Ping";
    public string CommandText => "/ping";
    public IReadOnlySet<UpdateType> SupportedUpdateTypes => new HashSet<UpdateType> { UpdateType.Message };
    public IReadOnlySet<MessageType> SupportedMessageTypes => TelegramBotMessageTypes.TextOrRich;

    public async Task<TelegramBotCommandProcessingResult> ProcessCommandAsync(
        ITelegramBotClient telegramBotClient, Update telegramUpdate,
        ITelegramBotCommandState? commandState, CancellationToken cancellationToken)
    {
        await telegramBotClient.SendMessage(
            telegramUpdate.Message!.Chat.Id, "pong", cancellationToken: cancellationToken);
        return TelegramBotCommandProcessingResult.WithoutState();
    }
}
```

The bot starts automatically as an `IHostedService` — no extra startup code required.

## Configuration

All settings are passed via `TelegramBotOptions`:

| Property | Type | Default | Description |
|---|---|---|---|
| `ApiToken` | `string` | *required* | Telegram Bot API token. |
| `Password` | `string` | *required* | Password for chat authentication (see below). |
| `AllowedChatIds` | `long[]` | `[]` | Pre-authorized chat IDs that skip password verification. |
| `MaxDegreeOfParallelism` | `int` | `10` | Maximum number of updates processed concurrently. |
| `HttpProxy` | `HttpProxyOptions?` | `null` | HTTP proxy settings. When set, all bot API traffic is routed through this proxy. See below. |
| `QueuePersistenceFilePath` | `string?` | `null` | File path for persisting pending updates on shutdown. If set, the queue is saved to disk during graceful shutdown and restored on next startup. |

### HTTP proxy

`HttpProxyOptions` fields:

| Property | Type | Description |
|---|---|---|
| `Url` | `string` | Proxy URL (e.g. `http://proxy.example.com:8080`). Required. |
| `Username` | `string?` | Proxy authentication username. |
| `Password` | `string?` | Proxy authentication password. |

```csharp
services.AddTelegramBotInfrastructure(new TelegramBotOptions
{
    ApiToken = "BOT_TOKEN",
    Password = "secret",
    HttpProxy = new HttpProxyOptions
    {
        Url = "http://proxy.example.com:8080",
        Username = "user",
        Password = "pass"
    }
});
```

### Authentication flow

Chats listed in `AllowedChatIds` are authorized automatically. When an unknown chat sends a message:

1. The bot replies with *"Hi! To use this bot, please, send a verification password."*
2. If the user sends the correct `Password`, the chat is added to the allowed set for the lifetime of the process. Authorization is stored in memory only and resets on application restart.
3. If incorrect, the bot replies *"Incorrect password! Please, try again."*

## Multi-step commands

Return `WithSimpleState()` from `ProcessCommandAsync` to keep the conversation going — the next message from that chat will be routed to the same handler with the previous state:

```csharp
public class GreetCommandHandler : ITelegramBotCommandHandler
{
    public string CommandName => "Greet";
    public string CommandText => "/greet";
    public IReadOnlySet<UpdateType> SupportedUpdateTypes => new HashSet<UpdateType> { UpdateType.Message };
    public IReadOnlySet<MessageType> SupportedMessageTypes => TelegramBotMessageTypes.TextOrRich;

    public async Task<TelegramBotCommandProcessingResult> ProcessCommandAsync(
        ITelegramBotClient telegramBotClient, Update telegramUpdate,
        ITelegramBotCommandState? commandState, CancellationToken cancellationToken)
    {
        var chatId = telegramUpdate.Message!.Chat.Id;

        if (commandState is null)
        {
            await telegramBotClient.SendMessage(
                chatId, "What is your name?", cancellationToken: cancellationToken);
            return TelegramBotCommandProcessingResult.WithSimpleState();
        }

        var name = telegramUpdate.GetMessageText();
        await telegramBotClient.SendMessage(
            chatId, $"Hello, {name}!", cancellationToken: cancellationToken);
        return TelegramBotCommandProcessingResult.WithoutState();
    }
}
```

For custom state data, implement `ITelegramBotCommandState` (or extend `TelegramBotCommandStateBase` for timestamps) and return it via `new TelegramBotCommandProcessingResult { State = myState }`.

The user can abort a multi-step flow at any time by sending another `/command` — it will be matched to the new handler instead.

## Rich messages

A rich formatted message (Bot API 10.1) carries its content in `Message.RichMessage` and leaves `Message.Text` unset, so it arrives as `MessageType.RichMessage` rather than `MessageType.Text`.

**Receiving.** Declare `TelegramBotMessageTypes.TextOrRich` in `SupportedMessageTypes` to accept both, and read the text through `Update.GetMessageText()`, which falls back to flattening the rich blocks into plain text (blocks joined with newlines, inline formatting dropped). A handler that declares only `MessageType.Text` and reads `Message.Text` directly will reject rich messages.

For the structure itself, use `Update.GetRichBlocks()`:

```csharp
var blocks = telegramUpdate.GetRichBlocks();
if (blocks is not null)
{
    foreach (var table in blocks.OfType<RichBlockTable>())
    {
        // ...
    }
}
```

**Sending.** No library API is involved — handlers receive `ITelegramBotClient` directly and call `Telegram.Bot` themselves:

```csharp
await telegramBotClient.SendRichMessage(chatId, new InputRichMessage
{
    Blocks =
    [
        new InputRichBlockSectionHeading { Text = new RichTextText { Text = "Daily report" } },
        new InputRichBlockParagraph { Text = new RichTextText { Text = "All systems nominal." } }
    ]
}, cancellationToken: cancellationToken);
```

`InputRichMessage` accepts exactly one of `Blocks`, `Html`, or `Markdown`. Use `SendRichMessageDraft` to stream a partial message while it is still being generated.

## Concurrent lock keys

Override `TryGetLockKey` to prevent parallel execution of the same command for a specific context (e.g., per chat):

```csharp
public bool TryGetLockKey(Update telegramUpdate, out string? lockKey)
{
    lockKey = $"my_command_{telegramUpdate.Message?.Chat.Id}";
    return true;
}
```

When a lock key is active, conflicting updates are re-enqueued and retried. This method has a default implementation that returns `false` (no locking), so most handlers don't need to override it.

## Build

```bash
dotnet build
dotnet test
```

The project uses [Cake](https://cakebuild.net/) for build automation, the same pipeline runs locally and on CI:

```bash
dotnet tool restore                   # once: Cake and ReportGenerator
dotnet cake                           # Clean + build + tests
dotnet cake --target=CoverageReport   # Tests with coverage + HTML report in ./artifacts/coverage-report/
dotnet cake --target=Pack             # NuGet package in ./artifacts/packages/
```

Build scripts and settings are shared with the other Curiosus libraries via
[curiosus-dev/dotnet-tools](https://github.com/curiosus-dev/dotnet-tools).

Packages are restored exclusively from nuget.org: the repository-level `nuget.config` clears any inherited
source and maps every package pattern to nuget.org, so restore behaves identically on any machine.

## License

[MIT](LICENSE)
