using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Markeli.TelegramBot;

/// <summary>
/// Dispatches Telegram updates for parallel processing.
/// Resolves commands, manages locks, and delegates execution to processor.
/// </summary>
public class TelegramBotUpdateDispatcher : IHostedService
{
    private const int LockSpinIterations = 100;
    private static readonly TimeSpan LockWaitDelay = TimeSpan.FromMilliseconds(50);

    private readonly ILogger<TelegramBotUpdateDispatcher> _logger;
    private readonly TelegramUpdateQueue _queue;
    private readonly TelegramUpdateProcessor _updateProcessor;
    private readonly ITelegramBotClient _botClient;
    private readonly TelegramBotCommandStateCache _stateCache;
    private readonly IReadOnlyList<ITelegramBotCommandHandler> _telegramCommands;
    private readonly ISet<UpdateType> _supportedUpdateTypes;

    private CancellationTokenSource _cancellationTokenSource = null!;
    private Task? _dispatchingTask;

    private readonly SemaphoreSlim _parallelismSemaphore;
    private readonly ConcurrentDictionary<string, byte> _activeLocks = new();
    private int _activeProcessingCount;

    /// <inheritdoc cref="TelegramBotUpdateDispatcher"/>
    public TelegramBotUpdateDispatcher(
        ILogger<TelegramBotUpdateDispatcher> logger,
        TelegramBotOptions botOptions,
        TelegramUpdateQueue queue,
        TelegramUpdateProcessor updateProcessor,
        ITelegramBotClient botClient,
        IEnumerable<ITelegramBotCommandHandler> commands,
        TelegramBotCommandStateCache stateCache)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(botOptions);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(updateProcessor);
        ArgumentNullException.ThrowIfNull(botClient);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(stateCache);

        botOptions.AssertValid();

        _logger = logger;
        _queue = queue;
        _updateProcessor = updateProcessor;
        _botClient = botClient;
        _stateCache = stateCache;

        _telegramCommands = commands.ToArray();
        _supportedUpdateTypes = new HashSet<UpdateType>(_telegramCommands
            .SelectMany(x => x.SupportedUpdateTypes)
            .Distinct()
            .ToArray());

        _parallelismSemaphore = new SemaphoreSlim(botOptions.MaxDegreeOfParallelism);
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting TelegramBotUpdateDispatcher...");

        _cancellationTokenSource = new CancellationTokenSource();

        await _queue.LoadPersistedUpdatesAsync(cancellationToken);

        _dispatchingTask = DispatchUpdatesAsync(_cancellationTokenSource.Token);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = _supportedUpdateTypes.ToArray()
        };

        _botClient.StartReceiving(
            HandleUpdateAsync,
            HandleTelegramBotErrorAsync,
            receiverOptions,
            _cancellationTokenSource.Token);

        _logger.LogDebug("TelegramBotUpdateDispatcher started");
    }

    private Task HandleUpdateAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Received update {UpdateId}, enqueueing", update.Id);
        _queue.Enqueue(update);
        return Task.CompletedTask;
    }

    private async Task DispatchUpdatesAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();

        _logger.LogDebug("Update dispatching started");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var update = _queue.Take(cancellationToken);

                var chatId = update.GetChatId();
                if (!IsValidUpdateType(update))
                {
                    _logger.LogWarning(
                        "Received unsupported update type {UpdateType} in chat ChatId={chatId}",
                        update.Type,
                        chatId);
                    continue;
                }

                await DispatchUpdateAsync(update, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Update dispatching stopping due to cancellation");
                break;
            }
            catch (InvalidOperationException)
            {
                _logger.LogDebug("Queue completed, stopping dispatching");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching update");
            }
        }

        _logger.LogDebug("Update dispatching finished");
    }

    private async Task DispatchUpdateAsync(Update update, CancellationToken cancellationToken)
    {
        var chatId = update.GetChatId();
        var command = ResolveCommand(update, chatId);

        if (command == null)
        {
            await SendUnsupportedCommandNotificationAsync(chatId, update, cancellationToken);
            return;
        }

        if (command.TryGetLockKey(update, out var lockKey))
        {
            var acquired = await TryAcquireLockAsync(lockKey!, cancellationToken);
            if (!acquired)
            {
                _logger.LogDebug(
                    "Lock {LockKey} is busy for update {UpdateId}, re-enqueueing",
                    lockKey,
                    update.Id);
                _queue.Enqueue(update);
                return;
            }
        }

        await _parallelismSemaphore.WaitAsync(cancellationToken);
        var processorId = Interlocked.Increment(ref _activeProcessingCount);

        var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["ProcessorId"] = processorId,
            ["UpdateId"] = update.Id,
            ["ChatId"] = chatId,
            ["Command"] = command.CommandText
        });

        _ = _updateProcessor.ProcessAsync(update, command, cancellationToken)
            .ContinueWith(
                task =>
                {
                    try
                    {
                        if (task.IsFaulted)
                        {
                            _logger.LogError(
                                task.Exception?.GetBaseException(),
                                "Error processing update");
                        }
                    }
                    finally
                    {
                        scope?.Dispose();

                        if (lockKey != null)
                        {
                            ReleaseLock(lockKey);
                        }

                        _parallelismSemaphore.Release();
                        Interlocked.Decrement(ref _activeProcessingCount);
                    }
                },
                cancellationToken);
    }

    internal ITelegramBotCommandHandler? ResolveCommand(Update update, long chatId)
    {
        var message = update.Message;
        if (message == null)
            return null;

        var messageText = update.GetMessageText()?.Trim();
        if (String.IsNullOrWhiteSpace(messageText) && message.Type != MessageType.Document)
            return null;

        var cachedState = _stateCache.GetEntry(chatId);

        if (cachedState != null)
        {
            var command = cachedState.CommandHandler;

            if (!String.IsNullOrEmpty(messageText) && messageText.StartsWith("/"))
            {
                var nestedCommand = _telegramCommands.FirstOrDefault(x => messageText.StartsWith(x.CommandText));
                if (nestedCommand != null)
                {
                    return nestedCommand;
                }
            }

            return command;
        }

        if (!String.IsNullOrEmpty(messageText))
        {
            return _telegramCommands.FirstOrDefault(x => messageText.StartsWith(x.CommandText));
        }

        return null;
    }

    private async Task SendUnsupportedCommandNotificationAsync(
        long chatId,
        Update update,
        CancellationToken cancellationToken)
    {
        var messageText = update.GetMessageText()?.Trim() ?? "";

        _logger.LogDebug(
            "Unsupported command in chat ChatId={ChatId}. Command - \"{Command}\"",
            chatId,
            messageText);

        try
        {
            await _botClient.SendMessage(
                chatId,
                "Unsupported command. I can process only limited list of commands. Please, click on \"Menu\" to list all of them.",
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send unsupported command notification to chat {ChatId}", chatId);
        }
    }

    private bool IsValidUpdateType(Update update)
    {
        return _supportedUpdateTypes.Contains(update.Type);
    }

    private async Task<bool> TryAcquireLockAsync(string lockKey, CancellationToken cancellationToken)
    {
        if (_activeLocks.TryAdd(lockKey, 0))
            return true;

        for (var i = 0; i < LockSpinIterations; i++)
        {
            Thread.SpinWait(10);

            if (_activeLocks.TryAdd(lockKey, 0))
                return true;
        }

        for (var i = 0; i < 3; i++)
        {
            await Task.Delay(LockWaitDelay, cancellationToken);

            if (_activeLocks.TryAdd(lockKey, 0))
                return true;
        }

        return false;
    }

    private void ReleaseLock(string lockKey)
    {
        _activeLocks.TryRemove(lockKey, out _);
    }

    private Task HandleTelegramBotErrorAsync(
        ITelegramBotClient botClient,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Error while receiving Telegram messages");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Stopping TelegramBotUpdateDispatcher...");

        _cancellationTokenSource.Cancel();
        _queue.CompleteAdding();

        if (_dispatchingTask != null)
        {
            try
            {
                await _dispatchingTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Update dispatching did not complete in time");
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        var waitIterations = 0;
        while (_activeProcessingCount > 0 && waitIterations < 50)
        {
            await Task.Delay(100, CancellationToken.None);
            waitIterations++;
        }

        if (_activeProcessingCount > 0)
        {
            _logger.LogWarning(
                "Stopping with {Count} updates still being processed",
                _activeProcessingCount);
        }

        await _queue.PersistRemainingUpdatesAsync(CancellationToken.None);

        _logger.LogDebug("TelegramBotUpdateDispatcher stopped");
    }
}
