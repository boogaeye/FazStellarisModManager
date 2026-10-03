using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Session;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Tests;

public class FramingTests
{
    static async Task<SessionMessage?> RoundTrip(SessionMessage m)
    {
        var ms = new MemoryStream();
        await Framing.WriteAsync(ms, m);
        ms.Position = 0;
        return await Framing.ReadAsync(ms);
    }

    static byte[] FrameOf(byte[] body)
    {
        var frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, body.Length);
        body.CopyTo(frame, 4);
        return frame;
    }

    static byte[] Gzip(string text)
    {
        var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true)) gz.Write(Encoding.UTF8.GetBytes(text));
        return ms.ToArray();
    }

    [Fact]
    public async Task Round_trips_messages()
    {
        Assert.Equal(new Hello(1, "1.0", "Faz"), await RoundTrip(new Hello(1, "1.0", "Faz")));
        var roster = (Roster)(await RoundTrip(new Roster([new PlayerInfo("a", "Alice", false, PlayerStatus.Mismatch, new DiffSummary(true, 0, 0, 2, 0, 1, 0), null)])))!;
        var p = Assert.Single(roster.Players);
        Assert.Equal(("Alice", PlayerStatus.Mismatch, 2), (p.Name, p.Status, p.Summary!.Missing));
        Assert.IsType<Bye>(await RoundTrip(new Bye(null)));
    }

    [Fact]
    public async Task Round_trips_a_large_snapshot()
    {
        var files = Enumerable.Range(0, 50_000).Select(i => new ModFile($"common/file_{i}.txt", i.ToString("x32"), i)).ToList();
        var snap = new MachineSnapshot("PC", "v4.4", "C:/game", DateTime.UtcNow,
            new ModSnapshot("base", "Stellaris", "", null, null, null, "", 0, files), [], [], ["w"]);
        var list = new ModList("L", [new ModListEntry("ugc:1", "A", "mod/ugc_1.mod", "1")], []);

        var back = (Welcome)(await RoundTrip(new Welcome("id", list, snap)))!;

        Assert.Equal(50_000, back.HostSnapshot.Base.Files.Count);
        Assert.Equal(files[123], back.HostSnapshot.Base.Files[123]);
        Assert.Equal("ugc:1", back.HostList.Mods.Single().Key);
        Assert.Equal(new[] { "w" }, back.HostSnapshot.Warnings);
    }

    [Fact]
    public async Task Header_is_little_endian_length_of_a_gzip_body()
    {
        var ms = new MemoryStream();
        await Framing.WriteAsync(ms, new Bye("x"));
        var bytes = ms.ToArray();

        Assert.Equal(bytes.Length - 4, BinaryPrimitives.ReadInt32LittleEndian(bytes));
        Assert.Equal(new byte[] { 0x1f, 0x8b }, bytes[4..6]);
    }

    [Fact]
    public async Task Clean_end_of_stream_reads_as_null() =>
        Assert.Null(await Framing.ReadAsync(new MemoryStream()));

    [Fact]
    public async Task Truncated_frames_throw_end_of_stream()
    {
        var ms = new MemoryStream();
        await Framing.WriteAsync(ms, new Bye("x"));
        var bytes = ms.ToArray();

        await Assert.ThrowsAsync<EndOfStreamException>(() => Framing.ReadAsync(new MemoryStream(bytes[..^3])));
        await Assert.ThrowsAsync<EndOfStreamException>(() => Framing.ReadAsync(new MemoryStream(bytes[..2])));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(Framing.MaxFrameBytes + 1)]
    public async Task Bad_lengths_throw_invalid_data(int length)
    {
        var header = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);

        await Assert.ThrowsAsync<InvalidDataException>(() => Framing.ReadAsync(new MemoryStream(header)));
    }

    [Fact]
    public async Task Garbage_body_throws_invalid_data() =>
        await Assert.ThrowsAsync<InvalidDataException>(() => Framing.ReadAsync(new MemoryStream(FrameOf([1, 2, 3]))));

    [Fact]
    public async Task Unknown_message_type_throws_invalid_data() =>
        await Assert.ThrowsAsync<InvalidDataException>(() => Framing.ReadAsync(new MemoryStream(FrameOf(Gzip("{\"type\":\"nope\"}")))));

    [Fact]
    public async Task Malformed_json_throws_invalid_data() =>
        await Assert.ThrowsAsync<InvalidDataException>(() => Framing.ReadAsync(new MemoryStream(FrameOf(Gzip("{not json")))));

    [Fact]
    public async Task Frames_above_the_per_call_cap_throw_invalid_data()
    {
        var ms = new MemoryStream();
        await Framing.WriteAsync(ms, new Bye("x"));
        ms.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() => Framing.ReadAsync(ms, maxFrameBytes: 4));
    }

    [Theory]
    [InlineData("{\"type\":\"hello\"}")]
    [InlineData("{\"type\":\"welcome\"}")]
    public async Task Incomplete_messages_throw_invalid_data(string json) =>
        await Assert.ThrowsAsync<InvalidDataException>(() => Framing.ReadAsync(new MemoryStream(FrameOf(Gzip(json)))));
}
