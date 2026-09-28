using Markeli.TelegramBot.Commands.States;

namespace Markeli.TelegramBot;

/// <summary>
/// Result of command processing.
/// </summary>
public record TelegramBotCommandProcessingResult
{
    /// <summary>
    /// Command state for multistep commands.
    /// Used to maintain state between command execution steps.
    /// </summary>
    public ITelegramBotCommandState? State { get; init; }

    /// <summary>
    /// Creates a new instance of a processing result without any state.
    /// Used for single-step commands that don't need to maintain state.
    /// </summary>
    /// <returns>New instance of <see cref="TelegramBotCommandProcessingResult"/> without state.</returns>
    public static TelegramBotCommandProcessingResult WithoutState() => new();

    /// <summary>
    /// Creates a new instance of a processing result with a simple command state.
    /// Used for commands that need basic state management.
    /// </summary>
    /// <returns>New instance of <see cref="TelegramBotCommandProcessingResult"/> with simple state.</returns>
    public static TelegramBotCommandProcessingResult WithSimpleState() => new()
    {
        State = new SimpleCommandState()
    };
}
