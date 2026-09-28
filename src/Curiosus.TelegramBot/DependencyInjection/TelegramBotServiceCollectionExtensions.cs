using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot;

namespace Curiosus.TelegramBot;

/// <summary>
/// Extension methods for registering Telegram bot infrastructure services.
/// </summary>
public static class TelegramBotServiceCollectionExtensions
{
    /// <summary>
    /// Registers Telegram bot infrastructure services, including queue, cache, processor, dispatcher,
    /// and bot client. Optionally includes a default help command. Command handlers should be
    /// registered separately using <see cref="AddTelegramBotCommandHandler{T}"/>.
    /// </summary>
    /// <param name="services">The service collection where the services will be registered.</param>
    /// <param name="options">Configuration options for the Telegram bot.</param>
    /// <param name="addDefaultHelpCommand">Determines whether the default help command should be registered.</param>
    /// <returns>The modified service collection for chaining additional calls.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <see cref="TelegramBotOptions.ApiToken"/> is not a well-formed bot token.
    /// </exception>
    public static IServiceCollection AddTelegramBotInfrastructure(
        this IServiceCollection services,
        TelegramBotOptions options,
        bool addDefaultHelpCommand = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        options.AssertValid();

        // Built here rather than inside the factory below so that a malformed API token is rejected at
        // registration time. The token format check lives in TelegramBotClientOptions, so it is not
        // duplicated in TelegramBotOptions.Validate().
        var botClientOptions = new TelegramBotClientOptions(options.ApiToken);

        services.TryAddSingleton(options);

        services.AddMemoryCache();
        services.AddSingleton<ITelegramBotClient>(_ =>
        {
            if (options.HttpProxy is null)
                return new TelegramBotClient(botClientOptions);

            var proxy = new WebProxy(options.HttpProxy.Url);
            if (options.HttpProxy.Username is not null)
                proxy.Credentials = new NetworkCredential(options.HttpProxy.Username, options.HttpProxy.Password);

            var handler = new HttpClientHandler { Proxy = proxy, UseProxy = true };
            var httpClient = new HttpClient(handler);
            return new TelegramBotClient(botClientOptions, httpClient);
        });
        services.AddSingleton<TelegramUpdateQueue>();
        services.AddSingleton<TelegramBotCommandStateCache>();
        services.AddSingleton<TelegramUpdateProcessor>();
        services.AddHostedService<TelegramBotUpdateDispatcher>();

        if (addDefaultHelpCommand)
        {
            services.AddHelpCommand();
        }

        return services;
    }

    /// <summary>
    /// Registers the built-in <c>/help</c> command handler that lists all registered commands.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    private static IServiceCollection AddHelpCommand(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ITelegramBotCommandHandler, HelpCommandHandler>();

        return services;
    }

    /// <summary>
    /// Registers a Telegram bot command handler.
    /// </summary>
    /// <typeparam name="T">The command handler type implementing <see cref="ITelegramBotCommandHandler"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTelegramBotCommandHandler<T>(this IServiceCollection services)
        where T : class, ITelegramBotCommandHandler
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ITelegramBotCommandHandler, T>();

        return services;
    }
}
