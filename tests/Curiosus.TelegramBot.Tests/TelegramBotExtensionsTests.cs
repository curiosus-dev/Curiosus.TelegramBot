using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Xunit;

namespace Curiosus.TelegramBot.Tests;

public class TelegramBotExtensionsTests
{
    [Fact]
    public void GetChatId_WithMessage_ReturnsChatId()
    {
        var update = new Update
        {
            Message = new Message
            {
                Chat = new Chat { Id = 42 }
            }
        };

        var chatId = update.GetChatId();

        Assert.Equal(42, chatId);
    }

    [Fact]
    public void GetChatId_WithCallbackQuery_ReturnsChatId()
    {
        var update = new Update
        {
            CallbackQuery = new CallbackQuery
            {
                Id = "1",
                ChatInstance = "test",
                Message = new Message
                {
                    Chat = new Chat { Id = 99 }
                }
            }
        };

        var chatId = update.GetChatId();

        Assert.Equal(99, chatId);
    }

    [Fact]
    public void GetChatId_WithNoMessageOrCallback_ReturnsNegativeOne()
    {
        var update = new Update();

        var chatId = update.GetChatId();

        Assert.Equal(-1, chatId);
    }

    [Fact]
    public void GetChatId_WithNullUpdate_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => TelegramBotExtensions.GetChatId(null!));
    }

    [Fact]
    public async Task DeleteMessagesAsync_DeletesAllMessages()
    {
        var botClient = new Mock<ITelegramBotClient>();
        botClient
            .Setup(x => x.SendRequest(
                It.IsAny<DeleteMessageRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var logger = new Mock<ILogger>();
        var messageIds = new List<int> { 1, 2, 3 };

        await botClient.Object.DeleteMessagesAsync(
            new ChatId(42),
            messageIds,
            logger.Object);

        botClient.Verify(
            x => x.SendRequest(
                It.IsAny<DeleteMessageRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task DeleteMessagesAsync_WithEmptyList_DoesNothing()
    {
        var botClient = new Mock<ITelegramBotClient>();
        var logger = new Mock<ILogger>();
        var messageIds = new List<int>();

        await botClient.Object.DeleteMessagesAsync(
            new ChatId(42),
            messageIds,
            logger.Object);

        botClient.Verify(
            x => x.SendRequest(
                It.IsAny<DeleteMessageRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteMessagesAsync_WhenDeleteFails_ContinuesWithRemainingMessages()
    {
        var botClient = new Mock<ITelegramBotClient>();
        var callCount = 0;
        botClient
            .Setup(x => x.SendRequest(
                It.IsAny<DeleteMessageRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<DeleteMessageRequest, CancellationToken>((req, ct) =>
            {
                callCount++;
                if (callCount == 1)
                    throw new Exception("Telegram API error");
                return Task.FromResult(true);
            });

        var logger = new Mock<ILogger>();
        var messageIds = new List<int> { 1, 2 };

        await botClient.Object.DeleteMessagesAsync(
            new ChatId(42),
            messageIds,
            logger.Object);

        botClient.Verify(
            x => x.SendRequest(
                It.IsAny<DeleteMessageRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
