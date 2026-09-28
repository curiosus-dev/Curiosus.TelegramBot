using Markeli.TelegramBot.Commands.States;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Xunit;

namespace Markeli.TelegramBot.Tests;

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
        string text = "/test")
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
}
