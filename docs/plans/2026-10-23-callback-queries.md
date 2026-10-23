# Callback Queries Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Route inline keyboard presses (callback queries) to command handlers by a declared prefix and answer them automatically.

**Architecture:** `ITelegramBotCommandHandler` gets a default-null `CallbackDataPrefix`. `TelegramBotUpdateDispatcher` resolves
callback queries by prefix, then by the chat's state holder, and validates prefixes at construction.
`TelegramUpdateProcessor` authorizes presses without the password flow, keeps unrelated dialog state, and answers every
press after the handler.

**Tech Stack:** .NET 9/10, Telegram.Bot 22.10.2.1, xUnit + Moq.

**Spec:** `docs/specs/2026-10-23-callback-queries.md`

## Global Constraints

- Backward compatible, minor release 3.1.0: no breaking change to public types; new interface members are default
  interface members.
- No new dependencies.
- Callback data limit: 64 bytes in UTF-8; prefix separator `:`; prefix non-empty, without `:`.
- A callback press never posts a message to the chat (unknown button, unauthorized chat, no `Message`).
- 4 spaces, ≤ 130 chars, XML docs on public APIs, `ArgumentNullException.ThrowIfNull`, tests named
  `MethodName_Context_ExpectedResult`.

## Review Focus

- Handler answered the query itself → the processor's second `AnswerCallbackQuery` throws `ApiRequestException`; must be
  logged at debug and swallowed (Task 3 test `ProcessAsync_HandlerAlreadyAnswered_SwallowsApiRequestException`).
- Handler throws → the press is still answered without text (Task 3 test `ProcessAsync_HandlerThrows_StillAnswers`).
- Multi-byte payload near the limit (Cyrillic) → byte count, not char count (Task 1 test
  `Create_CyrillicPayloadOver64Bytes_Throws`).
- Prefix that is a prefix of another (`ret` vs `return:1`) → no match (Task 2 test
  `ResolveCommand_CallbackPrefixFollowedByOtherChars_ReturnsNull`).
- Callback press while another handler holds the chat state → that state survives (Task 3 test
  `ProcessAsync_CallbackForOtherHandler_KeepsForeignState`).

---

### Task 1: `CallbackData` helper and update extensions

**Files:**
- Create: `src/Curiosus.TelegramBot/Core/CallbackData.cs`
- Modify: `src/Curiosus.TelegramBot/Core/TelegramBotExtensions.cs`
- Test: `tests/Curiosus.TelegramBot.Tests/CallbackDataTests.cs`

**Interfaces:**
- Produces: `public static class CallbackData { public const int MaxLength = 64; public const char Separator = ':';
  public static string Create(string prefix, string? payload = null);
  public static bool TryParse(string? data, out string prefix, out string payload);
  internal static bool IsValidPrefix(string? prefix); internal static bool Matches(string data, string prefix); }`
- Produces: `TelegramBotExtensions.GetCallbackData(this Update) : string?`,
  `TelegramBotExtensions.GetCallbackPayload(this Update) : string?` (text after the first `:`, empty when none, null without a callback query).

- [ ] **Step 1: Write failing tests** — `Create_PrefixOnly_ReturnsPrefix` (`"ret"`), `Create_WithPayload_JoinsWithColon`
  (`"ret:42:1"`), `Create_CyrillicPayloadOver64Bytes_Throws` (prefix `"r"`, payload of 32 × `'я'` = 64 bytes + 2 → throws),
  `Create_Exactly64Bytes_Succeeds`, `Create_PrefixWithColon_Throws`, `Create_EmptyPrefix_Throws`,
  `TryParse_WithPayload_SplitsAtFirstColon` (`"ret:42:1"` → `"ret"`, `"42:1"`), `TryParse_NoColon_EmptyPayload`,
  `TryParse_NullOrEmpty_ReturnsFalse`, `Matches_PrefixFollowedByOtherChars_False` (`"return:1"`, `"ret"`),
  `GetCallbackPayload_CallbackUpdate_ReturnsPayload`, `GetCallbackData_MessageUpdate_ReturnsNull`.
- [ ] **Step 2: Run** `dotnet test tests/Curiosus.TelegramBot.Tests --filter "FullyQualifiedName~CallbackData"` — FAIL (type missing).
- [ ] **Step 3: Implement** the helper and the two extensions; byte count via `Encoding.UTF8.GetByteCount`; `ArgumentException`
  messages name the actual byte length.
- [ ] **Step 4: Run** the same filter — PASS.
- [ ] **Step 5: Commit** `feat: add callback data helper` (`Refs #28`).

### Task 2: Handler prefix and dispatcher routing

**Files:**
- Modify: `src/Curiosus.TelegramBot/Core/Commands/ITelegramBotCommandHandler.cs`
- Modify: `src/Curiosus.TelegramBot/Core/TelegramBotUpdateDispatcher.cs` (constructor validation, `ResolveCommand`,
  `DispatchUpdateAsync`, `FindCommand`)
- Modify: `src/Curiosus.TelegramBot/Core/Commands/HelpCommandHandler.cs` (skip empty `CommandText`)
- Test: `tests/Curiosus.TelegramBot.Tests/TelegramBotUpdateDispatcherTests.cs`, `HelpCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `CallbackData.IsValidPrefix`, `CallbackData.Matches`.
- Produces: `ITelegramBotCommandHandler.CallbackDataPrefix { get; }` → `string?`, default `null`.
- Produces: `internal ITelegramBotCommandHandler? ResolveCommand(Update update, long chatId)` now handles
  `update.CallbackQuery`; `internal async Task AnswerUnsupportedCallbackAsync(...)` (private is fine) answers with
  text `"This button is no longer supported."` and posts nothing.

- [ ] **Step 1: Write failing tests** — `ResolveCommand_CallbackWithMatchingPrefix_ReturnsHandler`,
  `ResolveCommand_CallbackPrefixFollowedByOtherChars_ReturnsNull`, `ResolveCommand_CallbackWithoutPrefixAndStateHolderSupportsCallbacks_ReturnsStateHolder`,
  `ResolveCommand_CallbackWithoutPrefixAndStateHolderWithoutCallbacks_ReturnsNull`,
  `ResolveCommand_CallbackPrefixWinsOverStateHolder`, `Constructor_DuplicateCallbackPrefix_ThrowsNamingHandlers`
  (`InvalidOperationException`, message contains both `CommandName`s), `Constructor_PrefixWithColon_Throws`,
  `Constructor_PrefixWithoutCallbackQueryUpdateType_Throws`, `ResolveCommand_MessageMatchingEmptyCommandText_ReturnsNull`
  (button-only handler with `CommandText = ""` never matches text), `DispatchUpdate_UnknownCallback_AnswersAndSendsNoMessage`
  (verify `AnswerCallbackQueryRequest` sent once, `SendMessageRequest` never), and in `HelpCommandHandlerTests`
  `ProcessCommandAsync_ButtonOnlyHandler_NotListed`.
- [ ] **Step 2: Run** `dotnet test tests/Curiosus.TelegramBot.Tests --filter "FullyQualifiedName~Dispatcher|FullyQualifiedName~Help"` — new tests FAIL.
- [ ] **Step 3: Implement.** Callback resolution: longest matching prefix first (order like `_telegramCommands`); state
  holder only if its `SupportedUpdateTypes` contains `CallbackQuery`. `DispatchUpdateAsync`: when `ResolveCommand` returns
  null for a callback query, answer it instead of `SendUnsupportedCommandNotificationAsync`. `IsCommandMatch` already
  rejects empty command text — keep it.
- [ ] **Step 4: Run** the same filter — PASS, existing tests unchanged.
- [ ] **Step 5: Commit** `feat: route callback queries to handlers by prefix` (`Refs #28`).

### Task 3: Processor — authorization, state, automatic answer

**Files:**
- Create: `src/Curiosus.TelegramBot/Core/TelegramBotCallbackAnswer.cs`
- Modify: `src/Curiosus.TelegramBot/Core/TelegramBotCommandProcessingResult.cs`
- Modify: `src/Curiosus.TelegramBot/Core/TelegramUpdateProcessor.cs`
- Test: `tests/Curiosus.TelegramBot.Tests/TelegramUpdateProcessorTests.cs` (new region or `CallbackQueryProcessingTests.cs`)

**Interfaces:**
- Produces: `public record TelegramBotCallbackAnswer(string? Text, bool ShowAlert = false);`
- Produces: `TelegramBotCommandProcessingResult.CallbackAnswer { get; init; }` → `TelegramBotCallbackAnswer?`.

- [ ] **Step 1: Write failing tests** — `ProcessAsync_CallbackInAllowedChat_ExecutesAndAnswersOnce`,
  `ProcessAsync_CallbackAnswerWithAlert_PassesTextAndShowAlert`, `ProcessAsync_HandlerAlreadyAnswered_SwallowsApiRequestException`
  (mock `AnswerCallbackQueryRequest` to throw `ApiRequestException("query is too old", 400)`; no error log, no throw),
  `ProcessAsync_HandlerThrows_StillAnswers`, `ProcessAsync_CallbackFromUnauthorizedChat_AnswersWithoutPasswordPrompt`
  (no `SendMessageRequest`, handler not called, chat not added to the waiting list — a later text message still gets the
  "Hi! Your chat ID…" greeting), `ProcessAsync_CallbackWithoutMessage_AnswersAndSkipsHandler`,
  `ProcessAsync_CallbackHandlerSupportsCallbackQuery_IgnoresSupportedMessageTypes`,
  `ProcessAsync_CallbackForOtherHandler_KeepsForeignState`, `ProcessAsync_CallbackForStateHolder_PassesAndUpdatesState`.
- [ ] **Step 2: Run** `dotnet test tests/Curiosus.TelegramBot.Tests --filter "FullyQualifiedName~Processor|FullyQualifiedName~Callback"` — FAIL.
- [ ] **Step 3: Implement.** Callback branch in `ProcessAsync` before the password flow: no `Message` or unauthorized chat →
  answer with `"Not authorized."` / no text and return. `ExecuteCommandAsync`: for callbacks, pass state only if the cached
  handler is the same, and never `RemoveEntry` a foreign state; for messages keep current behaviour. Answer in `finally`
  for callbacks with `result?.CallbackAnswer`; catch `ApiRequestException` → `LogDebug`.
- [ ] **Step 4: Run** the same filter — PASS.
- [ ] **Step 5: Commit** `feat: answer callback queries automatically` (`Refs #28`).

### Task 4: Docs, changelog, full verification

**Files:**
- Modify: `README.md` (section "Buttons" after the command section: a handler with `CallbackDataPrefix`, sending a keyboard
  with `CallbackData.Create`, reading `GetCallbackPayload`, returning `CallbackAnswer`; mention `#41` for builders), `CLAUDE.md`
  (pipeline: callback queries), `CHANGELOG.md` (`## [Unreleased]` → `### Added` entries for each public addition;
  no version section).

- [ ] **Step 1: Update docs** as above.
- [ ] **Step 2: Run** `dotnet tool restore && dotnet cake` — build and all tests pass on `net9.0` and `net10.0`, coverage
  ≥ `minLineCoverage` from `build.cake`.
- [ ] **Step 3: Commit** `docs: describe inline buttons` (`Refs #28`); open PR `feat: route callback queries to command handlers`
  with `Closes #28` (builders moved to #41).
