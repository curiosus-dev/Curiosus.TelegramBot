using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using IOFile = System.IO.File;

namespace Markeli.TelegramBot;

/// <summary>
/// Thread-safe queue for Telegram updates with persistence support.
/// </summary>
public sealed class TelegramUpdateQueue : IDisposable
{
    private readonly ILogger<TelegramUpdateQueue> _logger;
    private readonly string? _persistenceFilePath;
    private readonly BlockingCollection<Update> _queue;
    private bool _disposed;

    /// <inheritdoc cref="TelegramUpdateQueue"/>
    public TelegramUpdateQueue(
        ILogger<TelegramUpdateQueue> logger,
        TelegramBotOptions options)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);

        _logger = logger;
        _persistenceFilePath = options.QueuePersistenceFilePath;
        _queue = new BlockingCollection<Update>();
    }

    /// <summary>
    /// Enqueues an update for processing.
    /// </summary>
    public void Enqueue(Update update)
    {
        ThrowIfDisposed();

        _queue.Add(update);
        _logger.LogDebug("Update {UpdateId} enqueued. Queue size: {QueueSize}", update.Id, _queue.Count);
    }

    /// <summary>
    /// Takes an update from the queue, blocking until one is available.
    /// </summary>
    public Update Take(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _queue.Take(cancellationToken);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TelegramUpdateQueue));
    }

    /// <summary>
    /// Marks the queue as complete for adding (no more items will be added).
    /// </summary>
    public void CompleteAdding()
    {
        if (!_queue.IsAddingCompleted)
        {
            _queue.CompleteAdding();
        }
    }

    /// <summary>
    /// Gets the current number of items in the queue.
    /// </summary>
    public int Count => _queue.Count;

    /// <summary>
    /// Loads persisted updates from disk into the queue.
    /// Should be called during startup.
    /// </summary>
    public async Task LoadPersistedUpdatesAsync(CancellationToken cancellationToken)
    {
        if (String.IsNullOrWhiteSpace(_persistenceFilePath))
        {
            _logger.LogDebug("Queue persistence file path not configured, skipping load");
            return;
        }

        if (!IOFile.Exists(_persistenceFilePath))
        {
            _logger.LogDebug("Queue persistence file not found at {Path}, starting with empty queue", _persistenceFilePath);
            return;
        }

        try
        {
            var json = await IOFile.ReadAllTextAsync(_persistenceFilePath, cancellationToken);
            var updates = JsonSerializer.Deserialize<List<Update>>(json);

            if (updates is { Count: > 0 })
            {
                foreach (var update in updates)
                {
                    _queue.Add(update, cancellationToken);
                }

                _logger.LogInformation(
                    "Loaded {Count} persisted updates from {Path}",
                    updates.Count,
                    _persistenceFilePath);
            }

            IOFile.Delete(_persistenceFilePath);
            _logger.LogDebug("Deleted persistence file after loading");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load persisted updates from {Path}", _persistenceFilePath);
        }
    }

    /// <summary>
    /// Persists remaining updates to disk.
    /// Should be called during shutdown.
    /// </summary>
    public async Task PersistRemainingUpdatesAsync(CancellationToken cancellationToken)
    {
        if (String.IsNullOrWhiteSpace(_persistenceFilePath))
        {
            _logger.LogDebug("Queue persistence file path not configured, skipping persist");
            return;
        }

        var remainingUpdates = new List<Update>();
        while (_queue.TryTake(out var update))
        {
            remainingUpdates.Add(update);
        }

        if (remainingUpdates.Count == 0)
        {
            _logger.LogDebug("No remaining updates to persist");
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_persistenceFilePath);
            if (!String.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(remainingUpdates, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await IOFile.WriteAllTextAsync(_persistenceFilePath, json, cancellationToken);

            _logger.LogInformation(
                "Persisted {Count} updates to {Path}",
                remainingUpdates.Count,
                _persistenceFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist {Count} updates to {Path}. Updates will be lost",
                remainingUpdates.Count,
                _persistenceFilePath);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _queue.Dispose();
    }
}
