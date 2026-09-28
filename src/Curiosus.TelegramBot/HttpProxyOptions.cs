namespace Curiosus.TelegramBot;

/// <summary>
/// HTTP proxy configuration for the Telegram bot client.
/// </summary>
public class HttpProxyOptions
{
    /// <summary>
    /// Proxy URL (e.g. "http://proxy.example.com:8080").
    /// </summary>
    public string Url { get; init; } = null!;

    /// <summary>
    /// Proxy authentication username. Optional.
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// Proxy authentication password. Optional.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Validates the options and returns a list of validation errors.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (String.IsNullOrWhiteSpace(Url))
        {
            errors.Add($"{nameof(Url)} can't be empty");
            return errors;
        }

        if (!Uri.TryCreate(Url, UriKind.Absolute, out var proxyUri))
        {
            errors.Add($"{nameof(Url)} must be a valid absolute URI");
        }
        else if (proxyUri.Scheme is not "http" and not "https")
        {
            errors.Add($"{nameof(Url)} must use http or https scheme");
        }

        if (Password is not null && Username is null)
        {
            errors.Add($"{nameof(Username)} is required when {nameof(Password)} is set");
        }

        return errors;
    }
}
