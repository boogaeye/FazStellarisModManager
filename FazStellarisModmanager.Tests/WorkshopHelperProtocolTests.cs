using FazStellarisModmanager.Core.Workshop;

namespace FazStellarisModmanager.Tests;

public class WorkshopHelperProtocolTests
{
    static readonly string NewLine = ((char)10).ToString();

    [Fact]
    public void Progress_round_trips()
    {
        var sent = new HelperProgress(new WorkshopProgress(1419304439, WorkshopItemState.Downloading, 0.25, "half way"));

        var back = Assert.IsType<HelperProgress>(WorkshopHelperProtocol.Parse(WorkshopHelperProtocol.Write(sent)));

        Assert.Equal(sent.Progress, back.Progress);
    }

    [Fact]
    public void Progress_without_a_message_round_trips()
    {
        var sent = new HelperProgress(new WorkshopProgress(7, WorkshopItemState.Waiting, 0));

        var back = Assert.IsType<HelperProgress>(WorkshopHelperProtocol.Parse(WorkshopHelperProtocol.Write(sent)));

        Assert.Equal(sent.Progress, back.Progress);
    }

    [Fact]
    public void Results_round_trip_including_non_ascii_paths_and_the_largest_id()
    {
        WorkshopItemResult[] items =
        [
            new(ulong.MaxValue, true, @"C:\Users\Zoë\Steam\steamapps\workshop\content\281990\1", null),
            new(42, false, null, "The download stalled."),
        ];

        var back = Assert.IsType<HelperResults>(WorkshopHelperProtocol.Parse(WorkshopHelperProtocol.Write(new HelperResults(items))));

        Assert.Equal(items, back.Items);
    }

    [Fact]
    public void Unavailable_round_trips()
    {
        var back = Assert.IsType<HelperUnavailable>(WorkshopHelperProtocol.Parse(WorkshopHelperProtocol.Write(new HelperUnavailable("Steam is not running."))));

        Assert.Equal("Steam is not running.", back.Message);
    }

    [Fact]
    public void A_message_is_one_ascii_line_even_with_line_breaks_and_non_ascii_text()
    {
        var line = WorkshopHelperProtocol.Write(new HelperUnavailable("first" + NewLine + "second Zoë"));

        Assert.DoesNotContain((char)10, line);
        Assert.DoesNotContain((char)13, line);
        Assert.All(line, c => Assert.True(c < 128, $"non-ASCII char {(int)c}"));
        Assert.Equal("first" + NewLine + "second Zoë", Assert.IsType<HelperUnavailable>(WorkshopHelperProtocol.Parse(line)).Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[S_API] SteamAPI_Init(): Loaded local 'steamclient64.dll' OK.")]
    [InlineData("Setting breakpad minidump AppID = 281990")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("[]")]
    [InlineData("""{"type":"nope"}""")]
    [InlineData("""{"items":[]}""")]
    [InlineData("""{"type":"progress"}""")]
    [InlineData("""{"type":"progress","progress":null}""")]
    [InlineData("""{"type":"progress","progress":{"id":1,"state":"Exploding","fraction":0}}""")]
    [InlineData("""{"type":"progress","progress":{"id":-1,"state":"Waiting","fraction":0}}""")]
    [InlineData("""{"type":"results"}""")]
    [InlineData("""{"type":"results","items":[null]}""")]
    [InlineData("""{"type":"results","items":"x"}""")]
    [InlineData("""{"type":"unavailable"}""")]
    [InlineData("""{"type":"unavailable","message":null}""")]
    public void Malformed_or_foreign_lines_are_ignored(string? line)
    {
        Assert.Null(WorkshopHelperProtocol.Parse(line));
    }

    [Fact]
    public void A_message_after_unterminated_native_output_on_the_same_line_is_still_read()
    {
        var line = "[S_API] SteamAPI_Init(): done" + WorkshopHelperProtocol.Write(new HelperUnavailable("x"));

        Assert.Equal("x", Assert.IsType<HelperUnavailable>(WorkshopHelperProtocol.Parse(line)).Message);
    }

    [Fact]
    public void The_message_type_may_come_after_the_payload()
    {
        var parsed = WorkshopHelperProtocol.Parse("""{"message":"late","type":"unavailable"}""");

        Assert.Equal("late", Assert.IsType<HelperUnavailable>(parsed).Message);
    }

    [Fact]
    public void Request_round_trips()
    {
        var line = WorkshopHelperProtocol.WriteRequest([1419304439, ulong.MaxValue]);

        Assert.DoesNotContain((char)10, line);
        Assert.Equal([1419304439UL, ulong.MaxValue], WorkshopHelperProtocol.ParseRequest(line));
    }

    [Fact]
    public void An_empty_request_round_trips()
    {
        Assert.Equal([], WorkshopHelperProtocol.ParseRequest(WorkshopHelperProtocol.WriteRequest([]))!);
    }

    [Theory]
    [InlineData(0xFEFF)] // a byte order mark read as UTF-16
    [InlineData(0xEF)] // ...or as the first of its bytes in an ANSI code page
    public void A_request_after_a_byte_order_mark_is_read(int junk)
    {
        Assert.Equal([5UL], WorkshopHelperProtocol.ParseRequest((char)junk + WorkshopHelperProtocol.WriteRequest([5]))!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1,2,3")]
    [InlineData("{}")]
    [InlineData("""{"ids":null}""")]
    [InlineData("""{"ids":[-1]}""")]
    [InlineData("""{"ids":["abc"]}""")]
    [InlineData("""{"ids":[1.5]}""")]
    public void Malformed_requests_are_rejected(string? line)
    {
        Assert.Null(WorkshopHelperProtocol.ParseRequest(line));
    }
}
