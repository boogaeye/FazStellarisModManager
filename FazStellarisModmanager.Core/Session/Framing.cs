using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FazStellarisModmanager.Core.Session;

/// <summary>Wire format: 4-byte little-endian body length, then the body (gzip-compressed UTF-8 JSON of one <see cref="SessionMessage"/>).</summary>
public static class Framing
{
    public const int MaxFrameBytes = 64 * 1024 * 1024;
    public const int MaxJsonBytes = 512 * 1024 * 1024;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static byte[] Encode(SessionMessage message)
    {
        using var body = new MemoryStream();
        using (var gz = new GZipStream(body, CompressionLevel.Fastest, leaveOpen: true))
            JsonSerializer.Serialize(gz, message, Json);
        if (body.Length > MaxFrameBytes) throw new InvalidDataException($"Message too large to send ({body.Length} bytes).");
        var payload = body.ToArray();
        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);
        return frame;
    }

    public static async Task WriteAsync(Stream stream, SessionMessage message, CancellationToken ct = default)
    {
        await stream.WriteAsync(Encode(message), ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>Reads one message. Returns null when the peer closed the connection cleanly between frames.</summary>
    /// <exception cref="EndOfStreamException">The connection closed mid-frame.</exception>
    /// <exception cref="InvalidDataException">The frame is oversized, not gzip, not JSON, or an unknown message type.</exception>
    public static async Task<SessionMessage?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        var header = new byte[4];
        var got = await stream.ReadAtLeastAsync(header, 4, throwOnEndOfStream: false, ct);
        if (got == 0) return null;
        if (got < 4) throw new EndOfStreamException("Connection closed mid-frame.");
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxFrameBytes) throw new InvalidDataException($"Invalid frame length {length}.");

        var body = new byte[length];
        await stream.ReadExactlyAsync(body, ct);
        using var json = Inflate(body);
        try
        {
            return JsonSerializer.Deserialize<SessionMessage>(json, Json) ?? throw new InvalidDataException("Empty message.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new InvalidDataException($"Malformed message: {ex.Message}", ex);
        }
    }

    static MemoryStream Inflate(byte[] body)
    {
        using var gz = new GZipStream(new MemoryStream(body), CompressionMode.Decompress);
        var output = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = gz.Read(buffer)) > 0)
        {
            if (output.Length + n > MaxJsonBytes) throw new InvalidDataException("Message too large when decompressed.");
            output.Write(buffer, 0, n);
        }
        output.Position = 0;
        return output;
    }
}
