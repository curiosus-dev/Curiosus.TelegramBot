using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Extensions;
using Telegram.Bot.Types;

namespace Markeli.TelegramBot;

/// <summary>
/// Extension methods for Telegram bot operations.
/// </summary>
public static class TelegramBotExtensions
{
    /// <summary>
    /// Deletes multiple messages from a chat, logging each operation.
    /// </summary>
    public static async Task DeleteMessagesAsync(
        this ITelegramBotClient telegramBotClient,
        ChatId chatId,
        IReadOnlyList<int> messageIds,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        foreach (var messageId in messageIds)
        {
            try
            {
                logger.LogDebug(
                    "Deleting message with MessageId={MessageId} in chat ChatId={ChatId}...",
                    messageId,
                    chatId);
                await telegramBotClient.DeleteMessage(
                    chatId,
                    messageId,
                    cancellationToken);
                logger.LogInformation(
                    "Deleted message with MessageId={MessageId} in chat ChatId={ChatId}",
                    messageId,
                    chatId);
            }
            catch (Exception e)
            {
                logger.LogError(
                    e,
                    "Error while deleting message with MessageId={MessageId} in chat ChatId={ChatId}",
                    messageId,
                    chatId);
            }
        }
    }

    /// <summary>
    /// Extracts the chat ID from a Telegram update.
    /// </summary>
    public static long GetChatId(this Update update)
    {
        ArgumentNullException.ThrowIfNull(update);

        return update.Message?.Chat.Id ?? update.CallbackQuery?.Message?.Chat.Id ?? -1;
    }

    /// <summary>
    /// Extracts the textual content of the update's message, in order of preference:
    /// plain text, rich message content flattened to plain text, media caption.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rich formatted message (Bot API 10.1) carries its content in
    /// <see cref="Message.RichMessage"/> and leaves <see cref="Message.Text"/> unset, so command
    /// matching that reads <see cref="Message.Text"/> directly never sees such a message.
    /// </para>
    /// <para>
    /// When flattening a rich message, blocks are joined with newlines and inline formatting is
    /// dropped — only the text survives. Use <see cref="GetRichBlocks"/> to inspect the structure.
    /// </para>
    /// </remarks>
    /// <param name="update">The update to read the message text from.</param>
    /// <returns>The message text, or <see langword="null"/> when the message carries no text.</returns>
    public static string? GetMessageText(this Update update)
    {
        ArgumentNullException.ThrowIfNull(update);

        var message = update.Message;
        if (message == null)
            return null;

        if (message.Text != null)
            return message.Text;

        var richText = FlattenRichMessage(message.RichMessage);
        if (!String.IsNullOrWhiteSpace(richText))
            return richText;

        return message.Caption;
    }

    /// <summary>
    /// Gets the structured blocks of a rich formatted message (Bot API 10.1).
    /// </summary>
    /// <param name="update">The update to read the rich blocks from.</param>
    /// <returns>
    /// The rich message blocks, or <see langword="null"/> when the update's message is not a
    /// rich formatted message.
    /// </returns>
    public static IReadOnlyList<RichBlock>? GetRichBlocks(this Update update)
    {
        ArgumentNullException.ThrowIfNull(update);

        return update.Message?.RichMessage?.Blocks;
    }

    /// <summary>
    /// Flattens a rich formatted message into plain text by rendering it to HTML and
    /// stripping the tags. The trailing newline left by the last block is trimmed.
    /// </summary>
    private static string? FlattenRichMessage(RichMessage? richMessage)
    {
        if (richMessage?.Blocks == null)
            return null;

        var html = richMessage.ToHtml();

        return html == null ? null : HtmlText.ToPlain(html)?.Trim();
    }
}
