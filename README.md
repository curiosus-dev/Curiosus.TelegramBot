# Curiosus.TelegramBot

Infrastructure library for building Telegram bots on .NET: command dispatching, multi-step commands as a state machine,
a bounded update queue that survives restarts, chat authentication and HTTP proxy support.

[![Build](https://github.com/curiosus-dev/Curiosus.TelegramBot/actions/workflows/release-packages.yml/badge.svg?branch=main)](https://github.com/curiosus-dev/Curiosus.TelegramBot/actions/workflows/release-packages.yml)
[![License](https://img.shields.io/github/license/curiosus-dev/Curiosus.TelegramBot)](https://github.com/curiosus-dev/Curiosus.TelegramBot/blob/main/LICENSE)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Curiosus.TelegramBot)](https://www.nuget.org/packages/Curiosus.TelegramBot)
[![Coverage](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/curiosus-dev/Curiosus.TelegramBot/badges/coverage.json)](https://github.com/curiosus-dev/Curiosus.TelegramBot/actions/workflows/release-packages.yml)

> **Renamed:** formerly `Markeli.TelegramBot`. Since 2.0.0 the package is published as `Curiosus.TelegramBot`
> by [curiosus-dev](https://www.nuget.org/profiles/curiosus-dev). To migrate, update the package reference and replace the `Markeli.TelegramBot` namespace.

## Why use it

Telegram.Bot gives you the Bot API; a real bot also needs the plumbing around it. Curiosus.TelegramBot provides it,
so a bot is a set of command handlers and nothing else:

- **Commands, not update loops** — implement `ITelegramBotCommandHandler` per command, routing by command text and
  message type is done for you.
- **Conversations out of the box** — return a state and the next message of the chat comes back to the same handler.
- **Safe under load** — bounded parallelism, optional per-key locks for commands that must not run concurrently,
  and pending updates survive a graceful restart when queue persistence is on: the bot drains in-flight work and saves
  the rest of the queue to disk before it stops.
- **Private bots in one line** — allowed chat IDs and a password challenge for everyone else.
- **Zero startup code** — the bot runs as an `IHostedService` registered by `AddTelegramBotInfrastructure`.

## Features

- **Command dispatching** — one `ITelegramBotCommandHandler` per command: you write what the command does, the
  library decides which handler gets the update and rejects message types the handler doesn't support.
- **Multi-step commands** — a command is a state machine over the conversation: return a state and the next message
  of the chat comes back to the same handler, so questionnaires and wizards need no session plumbing.
- **Update queue** — bounded parallelism and per-key locks keep a burst of messages from overloading the bot or
  running the same command twice at once; pending updates are saved to disk on shutdown, so a deploy loses nothing.
- **Authentication** — a private bot without extra code: allowed chat IDs pass right away, others need a password.
- **HTTP proxy** — run the bot where Telegram is reachable only through a proxy.
- **Built-in `/help`** — users see the list of commands without you maintaining it by hand.
- **Rich messages** — Bot API 10.1 rich messages reach handlers as plain text, so existing commands keep working.
- **Hosting and DI** — one `AddTelegramBotInfrastructure` call; the bot runs as an `IHostedService` and invalid options
  fail at startup, not on the first message.

## How it differs

Many .NET Telegram bot frameworks focus on routing and UI (menus, keyboards). Curiosus.TelegramBot focuses on running
a bot reliably as a service:

- **Bounded, lock-aware processing** instead of a task per update: a burst of updates can't exhaust the thread pool or
  the database, and commands that must not run concurrently are serialized by a key you choose.
- **No lost updates on deploy**: graceful shutdown waits for in-flight handlers and persists the rest of the queue.
- **No unbounded memory growth**: conversation states expire, nothing is kept per update.
- **Private bots without extra code**: an allow-list plus a password challenge for everyone else.
- **Plain Telegram.Bot inside**: handlers get `ITelegramBotClient` and `Update` as they are, nothing to relearn.

Not there yet: inline keyboards and callback queries, persistent conversation state, scoped handlers, middleware
and webhooks are planned for 4.0 — see [Roadmap](#roadmap).

## Quick start

### Prerequisites

- [.NET 9.0](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- Telegram Bot API token — create one via [BotFather](https://core.telegram.org/bots#botfather)

### Installation

```bash
dotnet add package Curiosus.TelegramBot
```

### Usage

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

A multi-step command is a state machine over the conversation: each step reads the current state, answers the user
and returns the next state, or no state to finish. Return `WithSimpleState()` from `ProcessCommandAsync` to keep the
conversation going — the next message from that chat will be routed to the same handler with the previous state:

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

States are kept per chat in memory and expire an hour after the last step; they don't survive a restart yet
(persistent state storage is planned for 4.0).
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

## Available packages

| Package | Version | Downloads | Coverage |
|---|---|---|---|
| [Curiosus.TelegramBot](https://github.com/curiosus-dev/Curiosus.TelegramBot#readme) | [![NuGet](https://img.shields.io/nuget/v/Curiosus.TelegramBot)](https://www.nuget.org/packages/Curiosus.TelegramBot) | [![Downloads](https://img.shields.io/nuget/dt/Curiosus.TelegramBot)](https://www.nuget.org/packages/Curiosus.TelegramBot) | [![Coverage](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/curiosus-dev/Curiosus.TelegramBot/badges/coverage.json)](https://github.com/curiosus-dev/Curiosus.TelegramBot/actions/workflows/release-packages.yml) |

## Roadmap

Version 4.0 is tracked in the [v4 milestone](https://github.com/curiosus-dev/Curiosus.TelegramBot/milestone/1):
inline keyboards and callback queries, pluggable storage for the update queue, conversation state and authorized chats,
configurable state key (chat, user or topic), scoped command handlers, a middleware pipeline, webhooks as a separate
package and ready-made controls (date/time pickers, lists, yes/no).

## License

[MIT](https://github.com/curiosus-dev/Curiosus.TelegramBot/blob/main/LICENSE)
