using Curiosus.TelegramBot.Commands.States;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;

namespace Curiosus.TelegramBot.Tests;

public class TelegramBotCommandStateCacheTests
{
    private readonly TelegramBotCommandStateCache _cache;

    public TelegramBotCommandStateCacheTests()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        _cache = new TelegramBotCommandStateCache(memoryCache);
    }

    [Fact]
    public void GetEntry_WhenNoEntry_ReturnsNull()
    {
        var result = _cache.GetEntry(123);

        Assert.Null(result);
    }

    [Fact]
    public void SetEntry_WithState_CanBeRetrieved()
    {
        var handler = new Mock<ITelegramBotCommandHandler>().Object;
        var state = new SimpleCommandState();

        _cache.SetEntry(123, handler, state);

        var entry = _cache.GetEntry(123);
        Assert.NotNull(entry);
        Assert.Same(handler, entry!.CommandHandler);
        Assert.Same(state, entry.CommandState);
    }

    [Fact]
    public void SetEntry_WithNullState_RemovesEntry()
    {
        var handler = new Mock<ITelegramBotCommandHandler>().Object;
        var state = new SimpleCommandState();

        _cache.SetEntry(123, handler, state);
        _cache.SetEntry(123, handler, null);

        Assert.Null(_cache.GetEntry(123));
    }

    [Fact]
    public void RemoveEntry_RemovesExistingEntry()
    {
        var handler = new Mock<ITelegramBotCommandHandler>().Object;
        var state = new SimpleCommandState();

        _cache.SetEntry(123, handler, state);
        _cache.RemoveEntry(123);

        Assert.Null(_cache.GetEntry(123));
    }

    [Fact]
    public void DifferentChatIds_HaveIndependentEntries()
    {
        var handler1 = new Mock<ITelegramBotCommandHandler>().Object;
        var handler2 = new Mock<ITelegramBotCommandHandler>().Object;
        var state1 = new SimpleCommandState();
        var state2 = new SimpleCommandState();

        _cache.SetEntry(1, handler1, state1);
        _cache.SetEntry(2, handler2, state2);

        Assert.Same(handler1, _cache.GetEntry(1)!.CommandHandler);
        Assert.Same(handler2, _cache.GetEntry(2)!.CommandHandler);
    }
}
