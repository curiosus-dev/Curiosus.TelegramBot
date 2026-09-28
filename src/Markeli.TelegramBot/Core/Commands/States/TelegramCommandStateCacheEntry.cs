namespace Markeli.TelegramBot;

/// <summary>
/// Container for a cached command handler and its state.
/// </summary>
public class TelegramCommandStateCacheEntry
{
    /// <summary>
    /// Gets the command handler associated with this cache entry.
    /// </summary>
    public ITelegramBotCommandHandler CommandHandler { get; }

    /// <summary>
    /// Gets the command state associated with this cache entry.
    /// </summary>
    public ITelegramBotCommandState CommandState { get; }

    /// <inheritdoc cref="TelegramCommandStateCacheEntry"/>
    public TelegramCommandStateCacheEntry(
        ITelegramBotCommandHandler commandHandler,
        ITelegramBotCommandState commandState)
    {
        CommandHandler = commandHandler;
        CommandState = commandState;
    }
}
