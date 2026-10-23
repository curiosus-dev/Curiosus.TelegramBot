using Telegram.Bot.Types;
using Xunit;

namespace Curiosus.TelegramBot.Tests;

public class CallbackDataTests
{
    [Fact]
    public void Create_PrefixOnly_ReturnsPrefix()
    {
        Assert.Equal("ret", CallbackData.Create("ret"));
    }

    [Fact]
    public void Create_WithPayload_JoinsWithColon()
    {
        Assert.Equal("ret:42:1", CallbackData.Create("ret", "42:1"));
    }

    [Fact]
    public void Create_CyrillicPayloadOver64Bytes_Throws()
    {
        // 32 Cyrillic letters are 64 bytes in UTF-8, plus "r:" — 66 bytes in 34 characters
        var payload = new string('я', 32);

        var exception = Assert.Throws<ArgumentException>(() => CallbackData.Create("r", payload));

        Assert.Contains("66", exception.Message);
    }

    [Fact]
    public void Create_Exactly64Bytes_Succeeds()
    {
        var payload = new string('a', 62);

        var data = CallbackData.Create("r", payload);

        Assert.Equal(CallbackData.MaxLength, data.Length);
    }

    [Theory]
    [InlineData("a:b")]
    [InlineData("")]
    [InlineData(null)]
    public void Create_InvalidPrefix_Throws(string? prefix)
    {
        Assert.ThrowsAny<ArgumentException>(() => CallbackData.Create(prefix!, "1"));
    }

    [Fact]
    public void TryParse_WithPayload_SplitsAtFirstColon()
    {
        var parsed = CallbackData.TryParse("ret:42:1", out var prefix, out var payload);

        Assert.True(parsed);
        Assert.Equal("ret", prefix);
        Assert.Equal("42:1", payload);
    }

    [Fact]
    public void TryParse_NoColon_EmptyPayload()
    {
        var parsed = CallbackData.TryParse("ret", out var prefix, out var payload);

        Assert.True(parsed);
        Assert.Equal("ret", prefix);
        Assert.Equal(string.Empty, payload);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(":1")]
    public void TryParse_NoPrefix_ReturnsFalse(string? data)
    {
        Assert.False(CallbackData.TryParse(data, out _, out _));
    }

    [Theory]
    [InlineData("ret", "ret", true)]
    [InlineData("ret:1", "ret", true)]
    [InlineData("return:1", "ret", false)]
    [InlineData("re", "ret", false)]
    public void Matches_ChecksWholePrefix(string data, string prefix, bool expected)
    {
        Assert.Equal(expected, CallbackData.Matches(data, prefix));
    }

    [Fact]
    public void GetCallbackPayload_CallbackUpdate_ReturnsPayload()
    {
        var update = new Update { CallbackQuery = new CallbackQuery { Id = "1", Data = "ret:42:1" } };

        Assert.Equal("ret:42:1", update.GetCallbackData());
        Assert.Equal("42:1", update.GetCallbackPayload());
    }

    [Fact]
    public void GetCallbackPayload_PrefixOnly_ReturnsEmpty()
    {
        var update = new Update { CallbackQuery = new CallbackQuery { Id = "1", Data = "ret" } };

        Assert.Equal(string.Empty, update.GetCallbackPayload());
    }

    [Fact]
    public void GetCallbackData_MessageUpdate_ReturnsNull()
    {
        var update = new Update { Message = new Message { Chat = new Chat { Id = 1 }, Text = "/start" } };

        Assert.Null(update.GetCallbackData());
        Assert.Null(update.GetCallbackPayload());
    }
}
