using Curiosus.TelegramBot.Commands.States;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Xunit;

namespace Curiosus.TelegramBot.Tests;

public class CallbackQueryProcessingTests
{
    private readonly Mock<ILogger<TelegramUpdateProcessor>> _logger = new();
    private readonly Mock<ITelegramBotClient> _botClient = new();
    private readonly TelegramBotCommandStateCache _stateCache = new(new MemoryCache(new MemoryCacheOptions()));
    private readonly TelegramBotOptions _options = new()
    {
        ApiToken = "test-token",
        Password = "secret",
        AllowedChatIds = new[] { 100L }
    };

    public CallbackQueryProcessingTests()
    {
        _botClient
            .Setup(x => x.SendRequest(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message { Chat = new Chat { Id = 1 } });
        _botClient
            .Setup(x => x.SendRequest(It.IsAny<AnswerCallbackQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private TelegramUpdateProcessor CreateProcessor() =>
        new(_logger.Object, _botClient.Object, _options, _stateCache);

    private static Mock<ITelegramBotCommandHandler> CreateHandler(
        string name = "returns",
        string? prefix = "ret",
        TelegramBotCommandProcessingResult? result = null)
    {
        var mock = new Mock<ITelegramBotCommandHandler>();
        mock.Setup(x => x.CommandName).Returns(name);
        mock.Setup(x => x.CommandText).Returns(string.Empty);
        mock.Setup(x => x.CallbackDataPrefix).Returns(prefix);
        mock.Setup(x => x.SupportedUpdateTypes)
            .Returns(new HashSet<UpdateType> { UpdateType.Message, UpdateType.CallbackQuery });
        // a callback query has no message type: SupportedMessageTypes must not be consulted
        mock.Setup(x => x.SupportedMessageTypes).Returns(new HashSet<MessageType> { MessageType.Text });
        mock.Setup(x => x.ProcessCommandAsync(
                It.IsAny<ITelegramBotClient>(),
                It.IsAny<Update>(),
                It.IsAny<ITelegramBotCommandState?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result ?? TelegramBotCommandProcessingResult.WithoutState());
        return mock;
    }

    private static Update CreateCallbackUpdate(long chatId, string data = "ret:42", bool withMessage = true) => new()
    {
        CallbackQuery = new CallbackQuery
        {
            Id = "query-1",
            Data = data,
            Message = withMessage ? new Message { Chat = new Chat { Id = chatId } } : null
        }
    };

    private void VerifyAnswered(Func<AnswerCallbackQueryRequest, bool> match, Times times) =>
        _botClient.Verify(
            x => x.SendRequest(It.Is<AnswerCallbackQueryRequest>(r => match(r)), It.IsAny<CancellationToken>()),
            times);

    private void VerifyNoMessageSent() =>
        _botClient.Verify(
            x => x.SendRequest(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);

    private static void VerifyNotExecuted(Mock<ITelegramBotCommandHandler> handler) =>
        handler.Verify(
            x => x.ProcessCommandAsync(
                It.IsAny<ITelegramBotClient>(),
                It.IsAny<Update>(),
                It.IsAny<ITelegramBotCommandState?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

    [Fact]
    public async Task ProcessAsync_CallbackInAllowedChat_ExecutesAndAnswersOnce()
    {
        var handler = CreateHandler();
        var update = CreateCallbackUpdate(100);

        await CreateProcessor().ProcessAsync(update, handler.Object, CancellationToken.None);

        handler.Verify(x => x.ProcessCommandAsync(_botClient.Object, update, null, It.IsAny<CancellationToken>()), Times.Once);
        VerifyAnswered(r => r.CallbackQueryId == "query-1" && r.Text == null && !r.ShowAlert, Times.Once());
        VerifyNoMessageSent();
    }

    [Fact]
    public async Task ProcessAsync_CallbackAnswerWithAlert_PassesTextAndShowAlert()
    {
        var handler = CreateHandler(result: new TelegramBotCommandProcessingResult
        {
            CallbackAnswer = new TelegramBotCallbackAnswer("Already returned", ShowAlert: true)
        });

        await CreateProcessor().ProcessAsync(CreateCallbackUpdate(100), handler.Object, CancellationToken.None);

        VerifyAnswered(r => r.Text == "Already returned" && r.ShowAlert, Times.Once());
    }

    [Fact]
    public async Task ProcessAsync_HandlerAlreadyAnswered_SwallowsApiRequestException()
    {
        _botClient
            .Setup(x => x.SendRequest(It.IsAny<AnswerCallbackQueryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiRequestException("Bad Request: query is too old and response timeout expired", 400));
        var handler = CreateHandler();

        await CreateProcessor().ProcessAsync(CreateCallbackUpdate(100), handler.Object, CancellationToken.None);

        VerifyAnswered(r => r.CallbackQueryId == "query-1", Times.Once());
        VerifyLogged(LogLevel.Debug, Times.AtLeastOnce());
        VerifyLogged(LogLevel.Warning, Times.Never());
        _logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    private void VerifyLogged(LogLevel level, Times times) =>
        _logger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    [Fact]
    public async Task ProcessAsync_AnswerRejectedForOtherReason_LogsWarning()
    {
        _botClient
            .Setup(x => x.SendRequest(It.IsAny<AnswerCallbackQueryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiRequestException("Bad Request: MESSAGE_TOO_LONG", 400));
        var handler = CreateHandler(result: new TelegramBotCommandProcessingResult
        {
            CallbackAnswer = new TelegramBotCallbackAnswer(new string('a', 250))
        });

        await CreateProcessor().ProcessAsync(CreateCallbackUpdate(100), handler.Object, CancellationToken.None);

        VerifyLogged(LogLevel.Warning, Times.Once());
    }

    [Fact]
    public async Task ProcessAsync_CallbackForOtherHandlerReturningState_LogsWarningAndKeepsForeignState()
    {
        var dialog = CreateHandler("dialog", prefix: null);
        var dialogState = new SimpleCommandState();
        _stateCache.SetEntry(100, dialog.Object, dialogState);
        var handler = CreateHandler(result: TelegramBotCommandProcessingResult.WithSimpleState());

        await CreateProcessor().ProcessAsync(CreateCallbackUpdate(100), handler.Object, CancellationToken.None);

        VerifyLogged(LogLevel.Warning, Times.Once());
        Assert.Same(dialogState, _stateCache.GetEntry(100)?.CommandState);
    }

    [Fact]
    public async Task ProcessAsync_HandlerThrows_StillAnswers()
    {
        var handler = CreateHandler();
        handler.Setup(x => x.ProcessCommandAsync(
                It.IsAny<ITelegramBotClient>(),
                It.IsAny<Update>(),
                It.IsAny<ITelegramBotCommandState?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await CreateProcessor().ProcessAsync(CreateCallbackUpdate(100), handler.Object, CancellationToken.None);

        VerifyAnswered(r => r.CallbackQueryId == "query-1" && r.Text == null, Times.Once());
    }

    [Fact]
    public async Task ProcessAsync_CallbackFromUnauthorizedChat_AnswersWithoutPasswordPrompt()
    {
        var processor = CreateProcessor();
        var handler = CreateHandler();

        await processor.ProcessAsync(CreateCallbackUpdate(999), handler.Object, CancellationToken.None);

        VerifyNotExecuted(handler);
        VerifyAnswered(r => r.CallbackQueryId == "query-1", Times.Once());
        VerifyNoMessageSent();

        // the press did not put the chat on the password waiting list: a text message still gets the greeting
        var text = new Update { Message = new Message { Chat = new Chat { Id = 999 }, Text = "secret" } };
        await processor.ProcessAsync(text, handler.Object, CancellationToken.None);

        _botClient.Verify(
            x => x.SendRequest(
                It.Is<SendMessageRequest>(r => r.Text.StartsWith("Hi! Your chat ID")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_CallbackWithoutMessage_AnswersAndSkipsHandler()
    {
        var handler = CreateHandler();

        await CreateProcessor().ProcessAsync(
            CreateCallbackUpdate(100, withMessage: false),
            handler.Object,
            CancellationToken.None);

        VerifyNotExecuted(handler);
        VerifyAnswered(r => r.CallbackQueryId == "query-1", Times.Once());
        VerifyNoMessageSent();
    }

    [Fact]
    public async Task ProcessAsync_CallbackForOtherHandler_KeepsForeignState()
    {
        var dialog = CreateHandler("dialog", prefix: null);
        var dialogState = new SimpleCommandState();
        _stateCache.SetEntry(100, dialog.Object, dialogState);
        var handler = CreateHandler();

        await CreateProcessor().ProcessAsync(CreateCallbackUpdate(100), handler.Object, CancellationToken.None);

        handler.Verify(
            x => x.ProcessCommandAsync(It.IsAny<ITelegramBotClient>(), It.IsAny<Update>(), null, It.IsAny<CancellationToken>()),
            Times.Once);
        var entry = _stateCache.GetEntry(100);
        Assert.NotNull(entry);
        Assert.Same(dialog.Object, entry.CommandHandler);
        Assert.Same(dialogState, entry.CommandState);
    }

    [Fact]
    public async Task ProcessAsync_CallbackForStateHolder_PassesAndUpdatesState()
    {
        var oldState = new SimpleCommandState();
        var newState = new SimpleCommandState();
        var dialog = CreateHandler("dialog", prefix: null, result: new TelegramBotCommandProcessingResult { State = newState });
        _stateCache.SetEntry(100, dialog.Object, oldState);

        await CreateProcessor().ProcessAsync(CreateCallbackUpdate(100, "yes"), dialog.Object, CancellationToken.None);

        dialog.Verify(
            x => x.ProcessCommandAsync(It.IsAny<ITelegramBotClient>(), It.IsAny<Update>(), oldState, It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Same(newState, _stateCache.GetEntry(100)?.CommandState);
    }

    [Fact]
    public async Task ProcessAsync_CallbackForStateHolderWithoutNewState_ClearsState()
    {
        var dialog = CreateHandler("dialog", prefix: null);
        _stateCache.SetEntry(100, dialog.Object, new SimpleCommandState());

        await CreateProcessor().ProcessAsync(CreateCallbackUpdate(100, "yes"), dialog.Object, CancellationToken.None);

        Assert.Null(_stateCache.GetEntry(100));
    }
}
