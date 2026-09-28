using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Markeli.TelegramBot;

/// <summary>
/// Built-in command handler that lists all registered bot commands.
/// </summary>
public sealed class HelpCommandHandler : ITelegramBotCommandHandler
{
    private readonly IServiceProvider _serviceProvider;

    /// <param name="serviceProvider">Service provider used to resolve command handlers at runtime.</param>
    public HelpCommandHandler(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    public string CommandName => "Help";

    /// <inheritdoc />
    public string CommandText => "/help";

    /// <inheritdoc />
    public IReadOnlySet<UpdateType> SupportedUpdateTypes { get; } =
        new HashSet<UpdateType> { UpdateType.Message };

    /// <inheritdoc />
    public IReadOnlySet<MessageType> SupportedMessageTypes { get; } = TelegramBotMessageTypes.TextOrRich;

    /// <inheritdoc />
    public async Task<TelegramBotCommandProcessingResult> ProcessCommandAsync(
        ITelegramBotClient telegramBotClient,
        Update telegramUpdate,
        ITelegramBotCommandState? commandState,
        CancellationToken cancellationToken = default)
    {
        var chatId = telegramUpdate.Message!.Chat.Id;
        var message = BuildHelpMessage();

        await telegramBotClient.SendMessage(
            chatId, message, cancellationToken: cancellationToken);

        return TelegramBotCommandProcessingResult.WithoutState();
    }

    internal string BuildHelpMessage()
    {
        var commandHandlers = _serviceProvider.GetServices<ITelegramBotCommandHandler>();
        var commands = commandHandlers
            .Where(h => h != this)
            .OrderBy(h => h.CommandText, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (commands.Count == 0)
            return "No commands available.";

        var sb = new StringBuilder("Available commands:\n");
        foreach (var command in commands)
        {
            sb.AppendLine($"{command.CommandText} — {command.CommandName}");
        }

        return sb.ToString().TrimEnd();
    }
}
