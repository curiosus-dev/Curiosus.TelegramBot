namespace Curiosus.TelegramBot;

/// <summary>
/// How the pressed inline keyboard button is answered after the handler returns.
/// </summary>
/// <param name="Text">Notification shown to the user, up to 200 characters; null — the button just stops spinning.</param>
/// <param name="ShowAlert">Show the text as an alert with an OK button instead of a short toast.</param>
public record TelegramBotCallbackAnswer(string? Text, bool ShowAlert = false);
