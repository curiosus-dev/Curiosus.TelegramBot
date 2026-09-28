using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Memory;

namespace Curiosus.TelegramBot;

/// <summary>
/// Manages command state caching for Telegram bot conversations.
/// </summary>
public class TelegramBotCommandStateCache
{
    private static readonly TimeSpan DefaultCacheExpiration = TimeSpan.FromHours(1);

    private readonly IMemoryCache _memoryCache;

    /// <inheritdoc cref="TelegramBotCommandStateCache"/>
    public TelegramBotCommandStateCache(IMemoryCache memoryCache)
    {
        ArgumentNullException.ThrowIfNull(memoryCache);
        _memoryCache = memoryCache;
    }

    /// <summary>
    /// Gets the cached command state entry for a chat.
    /// </summary>
    /// <param name="chatId">The chat identifier.</param>
    /// <returns>The cached entry or null if not found.</returns>
    public TelegramCommandStateCacheEntry? GetEntry(long chatId)
    {
        var cacheKey = BuildChatStateCacheKey(chatId);
        return _memoryCache.Get<TelegramCommandStateCacheEntry>(cacheKey);
    }

    /// <summary>
    /// Sets the command state for a chat.
    /// </summary>
    /// <param name="chatId">The chat identifier.</param>
    /// <param name="command">The command handler.</param>
    /// <param name="state">The command state. If null, the entry will be removed.</param>
    public void SetEntry(long chatId, ITelegramBotCommandHandler command, ITelegramBotCommandState? state)
    {
        var cacheKey = BuildChatStateCacheKey(chatId);

        if (state != null)
        {
            _memoryCache.Set(
                cacheKey,
                new TelegramCommandStateCacheEntry(command, state),
                DefaultCacheExpiration);
        }
        else
        {
            _memoryCache.Remove(cacheKey);
        }
    }

    /// <summary>
    /// Removes the command state entry for a chat.
    /// </summary>
    /// <param name="chatId">The chat identifier.</param>
    public void RemoveEntry(long chatId)
    {
        var cacheKey = BuildChatStateCacheKey(chatId);
        _memoryCache.Remove(cacheKey);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string BuildChatStateCacheKey(long chatId) => $"chat_state_{chatId}";
}
