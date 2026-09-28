using Microsoft.Extensions.DependencyInjection;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Xunit;

namespace Markeli.TelegramBot.Tests;

public class HelpCommandHandlerTests
{
    private static Mock<ITelegramBotCommandHandler> CreateMockHandler(
        string commandName, string commandText)
    {
        var mock = new Mock<ITelegramBotCommandHandler>();
        mock.Setup(h => h.CommandName).Returns(commandName);
        mock.Setup(h => h.CommandText).Returns(commandText);
        return mock;
    }

    private static (HelpCommandHandler handler, ServiceProvider provider) BuildHandler(
        params ITelegramBotCommandHandler[] otherHandlers)
    {
        var services = new ServiceCollection();
        foreach (var h in otherHandlers)
            services.AddSingleton<ITelegramBotCommandHandler>(h);
        services.AddSingleton<ITelegramBotCommandHandler, HelpCommandHandler>();
        var provider = services.BuildServiceProvider();
        var handler = provider.GetServices<ITelegramBotCommandHandler>()
            .OfType<HelpCommandHandler>()
            .Single();
        return (handler, provider);
    }

    private static Update CreateTextUpdate(long chatId, string text)
    {
        return new Update
        {
            Message = new Message
            {
                Chat = new Chat { Id = chatId },
                Text = text
            }
        };
    }

    [Fact]
    public async Task ProcessCommandAsync_ListsAllRegisteredCommands()
    {
        var ping = CreateMockHandler("Ping", "/ping");
        var greet = CreateMockHandler("Greet", "/greet");
        var (helpHandler, provider) = BuildHandler(ping.Object, greet.Object);
        using var _ = provider;

        var botClient = new Mock<ITelegramBotClient>();
        botClient.Setup(c => c.SendRequest(
                It.IsAny<Telegram.Bot.Requests.SendMessageRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message { Chat = new Chat { Id = 1 } });

        var update = CreateTextUpdate(42, "/help");

        var result = await helpHandler.ProcessCommandAsync(
            botClient.Object, update, null, CancellationToken.None);

        Assert.Null(result.State);
        botClient.Verify(c => c.SendRequest(
            It.Is<Telegram.Bot.Requests.SendMessageRequest>(r =>
                r.Text.Contains("/ping") && r.Text.Contains("/greet")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void BuildHelpMessage_ExcludesHelpCommandItself()
    {
        var ping = CreateMockHandler("Ping", "/ping");
        var (helpHandler, provider) = BuildHandler(ping.Object);
        using var _ = provider;

        var message = helpHandler.BuildHelpMessage();

        Assert.Contains("/ping", message);
        Assert.DoesNotContain("/help", message);
    }

    [Fact]
    public void BuildHelpMessage_WithNoOtherCommands_ReturnsNoCommandsMessage()
    {
        var (helpHandler, provider) = BuildHandler();
        using var _ = provider;

        var message = helpHandler.BuildHelpMessage();

        Assert.Equal("No commands available.", message);
    }

    [Fact]
    public async Task ProcessCommandAsync_ReturnsWithoutState()
    {
        var (helpHandler, provider) = BuildHandler();
        using var _ = provider;

        var botClient = new Mock<ITelegramBotClient>();
        botClient.Setup(c => c.SendRequest(
                It.IsAny<Telegram.Bot.Requests.SendMessageRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message { Chat = new Chat { Id = 1 } });

        var update = CreateTextUpdate(42, "/help");

        var result = await helpHandler.ProcessCommandAsync(
            botClient.Object, update, null, CancellationToken.None);

        Assert.Null(result.State);
    }

    [Fact]
    public void BuildHelpMessage_SortsCommandsAlphabetically()
    {
        var zeta = CreateMockHandler("Zeta", "/zeta");
        var alpha = CreateMockHandler("Alpha", "/alpha");
        var (helpHandler, provider) = BuildHandler(zeta.Object, alpha.Object);
        using var _ = provider;

        var message = helpHandler.BuildHelpMessage();

        var alphaIndex = message.IndexOf("/alpha", StringComparison.Ordinal);
        var zetaIndex = message.IndexOf("/zeta", StringComparison.Ordinal);
        Assert.True(alphaIndex < zetaIndex);
    }

    [Fact]
    public void CommandProperties_AreCorrect()
    {
        var (handler, provider) = BuildHandler();
        using var _ = provider;

        Assert.Equal("Help", handler.CommandName);
        Assert.Equal("/help", handler.CommandText);
        Assert.Contains(UpdateType.Message, handler.SupportedUpdateTypes);
        Assert.Contains(MessageType.Text, handler.SupportedMessageTypes);
    }

    [Fact]
    public void Constructor_WithNullServiceProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new HelpCommandHandler(null!));
    }
}
