using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Markeli.TelegramBot;

/// <summary>
/// Processes Telegram updates. Handles authentication, validation, and command execution.
/// </summary>
public class TelegramUpdateProcessor
{
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

    private bool IsCommandSupportedForUpdate(ITelegramBotCommandHandler command, Update update)
    {
        if (!command.SupportedUpdateTypes.Contains(update.Type))
            return false;

        return update.Message == null || command.SupportedMessageTypes.Contains(update.Message.Type);
    }

    private async Task ExecuteCommandAsync(
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

        ITelegramBotCommandState? commandState = null;
        if (cachedState?.CommandHandler == command)
        {
            commandState = cachedState.CommandState;
        }
        else if (cachedState != null && cachedState.CommandHandler != command)
        {
            _stateCache.RemoveEntry(chatId);
        }

        var result = await command.ProcessCommandAsync(
            _botClient,
            update,
            commandState,
            cancellationToken);

        _logger.LogDebug(
            "Executed command \"{CommandName}\" in chat ChatId={ChatId}",
            command.CommandName,
            chatId);

        if (result.State == null)
        {
            _stateCache.RemoveEntry(chatId);
        }
        else
        {
            _stateCache.SetEntry(chatId, command, result.State);
        }
    }

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
