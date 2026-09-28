namespace Markeli.TelegramBot;

/// <summary>
/// Base implementation for command states.
/// </summary>
public abstract class TelegramBotCommandStateBase : ITelegramBotCommandState
{
    /// <summary>
    /// Gets the UTC timestamp when this state was created.
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this state was last modified.
    /// </summary>
    public DateTime LastModifiedAt { get; set; }

    /// <inheritdoc cref="TelegramBotCommandStateBase"/>
    protected TelegramBotCommandStateBase()
    {
        CreatedAt = DateTime.UtcNow;
        LastModifiedAt = CreatedAt;
    }
}
