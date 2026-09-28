using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot.Types;
using Xunit;
using IOFile = System.IO.File;

namespace Curiosus.TelegramBot.Tests;

public class TelegramUpdateQueueTests : IDisposable
{
    private readonly Mock<ILogger<TelegramUpdateQueue>> _logger = new();
    private readonly string _tempDir;

    public TelegramUpdateQueueTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private TelegramUpdateQueue CreateQueue(string? persistencePath = null)
    {
        var options = new TelegramBotOptions
        {
            ApiToken = "test",
            Password = "test",
            QueuePersistenceFilePath = persistencePath
        };
        return new TelegramUpdateQueue(_logger.Object, options);
    }

    [Fact]
    public void Enqueue_IncreasesCount()
    {
        using var queue = CreateQueue();

        queue.Enqueue(new Update { Id = 1 });
        queue.Enqueue(new Update { Id = 2 });

        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void Take_ReturnsEnqueuedUpdate()
    {
        using var queue = CreateQueue();
        var update = new Update { Id = 42 };

        queue.Enqueue(update);
        var result = queue.Take(CancellationToken.None);

        Assert.Equal(42, result.Id);
    }

    [Fact]
    public void Take_MaintainsFifoOrder()
    {
        using var queue = CreateQueue();

        queue.Enqueue(new Update { Id = 1 });
        queue.Enqueue(new Update { Id = 2 });
        queue.Enqueue(new Update { Id = 3 });

        Assert.Equal(1, queue.Take(CancellationToken.None).Id);
        Assert.Equal(2, queue.Take(CancellationToken.None).Id);
        Assert.Equal(3, queue.Take(CancellationToken.None).Id);
    }

    [Fact]
    public void Take_WhenCancelled_ThrowsOperationCanceledException()
    {
        using var queue = CreateQueue();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => queue.Take(cts.Token));
    }

    [Fact]
    public void CompleteAdding_ThenTake_ThrowsInvalidOperationException()
    {
        using var queue = CreateQueue();
        queue.CompleteAdding();

        Assert.Throws<InvalidOperationException>(() => queue.Take(CancellationToken.None));
    }

    [Fact]
    public void CompleteAdding_CalledTwice_DoesNotThrow()
    {
        using var queue = CreateQueue();

        queue.CompleteAdding();
        queue.CompleteAdding();
    }

    [Fact]
    public void Dispose_ThenEnqueue_ThrowsObjectDisposedException()
    {
        var queue = CreateQueue();
        queue.Dispose();

        Assert.Throws<ObjectDisposedException>(() => queue.Enqueue(new Update { Id = 1 }));
    }

    [Fact]
    public async Task PersistRemainingUpdatesAsync_SavesUpdatesToFile()
    {
        var filePath = Path.Combine(_tempDir, "queue.json");
        using var queue = CreateQueue(filePath);

        queue.Enqueue(new Update { Id = 10 });
        queue.Enqueue(new Update { Id = 20 });

        await queue.PersistRemainingUpdatesAsync(CancellationToken.None);

        Assert.True(IOFile.Exists(filePath));
        var json = await IOFile.ReadAllTextAsync(filePath);
        var updates = JsonSerializer.Deserialize<List<Update>>(json);
        Assert.NotNull(updates);
        Assert.Equal(2, updates!.Count);
    }

    [Fact]
    public async Task LoadPersistedUpdatesAsync_LoadsUpdatesFromFile()
    {
        var filePath = Path.Combine(_tempDir, "queue.json");
        var updates = new List<Update>
        {
            new() { Id = 100 },
            new() { Id = 200 }
        };
        await IOFile.WriteAllTextAsync(filePath, JsonSerializer.Serialize(updates));

        using var queue = CreateQueue(filePath);
        await queue.LoadPersistedUpdatesAsync(CancellationToken.None);

        Assert.Equal(2, queue.Count);
        Assert.Equal(100, queue.Take(CancellationToken.None).Id);
        Assert.Equal(200, queue.Take(CancellationToken.None).Id);
    }

    [Fact]
    public async Task LoadPersistedUpdatesAsync_DeletesFileAfterLoading()
    {
        var filePath = Path.Combine(_tempDir, "queue.json");
        await IOFile.WriteAllTextAsync(filePath, JsonSerializer.Serialize(new List<Update> { new() { Id = 1 } }));

        using var queue = CreateQueue(filePath);
        await queue.LoadPersistedUpdatesAsync(CancellationToken.None);

        Assert.False(IOFile.Exists(filePath));
    }

    [Fact]
    public async Task LoadPersistedUpdatesAsync_WithNoFile_DoesNotThrow()
    {
        var filePath = Path.Combine(_tempDir, "nonexistent.json");
        using var queue = CreateQueue(filePath);

        await queue.LoadPersistedUpdatesAsync(CancellationToken.None);

        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task LoadPersistedUpdatesAsync_WithNoPersistencePath_Skips()
    {
        using var queue = CreateQueue(null);

        await queue.LoadPersistedUpdatesAsync(CancellationToken.None);

        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task PersistRemainingUpdatesAsync_WithNoPersistencePath_Skips()
    {
        using var queue = CreateQueue(null);
        queue.Enqueue(new Update { Id = 1 });

        await queue.PersistRemainingUpdatesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PersistRemainingUpdatesAsync_WithEmptyQueue_DoesNotCreateFile()
    {
        var filePath = Path.Combine(_tempDir, "queue.json");
        using var queue = CreateQueue(filePath);

        await queue.PersistRemainingUpdatesAsync(CancellationToken.None);

        Assert.False(IOFile.Exists(filePath));
    }
}
