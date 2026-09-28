namespace Markeli.TelegramBot;

/// <summary>
/// Options for Telegram bot.
/// </summary>
public class TelegramBotOptions
{
    /// <summary>
    /// API token for Telegram bot.
    /// </summary>
    public string ApiToken { get; init; } = null!;

    /// <summary>
    /// Simple password for authentication chats.
    /// </summary>
    public string Password { get; init; } = null!;

    /// <summary>
    /// Id of allowed chats to receive commands.
    /// </summary>
    public long[] AllowedChatIds { get; init; } = Array.Empty<long>();

    /// <summary>
    /// Path to file where pending Telegram updates will be persisted on shutdown.
    /// If not set, pending updates will be lost on restart.
    /// </summary>
    public string? QueuePersistenceFilePath { get; init; }

    /// <summary>
    /// Maximum number of updates processed concurrently.
    /// Default is 10.
    /// </summary>
    public int MaxDegreeOfParallelism { get; init; } = 10;

    /// <summary>
    /// HTTP proxy settings. When set, all Telegram Bot API traffic is routed through this proxy.
    /// </summary>
    public HttpProxyOptions? HttpProxy { get; init; }

    /// <summary>
    /// Validates the options and returns a list of validation errors.
    /// </summary>
    /// <returns>A list of validation error messages. Empty if valid.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (String.IsNullOrWhiteSpace(ApiToken))
            errors.Add($"{nameof(ApiToken)} can't be empty");
        if (String.IsNullOrWhiteSpace(Password))
            errors.Add($"{nameof(Password)} can't be empty");
        if (MaxDegreeOfParallelism <= 0)
            errors.Add($"{nameof(MaxDegreeOfParallelism)} must be greater than 0");

        if (HttpProxy is not null)
        {
            foreach (var error in HttpProxy.Validate())
            {
                errors.Add($"{nameof(HttpProxy)}.{error}");
            }
        }

        return errors;
    }

    /// <summary>
    /// Validates the options and throws if invalid.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    public void AssertValid()
    {
        var errors = Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"TelegramBotOptions validation failed: {String.Join("; ", errors)}");
    }
}
