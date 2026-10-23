# Inline keyboards: routing callback queries to command handlers

- Status: Approved
- Date: 2026-10-23
- Issue: [#28](https://github.com/curiosus-dev/Curiosus.TelegramBot/issues/28)

## Problem and goal

A bot built on the library can send a message with an inline keyboard (`InlineKeyboardMarkup` from Telegram.Bot),
but a press on its button never reaches a handler. `TelegramBotUpdateDispatcher.ResolveCommand` returns `null` for
an update without `Message`, and a callback query has none, so the press is answered with *"Unsupported command"*
and the button keeps spinning on the user's screen.

Goal: a handler declares which buttons are its own, receives their presses through the same pipeline as messages
(authorization, locks, parallelism, state), and the button always stops spinning. Existing handlers keep working
unchanged; the release is a minor one (3.1.0).

The first consumer is a stock-keeping bot that posts a returns notice to a channel with buttons "return this item",
"return all", "write off", pressed hours later, possibly several times.

## Scope

In scope:

- routing `UpdateType.CallbackQuery` updates to a handler by a callback data prefix the handler declares;
- answering the callback query automatically, with optional text from the handler;
- a helper to build and parse callback data within Telegram's 64-byte limit;
- authorization of presses by the chat of the message the button belongs to, channels included.

Out of scope (non-goals):

- keyboard builders and pagination helpers — [#41](https://github.com/curiosus-dev/Curiosus.TelegramBot/issues/41);
- silent handling of service messages that are answered with "Unsupported command" today (member joined, title
  changed, …) — [#40](https://github.com/curiosus-dev/Curiosus.TelegramBot/issues/40);
- buttons on inline-mode messages (`CallbackQuery.InlineMessageId` without `Message`) — the bot has no chat to
  authorize them against; such presses are answered and ignored;
- per-user authorization inside a group or channel (`AllowedUserIds`): a press is authorized by its chat, like a
  message.

## Requirements

Functional:

1. `ITelegramBotCommandHandler` gets `string? CallbackDataPrefix => null` (default interface member). A handler
   that sets it and lists `UpdateType.CallbackQuery` in `SupportedUpdateTypes` receives presses of buttons whose
   `callback_data` is `<prefix>:<payload>` or exactly `<prefix>`.
2. Callback query resolution, in order:
   1. the handler whose `CallbackDataPrefix` matches the data (prefix followed by `:` or the end of the data;
      ordinal comparison);
   2. otherwise the handler holding the chat's conversation state, if it supports `UpdateType.CallbackQuery` —
      this serves buttons of a multi-step dialog that don't need a prefix of their own;
   3. otherwise the press is unsupported: answered with a short text, logged at debug; no message is sent to the
      chat (a channel must not get "Unsupported command" posts).
3. Prefixes are validated when the dispatcher is built: non-empty, no `:`, unique among handlers; a violation throws
   `InvalidOperationException` naming the handlers.
4. Authorization: the chat is `CallbackQuery.Message.Chat.Id` (already returned by `GetChatId`). A press in a chat
   that is not authorized is answered with a short text and dropped; it never starts the password flow and never
   posts to the chat. A press without `Message` (inline mode, or a message too old for Telegram to include) is
   answered and dropped.
5. `IsCommandSupportedForUpdate` accepts a callback query when the handler lists `UpdateType.CallbackQuery`;
   `SupportedMessageTypes` is not consulted for it.
6. State: a press routed by prefix gets the chat's state only if that state belongs to the same handler; the
   handler's returned state is stored or cleared as for messages. A press routed to another handler leaves the
   chat's state untouched — pressing a button of an old message must not break a dialog in progress. (For
   messages the current behaviour — another command clears the state — stays.)
7. Answer: after `ProcessCommandAsync` returns, the processor calls `AnswerCallbackQuery` with
   `TelegramBotCommandProcessingResult.CallbackAnswer` (new optional record: `Text`, `ShowAlert`), or without text
   when it is null. If the handler already answered the query itself (to stop the spinner before long work),
   Telegram rejects the second answer; the processor treats that `ApiRequestException` as expected and logs it at
   debug. If the handler throws, the query is still answered without text.
8. Locks: `TryGetLockKey` is called for callback queries as for messages; the default stays "no lock".
9. `CallbackData` static helper:
   - `string Create(string prefix, string? payload = null)` → `prefix` or `prefix:payload`; throws
     `ArgumentException` when the result exceeds 64 bytes in UTF-8 or the prefix is invalid;
   - `bool TryParse(string? data, out string prefix, out string payload)` — splits at the first `:`; payload is
     empty when there is none;
   - `int MaxLength = 64` (bytes).
10. `TelegramBotExtensions.GetCallbackData(this Update)` returns `CallbackQuery.Data` or null, and
    `GetCallbackPayload(this Update)` returns the part after the prefix — so handlers don't repeat the parsing.
11. `HelpCommandHandler` lists only commands with a `CommandText` users can type; a handler may serve only
    buttons: `CommandText` empty string means "not a typed command" and is skipped by the help list and by message
    matching.

Non-functional:

1. Backward compatible: existing handlers compile and behave the same; bots that never send inline keyboards see no
   change. `allowed_updates` still comes from the handlers' `SupportedUpdateTypes`.
2. No new dependencies.

## Acceptance scenarios

```gherkin
Scenario: Press routed by prefix
  Given handler R with CallbackDataPrefix "ret" supporting CallbackQuery
  And a message in an authorized channel with a button "ret:42:1"
  When the button is pressed
  Then R.ProcessCommandAsync receives the update, GetCallbackPayload returns "42:1"
  And the callback query is answered once

Scenario: Handler answers with an alert
  When R returns CallbackAnswer { Text = "Already returned", ShowAlert = true }
  Then AnswerCallbackQuery is called with that text and showAlert = true

Scenario: Handler answered itself
  Given R calls AnswerCallbackQuery before its long work
  Then the processor's second answer fails with ApiRequestException, which is logged at debug, not as an error

Scenario: Unknown button
  Given no handler with prefix "old"
  When a button "old:1" is pressed in an authorized chat without conversation state
  Then the query is answered with a short text and nothing is posted to the chat

Scenario: Unauthorized chat
  When a button is pressed in a chat not in AllowedChatIds and not authenticated
  Then the query is answered, no password prompt is sent, the handler is not called

Scenario: Dialog button without a prefix
  Given handler D holds the chat state and supports CallbackQuery
  When a button "yes" is pressed in that chat
  Then D receives the press with its state

Scenario: Old button does not break a dialog
  Given handler D holds the chat state
  When a button "ret:42:1" of handler R is pressed
  Then R handles it and D's state is still there

Scenario: Duplicate prefix
  Given two handlers with CallbackDataPrefix "ret"
  When the dispatcher is created
  Then InvalidOperationException names both handlers

Scenario: Callback data too long
  When CallbackData.Create is called with a result over 64 bytes in UTF-8
  Then ArgumentException is thrown
```

## Contracts

```csharp
public interface ITelegramBotCommandHandler
{
    // new, default null: buttons with callback_data "<prefix>" or "<prefix>:<payload>" go to this handler
    string? CallbackDataPrefix => null;
}

public record TelegramBotCommandProcessingResult
{
    // new: how the processor answers a callback query; null — answer without text
    public TelegramBotCallbackAnswer? CallbackAnswer { get; init; }
}

public record TelegramBotCallbackAnswer(string? Text, bool ShowAlert = false);

public static class CallbackData
{
    public const int MaxLength = 64;
    public static string Create(string prefix, string? payload = null);
    public static bool TryParse(string? data, out string prefix, out string payload);
}

public static class TelegramBotExtensions
{
    public static string? GetCallbackData(this Update update);
    public static string? GetCallbackPayload(this Update update);
}
```

## Edge cases and failure modes

- Button pressed while the handler's lock is busy → re-enqueued as for messages; the spinner keeps going until the
  handler runs (Telegram stops it itself after a while).
- Update queue persisted on shutdown with a pending press → processed after restart; Telegram may reject the late
  answer ("query is too old"), which is logged at debug.
- Data with a prefix-like start but another prefix (`return:1` vs prefix `ret`) → not matched: the prefix must be
  followed by `:` or the end.
- Handler with `CallbackDataPrefix` but without `UpdateType.CallbackQuery` in `SupportedUpdateTypes` → validation
  error at startup: the handler would never receive its presses.

## Test plan

Unit tests with Moq on `ITelegramBotClient` (as the existing dispatcher and processor tests): resolution order,
prefix matching, validation errors, state handling for both routes, automatic answer with and without text, the
double-answer `ApiRequestException`, unauthorized and message-less presses, `CallbackData` limits with multi-byte
payloads, help list skipping button-only handlers. All target frameworks (`net9.0`, `net10.0`).

## Rollout

Minor release 3.1.0 from `main`. README: a "Buttons" section with a minimal handler example; CHANGELOG `Added`
entries.
