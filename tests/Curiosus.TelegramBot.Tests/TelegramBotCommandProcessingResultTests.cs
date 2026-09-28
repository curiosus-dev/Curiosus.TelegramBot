using Curiosus.TelegramBot.Commands.States;
using Xunit;

namespace Curiosus.TelegramBot.Tests;

public class TelegramBotCommandProcessingResultTests
{
    [Fact]
    public void WithoutState_ReturnsResultWithNullState()
    {
        var result = TelegramBotCommandProcessingResult.WithoutState();

        Assert.Null(result.State);
    }

    [Fact]
    public void WithSimpleState_ReturnsResultWithSimpleCommandState()
    {
        var result = TelegramBotCommandProcessingResult.WithSimpleState();

        Assert.NotNull(result.State);
        Assert.IsType<SimpleCommandState>(result.State);
    }

    [Fact]
    public void WithState_ViaInit_SetsState()
    {
        var state = new SimpleCommandState();

        var result = new TelegramBotCommandProcessingResult { State = state };

        Assert.Same(state, result.State);
    }

    [Fact]
    public void Default_HasNullState()
    {
        var result = new TelegramBotCommandProcessingResult();

        Assert.Null(result.State);
    }
}
