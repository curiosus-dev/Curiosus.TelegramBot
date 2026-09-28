using Telegram.Bot.Types.Enums;

namespace Curiosus.TelegramBot;

/// <summary>
/// Ready-made <see cref="MessageType"/> sets for
/// <see cref="ITelegramBotCommandHandler.SupportedMessageTypes"/>.
/// </summary>
public static class TelegramBotMessageTypes
{
    /// <summary>
    /// Plain text and rich formatted messages — the set most text-driven command handlers want.
    /// </summary>
    /// <remarks>
    /// A handler declaring only <see cref="MessageType.Text"/> rejects rich formatted messages
    /// (Bot API 10.1), because they arrive as <see cref="MessageType.RichMessage"/>. Use this set
    /// to accept both; <see cref="TelegramBotExtensions.GetMessageText"/> reads either of them.
    /// </remarks>
    public static IReadOnlySet<MessageType> TextOrRich { get; } =
        new HashSet<MessageType> { MessageType.Text, MessageType.RichMessage };
}
