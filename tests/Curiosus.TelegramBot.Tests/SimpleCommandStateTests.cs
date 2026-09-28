using Curiosus.TelegramBot.Commands.States;
using Xunit;

namespace Curiosus.TelegramBot.Tests;

public class SimpleCommandStateTests
{
    [Fact]
    public void CreatedAt_IsSetToUtcNow()
    {
        var before = DateTime.UtcNow;
        var state = new SimpleCommandState();
        var after = DateTime.UtcNow;

        Assert.InRange(state.CreatedAt, before, after);
    }

    [Fact]
    public void LastModifiedAt_InitiallyEqualsCreatedAt()
    {
        var state = new SimpleCommandState();

        Assert.Equal(state.CreatedAt, state.LastModifiedAt);
    }

    [Fact]
    public void LastModifiedAt_CanBeUpdated()
    {
        var state = new SimpleCommandState();
        var newTime = DateTime.UtcNow.AddMinutes(5);

        state.LastModifiedAt = newTime;

        Assert.Equal(newTime, state.LastModifiedAt);
        Assert.NotEqual(state.CreatedAt, state.LastModifiedAt);
    }

    [Fact]
    public void Implements_ITelegramBotCommandState()
    {
        var state = new SimpleCommandState();

        Assert.IsAssignableFrom<ITelegramBotCommandState>(state);
    }

    [Fact]
    public void InheritsFrom_TelegramBotCommandStateBase()
    {
        var state = new SimpleCommandState();

        Assert.IsAssignableFrom<TelegramBotCommandStateBase>(state);
    }
}
