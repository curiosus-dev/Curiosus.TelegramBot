using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Xunit;

namespace Curiosus.TelegramBot.Tests;

public class TelegramBotServiceCollectionExtensionsTests
{
    // Telegram.Bot validates the "{botId}:{secret}" token shape when the client is constructed.
    private const string TestApiToken = "123456:test-token";

    private static TelegramBotOptions CreateValidOptions() => new()
    {
        ApiToken = TestApiToken,
        Password = "test-password"
    };

    [Fact]
    public void AddTelegramBotInfrastructure_RegistersAllRequiredServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTelegramBotInfrastructure(CreateValidOptions());

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<TelegramBotOptions>());
        Assert.NotNull(provider.GetService<IMemoryCache>());
        Assert.NotNull(provider.GetService<ITelegramBotClient>());
        Assert.NotNull(provider.GetService<TelegramUpdateQueue>());
        Assert.NotNull(provider.GetService<TelegramBotCommandStateCache>());
        Assert.NotNull(provider.GetService<TelegramUpdateProcessor>());
    }

    [Fact]
    public void AddTelegramBotInfrastructure_RegistersDispatcherAsHostedService()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTelegramBotInfrastructure(CreateValidOptions());

        var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();
        Assert.Contains(hostedServices, s => s is TelegramBotUpdateDispatcher);
    }

    [Fact]
    public void AddTelegramBotInfrastructure_WithInvalidOptions_Throws()
    {
        var services = new ServiceCollection();
        var invalidOptions = new TelegramBotOptions
        {
            ApiToken = "",
            Password = ""
        };

        Assert.Throws<InvalidOperationException>(() =>
            services.AddTelegramBotInfrastructure(invalidOptions));
    }

    [Fact]
    public void AddTelegramBotInfrastructure_WithMalformedApiToken_ThrowsAtRegistrationTime()
    {
        var services = new ServiceCollection();
        var options = new TelegramBotOptions
        {
            ApiToken = "no-bot-id-prefix",
            Password = "test-password"
        };

        Assert.Throws<ArgumentException>(() => services.AddTelegramBotInfrastructure(options));
    }

    [Fact]
    public void AddTelegramBotInfrastructure_WithNullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TelegramBotServiceCollectionExtensions.AddTelegramBotInfrastructure(null!, CreateValidOptions()));
    }

    [Fact]
    public void AddTelegramBotInfrastructure_WithNullOptions_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() =>
            services.AddTelegramBotInfrastructure(null!));
    }

    [Fact]
    public void AddTelegramBotCommandHandler_RegistersHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTelegramBotInfrastructure(CreateValidOptions());

        services.AddTelegramBotCommandHandler<TestCommandHandler>();

        var provider = services.BuildServiceProvider();
        var handlers = provider.GetServices<ITelegramBotCommandHandler>();
        Assert.Contains(handlers, h => h is TestCommandHandler);
    }

    [Fact]
    public void AddTelegramBotCommandHandler_WithNullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TelegramBotServiceCollectionExtensions.AddTelegramBotCommandHandler<TestCommandHandler>(null!));
    }

    [Fact]
    public void AddTelegramBotInfrastructure_WithHttpProxy_RegistersBotClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var options = new TelegramBotOptions
        {
            ApiToken = TestApiToken,
            Password = "test-password",
            HttpProxy = new HttpProxyOptions { Url = "http://proxy.example.com:8080" }
        };

        services.AddTelegramBotInfrastructure(options);

        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<ITelegramBotClient>());
    }

    [Fact]
    public void AddTelegramBotInfrastructure_WithHttpProxyCredentials_RegistersBotClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var options = new TelegramBotOptions
        {
            ApiToken = TestApiToken,
            Password = "test-password",
            HttpProxy = new HttpProxyOptions
            {
                Url = "http://proxy.example.com:8080",
                Username = "user",
                Password = "pass"
            }
        };

        services.AddTelegramBotInfrastructure(options);

        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<ITelegramBotClient>());
    }

    [Fact]
    public void AddTelegramBotInfrastructure_WithInvalidHttpProxy_Throws()
    {
        var services = new ServiceCollection();
        var options = new TelegramBotOptions
        {
            ApiToken = TestApiToken,
            Password = "test-password",
            HttpProxy = new HttpProxyOptions { Url = "not-a-url" }
        };

        Assert.Throws<InvalidOperationException>(() =>
            services.AddTelegramBotInfrastructure(options));
    }

    [Fact]
    public void AddTelegramBotInfrastructure_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var result = services.AddTelegramBotInfrastructure(CreateValidOptions());

        Assert.Same(services, result);
    }

    private class TestCommandHandler : ITelegramBotCommandHandler
    {
        public string CommandName => "Test";
        public string CommandText => "/test";
        public IReadOnlySet<UpdateType> SupportedUpdateTypes => new HashSet<UpdateType> { UpdateType.Message };
        public IReadOnlySet<MessageType> SupportedMessageTypes => new HashSet<MessageType> { MessageType.Text };

        public Task<TelegramBotCommandProcessingResult> ProcessCommandAsync(
            ITelegramBotClient telegramBotClient,
            Update telegramUpdate,
            ITelegramBotCommandState? commandState,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TelegramBotCommandProcessingResult.WithoutState());
        }
    }
}
