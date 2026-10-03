using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FazStellarisModmanager.Core.Session;

/// <summary>Wire format: 4-byte little-endian body length, then the body (gzip-compressed UTF-8 JSON of one <see cref="SessionMessage"/>).</summary>
public static class Framing
{
    public const int MaxFrameBytes = 64 * 1024 * 1024;
    public const int MaxJsonBytes = 128 * 1024 * 1024;

    /// <summary>Frame cap for the first (Hello) message from a not-yet-trusted peer.</summary>
    public const int MaxHandshakeBytes = 16 * 1024;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
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
    /// <exception cref="InvalidDataException">The frame is oversized, not gzip, not JSON, incomplete, or an unknown message type.</exception>
    public static async Task<SessionMessage?> ReadAsync(Stream stream, CancellationToken ct = default, int maxFrameBytes = MaxFrameBytes)
    {
        var header = new byte[4];
        var got = await stream.ReadAtLeastAsync(header, 4, throwOnEndOfStream: false, ct);
        if (got == 0) return null;
        if (got < 4) throw new EndOfStreamException("Connection closed mid-frame.");
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > maxFrameBytes) throw new InvalidDataException($"Invalid frame length {length}.");

        var body = new byte[length];
        await stream.ReadExactlyAsync(body, ct);
        try
        {
            using var gz = new GZipStream(new MemoryStream(body), CompressionMode.Decompress);
            using var limited = new LimitedReadStream(gz, MaxJsonBytes);
            return await JsonSerializer.DeserializeAsync<SessionMessage>(limited, Json, ct) ?? throw new InvalidDataException("Empty message.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new InvalidDataException($"Malformed message: {ex.Message}", ex);
        }
    }

    /// <summary>Read-only wrapper that throws once more than a limit of bytes has been read (guards against gzip bombs).</summary>
    sealed class LimitedReadStream(Stream inner, long limit) : Stream
    {
        long _read;

        int Count(int n)
        {
            _read += n;
            if (_read > limit) throw new InvalidDataException("Message too large when decompressed.");
            return n;
        }

        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));
        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken));

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
