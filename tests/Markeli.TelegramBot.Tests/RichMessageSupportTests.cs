using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Xunit;

namespace Markeli.TelegramBot.Tests;

public class RichMessageSupportTests
{
    private static Update CreateRichUpdate(long chatId, params RichBlock[] blocks)
    {
        return new Update
        {
            Message = new Message
            {
                Chat = new Chat { Id = chatId },
                RichMessage = new RichMessage { Blocks = blocks }
            }
        };
    }

    private static RichBlockParagraph Paragraph(string text) =>
        new() { Text = new RichTextText { Text = text } };

    [Fact]
    public void RichMessage_HasRichMessageType_NotTextType()
    {
        var update = CreateRichUpdate(1, Paragraph("hello"));

        // The whole reason handlers must opt in: a rich message is not MessageType.Text.
        Assert.Equal(MessageType.RichMessage, update.Message!.Type);
        Assert.Null(update.Message.Text);
    }

    [Fact]
    public void GetMessageText_RichMessage_FlattensSingleParagraph()
    {
        var update = CreateRichUpdate(1, Paragraph("/report daily"));

        Assert.Equal("/report daily", update.GetMessageText());
    }

    [Fact]
    public void GetMessageText_RichMessage_JoinsBlocksWithNewlinesAndDropsFormatting()
    {
        var update = CreateRichUpdate(
            1,
            new RichBlockSectionHeading { Text = new RichTextText { Text = "Report" } },
            new RichBlockParagraph
            {
                Text = new RichTextArray
                {
                    Array =
                    [
                        new RichTextText { Text = "plain and " },
                        new RichTextBold { Text = new RichTextText { Text = "bold" } }
                    ]
                }
            });

        Assert.Equal("Report\nplain and bold", update.GetMessageText());
    }

    [Fact]
    public void GetMessageText_RichMessage_DecodesHtmlEntities()
    {
        var update = CreateRichUpdate(1, Paragraph("a < b & c > d"));

        Assert.Equal("a < b & c > d", update.GetMessageText());
    }

    [Fact]
    public void GetMessageText_PlainText_PrefersTextOverRichAndCaption()
    {
        var update = new Update
        {
            Message = new Message
            {
                Chat = new Chat { Id = 1 },
                Text = "plain",
                Caption = "caption"
            }
        };

        Assert.Equal("plain", update.GetMessageText());
    }

    [Fact]
    public void GetMessageText_CaptionOnly_ReturnsCaption()
    {
        var update = new Update
        {
            Message = new Message
            {
                Chat = new Chat { Id = 1 },
                Caption = "caption"
            }
        };

        Assert.Equal("caption", update.GetMessageText());
    }

    [Fact]
    public void GetMessageText_WithoutMessage_ReturnsNull()
    {
        var update = new Update
        {
            CallbackQuery = new CallbackQuery { Id = "1", ChatInstance = "test" }
        };

        Assert.Null(update.GetMessageText());
    }

    [Fact]
    public void GetMessageText_WithNullUpdate_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => TelegramBotExtensions.GetMessageText(null!));
    }

    [Fact]
    public void GetRichBlocks_RichMessage_ReturnsBlocks()
    {
        var update = CreateRichUpdate(1, Paragraph("first"), Paragraph("second"));

        var blocks = update.GetRichBlocks();

        Assert.NotNull(blocks);
        Assert.Equal(2, blocks!.Count);
        Assert.All(blocks, b => Assert.IsType<RichBlockParagraph>(b));
    }

    [Fact]
    public void GetRichBlocks_PlainTextMessage_ReturnsNull()
    {
        var update = new Update
        {
            Message = new Message { Chat = new Chat { Id = 1 }, Text = "plain" }
        };

        Assert.Null(update.GetRichBlocks());
    }

    [Fact]
    public void GetRichBlocks_WithNullUpdate_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => TelegramBotExtensions.GetRichBlocks(null!));
    }

    [Fact]
    public void TextOrRich_ContainsTextAndRichMessage()
    {
        Assert.Contains(MessageType.Text, TelegramBotMessageTypes.TextOrRich);
        Assert.Contains(MessageType.RichMessage, TelegramBotMessageTypes.TextOrRich);
        Assert.Equal(2, TelegramBotMessageTypes.TextOrRich.Count);
    }

    [Fact]
    public void HelpCommandHandler_AcceptsRichMessages()
    {
        var handler = new HelpCommandHandler(new ServiceCollectionStub());

        Assert.Contains(MessageType.RichMessage, handler.SupportedMessageTypes);
    }

    private sealed class ServiceCollectionStub : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
