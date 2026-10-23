using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Curiosus.TelegramBot;

/// <summary>
/// Builds and parses inline keyboard callback data in the <c>prefix</c> or <c>prefix:payload</c> form,
/// where the prefix selects the handler (<see cref="ITelegramBotCommandHandler.CallbackDataPrefix"/>).
/// </summary>
public static class CallbackData
{
    /// <summary>
    /// Maximum length of callback data accepted by Telegram, in bytes of UTF-8.
    /// </summary>
    public const int MaxLength = 64;

    /// <summary>
    /// Separator between the prefix and the payload.
    /// </summary>
    public const char Separator = ':';

    /// <summary>
    /// Creates callback data for a button handled by the handler with the given prefix.
    /// </summary>
    /// <param name="prefix">Handler prefix: non-empty, without <see cref="Separator"/>.</param>
    /// <param name="payload">Optional payload; may contain <see cref="Separator"/>.</param>
    /// <returns><c>prefix</c> or <c>prefix:payload</c>.</returns>
    /// <exception cref="ArgumentException">
    /// The prefix is invalid, or the result is longer than <see cref="MaxLength"/> bytes: Telegram would reject the message.
    /// </exception>
    public static string Create(string prefix, string? payload = null)
    {
        if (!TryValidatePrefix(prefix, out var error))
            throw new ArgumentException(error, nameof(prefix));

        var data = payload == null ? prefix : prefix + Separator + payload;

        var length = Encoding.UTF8.GetByteCount(data);
        if (length > MaxLength)
        {
            throw new ArgumentException(
                $"Callback data \"{data}\" is {length} bytes in UTF-8, Telegram accepts at most {MaxLength}.",
                nameof(payload));
        }

        return data;
    }

    /// <summary>
    /// Splits callback data into the prefix and the payload at the first <see cref="Separator"/>.
    /// </summary>
    /// <param name="data">Callback data, e.g. <see cref="Telegram.Bot.Types.CallbackQuery.Data"/>.</param>
    /// <param name="prefix">The prefix, when the method returns true.</param>
    /// <param name="payload">The payload, empty when the data has no separator.</param>
    /// <returns>True when the data starts with a non-empty prefix.</returns>
    public static bool TryParse(string? data, out string prefix, out string payload)
    {
        prefix = String.Empty;
        payload = String.Empty;

        if (String.IsNullOrEmpty(data))
            return false;

        var separatorIndex = data.IndexOf(Separator);
        if (separatorIndex == 0)
            return false;

        if (separatorIndex < 0)
        {
            prefix = data;
            return true;
        }

        prefix = data[..separatorIndex];
        payload = data[(separatorIndex + 1)..];
        return true;
    }

    /// <summary>
    /// Checks a handler prefix; the caller decides which exception to throw with <paramref name="error"/>.
    /// </summary>
    internal static bool TryValidatePrefix(string? prefix, [NotNullWhen(false)] out string? error)
    {
        if (String.IsNullOrEmpty(prefix))
        {
            error = "Callback data prefix must be non-empty.";
            return false;
        }

        if (prefix.Contains(Separator))
        {
            error = $"Callback data prefix \"{prefix}\" must not contain '{Separator}'.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Checks that the data is the prefix itself or the prefix followed by <see cref="Separator"/>,
    /// so <c>return:1</c> does not match <c>ret</c>.
    /// </summary>
    internal static bool Matches(string data, string prefix)
    {
        if (!data.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        return data.Length == prefix.Length || data[prefix.Length] == Separator;
    }
}
