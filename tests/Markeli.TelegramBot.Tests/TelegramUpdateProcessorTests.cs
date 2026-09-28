using Markeli.TelegramBot.Commands.States;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Xunit;

namespace Markeli.TelegramBot.Tests;

public class TelegramUpdateProcessorTests
{
    private readonly Mock<ILogger<TelegramUpdateProcessor>> _logger = new();
    private readonly Mock<ITelegramBotClient> _botClient = new();
    private readonly TelegramBotCommandStateCache _stateCache;
    private readonly TelegramBotOptions _options;

    public TelegramUpdateProcessorTests()
    {
        _options = new TelegramBotOptions
        {
            ApiToken = "test-token",
            Password = "secret",
            AllowedChatIds = new[] { 100L }
        };
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        _stateCache = new TelegramBotCommandStateCache(memoryCache);

        _botClient
            .Setup(x => x.SendRequest(
                It.IsAny<SendMessageRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message { Chat = new Chat { Id = 1 } });
    }

    private TelegramUpdateProcessor CreateProcessor(TelegramBotOptions? options = null)
    {
        return new TelegramUpdateProcessor(
            _logger.Object,
            _botClient.Object,
            options ?? _options,
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

    private static Update CreateTextUpdate(long chatId, string text = "/test")
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
    public async Task ProcessAsync_AllowedChat_ExecutesCommand()
    {
        var processor = CreateProcessor();
        var command = CreateMockCommand();
        var update = CreateTextUpdate(100);

        await processor.ProcessAsync(update, command.Object, CancellationToken.None);

        command.Verify(x => x.ProcessCommandAsync(
            _botClient.Object,
            update,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_UnauthenticatedChat_PromptsForPassword()
    {
        var processor = CreateProcessor();
        var command = CreateMockCommand();
        var update = CreateTextUpdate(999);

        await processor.ProcessAsync(update, command.Object, CancellationToken.None);

        command.Verify(x => x.ProcessCommandAsync(
            It.IsAny<ITelegramBotClient>(),
            It.IsAny<Update>(),
            It.IsAny<ITelegramBotCommandState?>(),
            It.IsAny<CancellationToken>()), Times.Never);

        _botClient.Verify(x => x.SendRequest(
            It.IsAny<SendMessageRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_CorrectPassword_AuthenticatesChat()
    {
        var processor = CreateProcessor();
        var command = CreateMockCommand();

        // First call: prompts for password
        await processor.ProcessAsync(
            CreateTextUpdate(200, "hello"),
            command.Object,
            CancellationToken.None);

        // Second call: send correct password
        await processor.ProcessAsync(
            CreateTextUpdate(200, "secret"),
            command.Object,
            CancellationToken.None);

        // Third call: should now execute command
        var update = CreateTextUpdate(200);
        await processor.ProcessAsync(update, command.Object, CancellationToken.None);

        command.Verify(x => x.ProcessCommandAsync(
            _botClient.Object,
            update,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WrongPassword_RejectsAuth()
    {
        var processor = CreateProcessor();
        var command = CreateMockCommand();

        // First call: prompts for password
        await processor.ProcessAsync(
            CreateTextUpdate(300, "hello"),
            command.Object,
            CancellationToken.None);

        // Second call: wrong password
        await processor.ProcessAsync(
            CreateTextUpdate(300, "wrong-password"),
            command.Object,
            CancellationToken.None);

        // Third call: still not authenticated
        await processor.ProcessAsync(
            CreateTextUpdate(300),
            command.Object,
            CancellationToken.None);

        command.Verify(x => x.ProcessCommandAsync(
            It.IsAny<ITelegramBotClient>(),
            It.IsAny<Update>(),
            It.IsAny<ITelegramBotCommandState?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_UnsupportedMessageType_SendsNotification()
    {
        var processor = CreateProcessor();
        var command = CreateMockCommand();
        // Command supports only Text messages, send a Photo type
        var update = new Update
        {
            Message = new Message
            {
                Chat = new Chat { Id = 100 },
                Photo = new[] { new PhotoSize { FileId = "x", FileUniqueId = "x", Width = 100, Height = 100 } }
            }
        };

        await processor.ProcessAsync(update, command.Object, CancellationToken.None);

        command.Verify(x => x.ProcessCommandAsync(
            It.IsAny<ITelegramBotClient>(),
            It.IsAny<Update>(),
            It.IsAny<ITelegramBotCommandState?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithState_UpdatesStateCache()
    {
        var processor = CreateProcessor();
        var command = CreateMockCommand();
        command.Setup(x => x.ProcessCommandAsync(
                It.IsAny<ITelegramBotClient>(),
                It.IsAny<Update>(),
                It.IsAny<ITelegramBotCommandState?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TelegramBotCommandProcessingResult.WithSimpleState());

        var update = CreateTextUpdate(100);
        await processor.ProcessAsync(update, command.Object, CancellationToken.None);

        var cached = _stateCache.GetEntry(100);
        Assert.NotNull(cached);
        Assert.Same(command.Object, cached!.CommandHandler);
    }

    [Fact]
    public async Task ProcessAsync_WithoutState_RemovesFromCache()
    {
        var processor = CreateProcessor();
        var command = CreateMockCommand();

        // First: set state
        command.Setup(x => x.ProcessCommandAsync(
                It.IsAny<ITelegramBotClient>(),
                It.IsAny<Update>(),
                It.IsAny<ITelegramBotCommandState?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TelegramBotCommandProcessingResult.WithSimpleState());
        await processor.ProcessAsync(CreateTextUpdate(100), command.Object, CancellationToken.None);

        // Second: return no state
        command.Setup(x => x.ProcessCommandAsync(
                It.IsAny<ITelegramBotClient>(),
                It.IsAny<Update>(),
                It.IsAny<ITelegramBotCommandState?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TelegramBotCommandProcessingResult.WithoutState());
        await processor.ProcessAsync(CreateTextUpdate(100), command.Object, CancellationToken.None);

        Assert.Null(_stateCache.GetEntry(100));
    }

    [Fact]
    public async Task ProcessAsync_UnauthenticatedUpdateWithoutMessage_StillPromptsAndRejects()
    {
        var processor = CreateProcessor();
        var command = CreateMockCommand();

        // An update carrying no Message at all (e.g. a callback query from an unknown chat).
        // The second one used to dereference update.Message and fail before replying.
        Update CreateCallbackUpdate() => new()
        {
            CallbackQuery = new CallbackQuery
            {
                Id = "1",
                ChatInstance = "test",
                Message = new Message { Chat = new Chat { Id = 400 } }
            }
        };

        await processor.ProcessAsync(CreateCallbackUpdate(), command.Object, CancellationToken.None);
        await processor.ProcessAsync(CreateCallbackUpdate(), command.Object, CancellationToken.None);

        // First call prompts for the password, second one answers "incorrect password".
        _botClient.Verify(x => x.SendRequest(
            It.IsAny<SendMessageRequest>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));

        command.Verify(x => x.ProcessCommandAsync(
            It.IsAny<ITelegramBotClient>(),
            It.IsAny<Update>(),
            It.IsAny<ITelegramBotCommandState?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_RichMessagePassword_AuthenticatesChat()
    {
        var processor = CreateProcessor();
        var command = CreateMockCommand();

        await processor.ProcessAsync(CreateTextUpdate(500, "hi"), command.Object, CancellationToken.None);

        // The password arrives as a rich formatted message, which carries no Message.Text.
        var richPassword = new Update
        {
            Message = new Message
            {
                Chat = new Chat { Id = 500 },
                RichMessage = new RichMessage
                {
                    Blocks = [new RichBlockParagraph { Text = new RichTextText { Text = "secret" } }]
                }
            }
        };
        await processor.ProcessAsync(richPassword, command.Object, CancellationToken.None);

        var update = CreateTextUpdate(500);
        await processor.ProcessAsync(update, command.Object, CancellationToken.None);

        command.Verify(x => x.ProcessCommandAsync(
            _botClient.Object,
            update,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_CommandThrows_DoesNotPropagateException()
    {
        var processor = CreateProcessor();
        var command = CreateMockCommand();
        command.Setup(x => x.ProcessCommandAsync(
                It.IsAny<ITelegramBotClient>(),
                It.IsAny<Update>(),
                It.IsAny<ITelegramBotCommandState?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Command failed"));

        var update = CreateTextUpdate(100);

        // Should not throw — error is caught internally
        await processor.ProcessAsync(update, command.Object, CancellationToken.None);
    }
}
