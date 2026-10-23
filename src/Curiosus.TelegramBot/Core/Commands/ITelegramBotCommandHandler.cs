using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Curiosus.TelegramBot;

/// <summary>
/// Non-generic base interface for command handlers (for internal use).
/// </summary>
public interface ITelegramBotCommandHandler
{
    /// <summary>
    /// Gets the human-readable name of the command.
    /// </summary>
    string CommandName { get; }

    /// <summary>
    /// Gets the text that triggers this command (e.g. "/command_name").
    /// An empty string means the handler is not a typed command (e.g. it serves only buttons):
    /// it never matches a message and is not listed by <c>/help</c>.
    /// </summary>
    string CommandText { get; }

    /// <summary>
    /// Gets the prefix of inline keyboard buttons this handler serves: presses of buttons whose callback data is
    /// <c>prefix</c> or <c>prefix:payload</c> are routed here (build the data with <see cref="CallbackData.Create"/>).
    /// The handler must also list <see cref="UpdateType.CallbackQuery"/> in <see cref="SupportedUpdateTypes"/>.
    /// Null (the default) — the handler serves no buttons of its own.
    /// While another handler holds the chat's dialog, a press runs without state and a state it returns is discarded.
    /// </summary>
    string? CallbackDataPrefix => null;

    /// <summary>
    /// Gets the set of Telegram update types that this command can handle.
    /// </summary>
    IReadOnlySet<UpdateType> SupportedUpdateTypes { get; }

    /// <summary>
    /// Gets the set of Telegram message types that this command can process.
    /// </summary>
    IReadOnlySet<MessageType> SupportedMessageTypes { get; }

    /// <summary>
    /// Processes the Telegram bot command based on the received update.
    /// </summary>
    /// <param name="telegramBotClient">The Telegram bot client instance used to send responses.</param>
    /// <param name="telegramUpdate">The update received from Telegram containing the command and context.</param>
    /// <param name="commandState">Optional state for multi-step commands. Can be null for stateless commands.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A result containing the command processing status and optional new state.</returns>
    Task<TelegramBotCommandProcessingResult> ProcessCommandAsync(
        ITelegramBotClient telegramBotClient,
        Update telegramUpdate,
        ITelegramBotCommandState? commandState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries to get a lock key for the update to prevent parallel execution of conflicting commands.
    /// </summary>
    /// <param name="telegramUpdate">The update to get lock key for.</param>
    /// <param name="lockKey">
    /// When this method returns true, contains the lock key string for exclusive execution;
    /// otherwise, null.
    /// </param>
    /// <returns>
    /// True if this command requires exclusive execution for the given update;
    /// false if the command can run in parallel without restrictions.
    /// </returns>
    bool TryGetLockKey(Update telegramUpdate, out string? lockKey)
    {
        lockKey = null;
        return false;
    }
}
