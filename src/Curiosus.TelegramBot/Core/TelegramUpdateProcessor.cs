using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Curiosus.TelegramBot;

/// <summary>
/// Processes Telegram updates. Handles authentication, validation, and command execution.
/// </summary>
public class TelegramUpdateProcessor
{
    private const string NotAuthorizedButtonText = "Not authorized.";

    private readonly ILogger<TelegramUpdateProcessor> _logger;
    private readonly ITelegramBotClient _botClient;
    private readonly TelegramBotOptions _botOptions;
    private readonly TelegramBotCommandStateCache _stateCache;

    private readonly ConcurrentDictionary<long, byte> _allowedChatIds;
    private readonly ConcurrentDictionary<long, byte> _waitingPasswordChatIds = new();

    /// <inheritdoc cref="TelegramUpdateProcessor"/>
    public TelegramUpdateProcessor(
        ILogger<TelegramUpdateProcessor> logger,
        ITelegramBotClient botClient,
        TelegramBotOptions botOptions,
        TelegramBotCommandStateCache stateCache)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(botClient);
        ArgumentNullException.ThrowIfNull(botOptions);
        ArgumentNullException.ThrowIfNull(stateCache);

        _logger = logger;
        _botClient = botClient;
        _botOptions = botOptions;
        _stateCache = stateCache;

        _allowedChatIds = new ConcurrentDictionary<long, byte>();
        foreach (var chatId in botOptions.AllowedChatIds)
        {
            _allowedChatIds.TryAdd(chatId, 0);
        }
    }

    /// <summary>
    /// Processes an update with the resolved command.
    /// Handles authentication, message type validation, and command execution.
    /// </summary>
    /// <param name="update">The Telegram update.</param>
    /// <param name="command">The resolved command to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ProcessAsync(
        Update update,
        ITelegramBotCommandHandler command,
        CancellationToken cancellationToken)
    {
        await Task.Yield();

        var chatId = update.GetChatId();

        if (update.CallbackQuery is { } callbackQuery)
        {
            await ProcessCallbackQueryAsync(update, callbackQuery, chatId, command, cancellationToken);
            return;
        }

        try
        {
            if (!_allowedChatIds.ContainsKey(chatId))
            {
                await HandleUnauthenticatedUserAsync(chatId, update, cancellationToken);
                return;
            }

            if (!IsCommandSupportedForUpdate(command, update))
            {
                await SendUnsupportedMessageTypeNotificationAsync(
                    chatId,
                    update.Message?.Type ?? MessageType.Unknown,
                    command.CommandName,
                    cancellationToken);
                return;
            }

            await ExecuteCommandAsync(update, chatId, command, cancellationToken);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error processing update");
        }
    }

    /// <summary>
    /// A press is never answered with a chat message: an unauthorized chat or a press without a message (inline mode)
    /// gets only the callback answer, and every press is answered, even when the handler fails.
    /// </summary>
    private async Task ProcessCallbackQueryAsync(
        Update update,
        CallbackQuery callbackQuery,
        long chatId,
        ITelegramBotCommandHandler command,
        CancellationToken cancellationToken)
    {
        TelegramBotCallbackAnswer? answer = null;

        try
        {
            if (callbackQuery.Message == null || !_allowedChatIds.ContainsKey(chatId))
            {
                _logger.LogDebug(
                    "Button \"{CallbackData}\" pressed in a chat that is not authorized, ChatId={ChatId}",
                    callbackQuery.Data,
                    chatId);
                answer = new TelegramBotCallbackAnswer(NotAuthorizedButtonText);
                return;
            }

            if (!command.SupportedUpdateTypes.Contains(UpdateType.CallbackQuery))
            {
                _logger.LogDebug(
                    "Command \"{CommandName}\" in chat ChatId={ChatId} does not support callback queries",
                    command.CommandName,
                    chatId);
                return;
            }

            answer = await ExecuteCommandAsync(update, chatId, command, cancellationToken);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error processing callback query");
        }
        finally
        {
            await AnswerCallbackQueryAsync(callbackQuery, answer, cancellationToken);
        }
    }

    private async Task AnswerCallbackQueryAsync(
        CallbackQuery callbackQuery,
        TelegramBotCallbackAnswer? answer,
        CancellationToken cancellationToken)
    {
        try
        {
            await _botClient.AnswerCallbackQuery(
                callbackQuery.Id,
                answer?.Text,
                answer?.ShowAlert ?? false,
                cancellationToken: cancellationToken);
        }
        catch (ApiRequestException e) when (IsExpiredQueryError(e))
        {
            // the handler answered the query itself, or it is too old to answer (e.g. restored from the persisted queue)
            _logger.LogDebug(e, "Callback query {CallbackQueryId} was already answered or expired", callbackQuery.Id);
        }
        catch (ApiRequestException e)
        {
            _logger.LogWarning(e, "Telegram rejected the answer to callback query {CallbackQueryId}", callbackQuery.Id);
        }
    }

    /// <summary>
    /// Telegram answers "query is too old and response timeout expired or query ID is invalid" both for a query that was
    /// already answered and for an expired one.
    /// </summary>
    private static bool IsExpiredQueryError(ApiRequestException exception) =>
        exception.Message.Contains("query is too old", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("query ID is invalid", StringComparison.OrdinalIgnoreCase);

    private bool IsCommandSupportedForUpdate(ITelegramBotCommandHandler command, Update update)
    {
        if (!command.SupportedUpdateTypes.Contains(update.Type))
            return false;

        return update.Message == null || command.SupportedMessageTypes.Contains(update.Message.Type);
    }

    private async Task<TelegramBotCallbackAnswer?> ExecuteCommandAsync(
        Update update,
        long chatId,
        ITelegramBotCommandHandler command,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Executing command \"{CommandName}\" in chat ChatId={ChatId}...",
            command.CommandName,
            chatId);

        var cachedState = _stateCache.GetEntry(chatId);
        var isOwnState = cachedState?.CommandHandler == command;

        ITelegramBotCommandState? commandState = null;
        if (isOwnState)
        {
            commandState = cachedState!.CommandState;
        }
        else if (cachedState != null && update.CallbackQuery != null)
        {
            // a button of an old message must not break the dialog in progress: the press runs without state
            // and leaves the other handler's state alone
            var pressResult = await command.ProcessCommandAsync(_botClient, update, null, cancellationToken);
            LogExecuted(command, chatId);

            if (pressResult.State != null)
            {
                _logger.LogWarning(
                    "Command \"{CommandName}\" returned a state for a button press in chat ChatId={ChatId}, " +
                    "but command \"{StateHolder}\" holds the chat's dialog: the returned state is discarded",
                    command.CommandName,
                    chatId,
                    cachedState.CommandHandler.CommandName);
            }

            return pressResult.CallbackAnswer;
        }
        else if (cachedState != null)
        {
            _stateCache.RemoveEntry(chatId);
        }

        var result = await command.ProcessCommandAsync(
            _botClient,
            update,
            commandState,
            cancellationToken);

        LogExecuted(command, chatId);

        if (result.State == null)
        {
            _stateCache.RemoveEntry(chatId);
        }
        else
        {
            _stateCache.SetEntry(chatId, command, result.State);
        }

        return result.CallbackAnswer;
    }

    private void LogExecuted(ITelegramBotCommandHandler command, long chatId) =>
        _logger.LogDebug(
            "Executed command \"{CommandName}\" in chat ChatId={ChatId}",
            command.CommandName,
            chatId);

    private async Task HandleUnauthenticatedUserAsync(
        long chatId,
        Update update,
        CancellationToken cancellationToken)
    {
        if (!_waitingPasswordChatIds.TryAdd(chatId, 0))
        {
            // Updates without a message (e.g. a callback query) carry no password: treated as incorrect.
            if (Equals(update.GetMessageText(), _botOptions.Password))
            {
                _allowedChatIds.TryAdd(chatId, 0);
                _logger.LogInformation(
                    "Chat ChatId={ChatId} authenticated successfully",
                    chatId);

                await _botClient.SendMessage(
                    chatId,
                    "Correct! Now you can send me command to execute. Please, click on \"Menu\" to list all of them.",
                    cancellationToken: cancellationToken);
            }
            else
            {
                _logger.LogWarning(
                    "Wrong password from chat ChatId={ChatId}",
                    chatId);

                await _botClient.SendMessage(
                    chatId,
                    "Incorrect password! Please, try again.",
                    cancellationToken: cancellationToken);
            }
        }
        else
        {
            await _botClient.SendMessage(
                chatId,
                $"Hi! Your chat ID is `{chatId}`. To use this bot, please, send a verification password.",
                cancellationToken: cancellationToken);
            _logger.LogInformation(
                "Chat ChatId={ChatId} added to waiting list for authentication",
                chatId);
        }
    }

    private async Task SendUnsupportedMessageTypeNotificationAsync(
        long chatId,
        MessageType messageType,
        string commandName,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Command \"{CommandName}\" in chat ChatId={ChatId} received unsupported message type {MessageType}",
            commandName,
            chatId,
            messageType);

        await _botClient.SendMessage(
            chatId,
            $"Command \"{commandName}\" doesn't support {messageType} messages. Please use supported message types.",
            cancellationToken: cancellationToken);
    }
}
