using Curiosus.TelegramBot.Commands.States;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Xunit;

namespace Curiosus.TelegramBot.Tests;

public class TelegramBotUpdateDispatcherTests
{
    private readonly Mock<ILogger<TelegramBotUpdateDispatcher>> _logger = new();
    private readonly Mock<ITelegramBotClient> _botClient = new();
    private readonly Mock<TelegramUpdateProcessor> _processor;
    private readonly TelegramUpdateQueue _queue;
    private readonly TelegramBotCommandStateCache _stateCache;
    private readonly TelegramBotOptions _options;

    public TelegramBotUpdateDispatcherTests()
    {
        _options = new TelegramBotOptions
        {
            ApiToken = "test-token",
            Password = "test",
            MaxDegreeOfParallelism = 2
        };

        var queueLogger = new Mock<ILogger<TelegramUpdateQueue>>();
        _queue = new TelegramUpdateQueue(queueLogger.Object, _options);

        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        _stateCache = new TelegramBotCommandStateCache(memoryCache);

        var processorLogger = new Mock<ILogger<TelegramUpdateProcessor>>();
        _processor = new Mock<TelegramUpdateProcessor>(
            processorLogger.Object,
            _botClient.Object,
            _options,
            _stateCache);
    }

    private static Mock<ITelegramBotCommandHandler> CreateMockCommand(
        string name = "test",
        string? text = "/test")
    {
        var mock = new Mock<ITelegramBotCommandHandler>();
        mock.Setup(x => x.CommandName).Returns(name);
        mock.Setup(x => x.CommandText).Returns(text);
        mock.Setup(x => x.SupportedUpdateTypes).Returns(new HashSet<UpdateType> { UpdateType.Message });
        mock.Setup(x => x.SupportedMessageTypes).Returns(new HashSet<MessageType> { MessageType.Text });
        mock.Setup(x => x.ProcessCommandAsync(
                It.IsAny<ITelegramBotClient>(),
                It.IsAny<Update>(),
                It.IsAny<ITelegramBotCommandState?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TelegramBotCommandProcessingResult.WithoutState());
        return mock;
    }

    private TelegramBotUpdateDispatcher CreateDispatcher(
        IEnumerable<ITelegramBotCommandHandler>? commands = null)
    {
        return new TelegramBotUpdateDispatcher(
            _logger.Object,
            _options,
            _queue,
            _processor.Object,
            _botClient.Object,
            commands ?? new[] { CreateMockCommand().Object },
            _stateCache);
    }

    [Fact]
    public void Constructor_WithInvalidOptions_Throws()
    {
        var invalidOptions = new TelegramBotOptions
        {
            ApiToken = "",
            Password = ""
        };

        Assert.Throws<InvalidOperationException>(() =>
            new TelegramBotUpdateDispatcher(
                _logger.Object,
                invalidOptions,
                _queue,
                _processor.Object,
                _botClient.Object,
                Array.Empty<ITelegramBotCommandHandler>(),
                _stateCache));
    }

    [Fact]
    public void Constructor_WithNullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TelegramBotUpdateDispatcher(
                null!,
                _options,
                _queue,
                _processor.Object,
                _botClient.Object,
                Array.Empty<ITelegramBotCommandHandler>(),
                _stateCache));
    }

    [Theory]
    [InlineData("/ping", "/ping", true)]
    [InlineData("/ping now", "/ping", true)]
    [InlineData("/ping\nnow", "/ping", true)]
    [InlineData("/ping@MyBot", "/ping", true)]
    [InlineData("Show report", "Show report", true)]
    [InlineData("Show report today", "Show report", true)]
    [InlineData("/pinger", "/ping", false)]
    [InlineData("/pin", "/ping", false)]
    [InlineData("/PING", "/ping", false)]
    [InlineData("Show reports", "Show report", false)]
    [InlineData("/ping", "", false)]
    public void IsCommandMatch_MatchesWholeCommandText(string messageText, string commandText, bool expected)
    {
        Assert.Equal(expected, TelegramBotUpdateDispatcher.IsCommandMatch(messageText, commandText));
    }

    [Fact]
    public void ResolveCommand_CommandWithLongerName_DoesNotResolveShorterCommand()
    {
        var ping = CreateMockCommand("ping", "/ping");
        var dispatcher = CreateDispatcher(new[] { ping.Object });

        var resolved = dispatcher.ResolveCommand(CreateRichUpdate(100, "/pinger"), 100);

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveCommand_SeveralCommandsMatch_ResolvesLongestCommandText()
    {
        var report = CreateMockCommand("report", "/report");
        var dailyReport = CreateMockCommand("daily report", "/report daily");
        var dispatcher = CreateDispatcher(new[] { report.Object, dailyReport.Object });

        var resolved = dispatcher.ResolveCommand(CreateRichUpdate(100, "/report daily now"), 100);

        Assert.Same(dailyReport.Object, resolved);
    }

    [Fact]
    public void ResolveCommand_CachedStateAndLongerCommandName_ReturnsCachedHandler()
    {
        var ping = CreateMockCommand("ping", "/ping");
        var dispatcher = CreateDispatcher(new[] { ping.Object });
        _stateCache.SetEntry(100, ping.Object, new SimpleCommandState());

        var resolved = dispatcher.ResolveCommand(CreateRichUpdate(100, "/pinger"), 100);

        Assert.Same(ping.Object, resolved);
    }

    private static Update CreateRichUpdate(long chatId, string text)
    {
        return new Update
        {
            Message = new Message
            {
                Chat = new Chat { Id = chatId },
                RichMessage = new RichMessage
                {
                    Blocks = [new RichBlockParagraph { Text = new RichTextText { Text = text } }]
                }
            }
        };
    }

    [Fact]
    public void ResolveCommand_RichMessageStartingWithCommand_ResolvesCommand()
    {
        var command = CreateMockCommand("report", "/report");
        var dispatcher = CreateDispatcher(new[] { command.Object });

        var resolved = dispatcher.ResolveCommand(CreateRichUpdate(100, "/report daily"), 100);

        Assert.Same(command.Object, resolved);
    }

    [Fact]
    public void ResolveCommand_RichMessageWithCachedState_ReturnsCachedHandler()
    {
        var command = CreateMockCommand();
        var dispatcher = CreateDispatcher(new[] { command.Object });
        _stateCache.SetEntry(100, command.Object, new SimpleCommandState());

        // A rich message carries no Message.Text: before rich support it fell through to
        // "unsupported command" and abandoned the multi-step state.
        var resolved = dispatcher.ResolveCommand(CreateRichUpdate(100, "some answer"), 100);

        Assert.Same(command.Object, resolved);
    }

    [Fact]
    public void ResolveCommand_MessageWithoutAnyText_ReturnsNull()
    {
        var command = CreateMockCommand();
        var dispatcher = CreateDispatcher(new[] { command.Object });

        var update = new Update
        {
            Message = new Message { Chat = new Chat { Id = 100 } }
        };

        Assert.Null(dispatcher.ResolveCommand(update, 100));
    }

    [Fact]
    public async Task StartAsync_StopAsync_Lifecycle()
    {
        var dispatcher = CreateDispatcher();

        await dispatcher.StartAsync(CancellationToken.None);
        await dispatcher.StopAsync(CancellationToken.None);

        // Queue should be marked as complete — enqueueing should fail
        Assert.Throws<InvalidOperationException>(() =>
            _queue.Enqueue(new Update { Id = 1 }));
    }

    private static Mock<ITelegramBotCommandHandler> CreateButtonCommand(
        string name,
        string? prefix,
        bool supportsCallbacks = true,
        string? text = null)
    {
        var mock = CreateMockCommand(name, text);
        mock.Setup(x => x.CallbackDataPrefix).Returns(prefix);
        var updateTypes = new HashSet<UpdateType> { UpdateType.Message };
        if (supportsCallbacks)
            updateTypes.Add(UpdateType.CallbackQuery);
        mock.Setup(x => x.SupportedUpdateTypes).Returns(updateTypes);
        return mock;
    }

    private static Update CreateCallbackUpdate(long chatId, string data)
    {
        return new Update
        {
            CallbackQuery = new CallbackQuery
            {
                Id = "query-1",
                Data = data,
                Message = new Message { Chat = new Chat { Id = chatId } }
            }
        };
    }

    [Fact]
    public void ResolveCommand_CallbackWithMatchingPrefix_ReturnsHandler()
    {
        var returns = CreateButtonCommand("returns", "ret");
        var dispatcher = CreateDispatcher(new[] { CreateMockCommand().Object, returns.Object });

        var resolved = dispatcher.ResolveCommand(CreateCallbackUpdate(100, "ret:42:1"), 100);

        Assert.Same(returns.Object, resolved);
    }

    [Fact]
    public void ResolveCommand_CallbackPrefixFollowedByOtherChars_ReturnsNull()
    {
        var returns = CreateButtonCommand("returns", "ret");
        var dispatcher = CreateDispatcher(new[] { returns.Object });

        var resolved = dispatcher.ResolveCommand(CreateCallbackUpdate(100, "return:1"), 100);

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveCommand_CallbackWithoutPrefixAndStateHolderSupportsCallbacks_ReturnsStateHolder()
    {
        var dialog = CreateButtonCommand("dialog", null, text: "/dialog");
        var dispatcher = CreateDispatcher(new[] { dialog.Object });
        _stateCache.SetEntry(100, dialog.Object, new SimpleCommandState());

        var resolved = dispatcher.ResolveCommand(CreateCallbackUpdate(100, "yes"), 100);

        Assert.Same(dialog.Object, resolved);
    }

    [Fact]
    public void ResolveCommand_CallbackWithoutPrefixAndStateHolderWithoutCallbacks_ReturnsNull()
    {
        var dialog = CreateMockCommand("dialog", "/dialog");
        var dispatcher = CreateDispatcher(new[] { dialog.Object });
        _stateCache.SetEntry(100, dialog.Object, new SimpleCommandState());

        var resolved = dispatcher.ResolveCommand(CreateCallbackUpdate(100, "yes"), 100);

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveCommand_CallbackPrefixWinsOverStateHolder()
    {
        var dialog = CreateButtonCommand("dialog", null, text: "/dialog");
        var returns = CreateButtonCommand("returns", "ret");
        var dispatcher = CreateDispatcher(new[] { dialog.Object, returns.Object });
        _stateCache.SetEntry(100, dialog.Object, new SimpleCommandState());

        var resolved = dispatcher.ResolveCommand(CreateCallbackUpdate(100, "ret:1"), 100);

        Assert.Same(returns.Object, resolved);
    }

    [Fact]
    public void Constructor_DuplicateCallbackPrefix_ThrowsNamingHandlers()
    {
        var first = CreateButtonCommand("first returns", "ret");
        var second = CreateButtonCommand("second returns", "ret");

        var exception = Assert.Throws<InvalidOperationException>(
            () => CreateDispatcher(new[] { first.Object, second.Object }));

        Assert.Contains("first returns", exception.Message);
        Assert.Contains("second returns", exception.Message);
    }

    [Theory]
    [InlineData("a:b")]
    [InlineData("")]
    public void Constructor_InvalidCallbackPrefix_Throws(string prefix)
    {
        var returns = CreateButtonCommand("returns", prefix);

        Assert.Throws<InvalidOperationException>(() => CreateDispatcher(new[] { returns.Object }));
    }

    [Fact]
    public void Constructor_PrefixWithoutCallbackQueryUpdateType_Throws()
    {
        var returns = CreateButtonCommand("returns", "ret", supportsCallbacks: false);

        var exception = Assert.Throws<InvalidOperationException>(() => CreateDispatcher(new[] { returns.Object }));

        Assert.Contains("returns", exception.Message);
    }

    [Fact]
    public void ResolveCommand_MessageWithButtonOnlyHandlers_ReturnsNull()
    {
        var returns = CreateButtonCommand("returns", "ret");
        var legacy = CreateButtonCommand("legacy buttons", "old", text: string.Empty);
        var dispatcher = CreateDispatcher(new[] { returns.Object, legacy.Object, CreateMockCommand().Object });

        Assert.Null(dispatcher.ResolveCommand(CreateRichUpdate(100, "hello"), 100));
    }

    [Fact]
    public void ResolveCommand_MessageMatchingEmptyCommandText_ReturnsNull()
    {
        var returns = CreateButtonCommand("returns", "ret", text: string.Empty);
        var dispatcher = CreateDispatcher(new[] { returns.Object });

        var resolved = dispatcher.ResolveCommand(CreateRichUpdate(100, "hello"), 100);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task DispatchUpdateAsync_UnknownCallback_AnswersAndSendsNoMessage()
    {
        var returns = CreateButtonCommand("returns", "ret");
        var dispatcher = CreateDispatcher(new[] { returns.Object });

        await dispatcher.DispatchUpdateAsync(CreateCallbackUpdate(100, "old:1"), CancellationToken.None);

        _botClient.Verify(
            x => x.SendRequest(
                It.Is<Telegram.Bot.Requests.AnswerCallbackQueryRequest>(r => r.CallbackQueryId == "query-1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _botClient.Verify(
            x => x.SendRequest(It.IsAny<Telegram.Bot.Requests.SendMessageRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
