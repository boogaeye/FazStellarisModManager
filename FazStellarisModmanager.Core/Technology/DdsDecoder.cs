using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>Decodes the top mip level of a DDS file (uncompressed 24/32-bit with masks, DXT1, DXT3, DXT5) to RGBA bytes.</summary>
public static class DdsDecoder
{
    const int HeaderSize = 128;
    const int MaxSide = 4096;

    delegate void BlockDecoder(ReadOnlySpan<byte> block, Span<uint> pixels);

    /// <exception cref="InvalidDataException">Not a DDS file, truncated, or an unsupported format.</exception>
    public static (int Width, int Height, byte[] Rgba) Decode(ReadOnlySpan<byte> dds)
    {
        if (dds.Length < HeaderSize || dds[0] != (byte)'D' || dds[1] != (byte)'D' || dds[2] != (byte)'S' || dds[3] != (byte)' ')
            throw new InvalidDataException("Not a DDS file.");
        if (I32(dds, 4) != 124 || I32(dds, 76) != 32) throw new InvalidDataException("Not a DDS file.");
        var flags = I32(dds, 8);
        var height = I32(dds, 12);
        var width = I32(dds, 16);
        if (width <= 0 || height <= 0 || width > MaxSide || height > MaxSide) throw new InvalidDataException($"Bad DDS size {width}x{height}.");
        var pfFlags = I32(dds, 80);
        var data = dds[HeaderSize..];

        if ((pfFlags & 0x4) != 0)
        {
            var fourCC = Encoding.ASCII.GetString(dds.Slice(84, 4));
            return fourCC switch
            {
                "DXT1" => (width, height, DecodeBlocks(data, width, height, 8, Dxt1)),
                "DXT3" => (width, height, DecodeBlocks(data, width, height, 16, Dxt3)),
                "DXT5" => (width, height, DecodeBlocks(data, width, height, 16, Dxt5)),
                _ => throw new InvalidDataException($"Unsupported DDS compression '{fourCC}'."),
            };
        }

        var bitCount = I32(dds, 88);
        if ((pfFlags & 0x40) != 0 && bitCount is 24 or 32)
        {
            var bpp = bitCount / 8;
            var pitchField = I32(dds, 20);
            var pitch = (flags & 0x80000) == 0 && (flags & 0x8) != 0 && pitchField >= width * bpp ? pitchField : width * bpp;
            var alphaMask = (pfFlags & 0x1) != 0 ? U32(dds, 104) : 0u;
            return (width, height, DecodeMasked(data, width, height, bpp, pitch, U32(dds, 92), U32(dds, 96), U32(dds, 100), alphaMask));
        }

        throw new InvalidDataException($"Unsupported DDS pixel format (flags 0x{pfFlags:x}, {bitCount} bpp).");
    }

    static int I32(ReadOnlySpan<byte> s, int offset) => BinaryPrimitives.ReadInt32LittleEndian(s[offset..]);

    static uint U32(ReadOnlySpan<byte> s, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(s[offset..]);

    static byte[] DecodeMasked(ReadOnlySpan<byte> data, int w, int h, int bpp, int pitch, uint r, uint g, uint b, uint a)
    {
        if (data.Length < (long)pitch * (h - 1) + (long)w * bpp) throw new InvalidDataException("Truncated DDS data.");
        var output = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var o = y * pitch + x * bpp;
                uint px = bpp == 4 ? BinaryPrimitives.ReadUInt32LittleEndian(data[o..]) : (uint)(data[o] | data[o + 1] << 8 | data[o + 2] << 16);
                var i = (y * w + x) * 4;
                output[i] = Channel(px, r);
                output[i + 1] = Channel(px, g);
                output[i + 2] = Channel(px, b);
                output[i + 3] = a == 0 ? (byte)255 : Channel(px, a);
            }
        return output;
    }

    static byte Channel(uint px, uint mask)
    {
        if (mask == 0) return 0;
        var shift = BitOperations.TrailingZeroCount(mask);
        var max = mask >> shift;
        var value = (px & mask) >> shift;
        return (byte)(max == 255 ? value : (ulong)value * 255 / max);
    }

    static byte[] DecodeBlocks(ReadOnlySpan<byte> data, int w, int h, int blockBytes, BlockDecoder decode)
    {
        int bw = (w + 3) / 4, bh = (h + 3) / 4;
        if (data.Length < (long)bw * bh * blockBytes) throw new InvalidDataException("Truncated DDS data.");
        var output = new byte[w * h * 4];
        Span<uint> pixels = stackalloc uint[16];
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                decode(data.Slice((by * bw + bx) * blockBytes, blockBytes), pixels);
                for (int j = 0; j < 4; j++)
                    for (int i = 0; i < 4; i++)
                    {
                        int x = bx * 4 + i, y = by * 4 + j;
                        if (x >= w || y >= h) continue;
                        var p = pixels[j * 4 + i];
                        var o = (y * w + x) * 4;
                        output[o] = (byte)p;
                        output[o + 1] = (byte)(p >> 8);
                        output[o + 2] = (byte)(p >> 16);
                        output[o + 3] = (byte)(p >> 24);
                    }
            }
        return output;
    }

    static void Dxt1(ReadOnlySpan<byte> block, Span<uint> pixels) => ColorBlock(block, pixels, dxt1: true);

    static void Dxt3(ReadOnlySpan<byte> block, Span<uint> pixels)
    {
        ColorBlock(block[8..], pixels, dxt1: false);
        for (int i = 0; i < 16; i++)
        {
            var nibble = (block[i / 2] >> (4 * (i % 2))) & 0xF;
            pixels[i] = (pixels[i] & 0x00FFFFFF) | (uint)(nibble * 17) << 24;
        }
    }

    static void Dxt5(ReadOnlySpan<byte> block, Span<uint> pixels)
    {
        ColorBlock(block[8..], pixels, dxt1: false);
        int a0 = block[0], a1 = block[1];
        Span<int> alpha = stackalloc int[8];
        alpha[0] = a0;
        alpha[1] = a1;
        if (a0 > a1)
        {
            for (int i = 1; i <= 6; i++) alpha[i + 1] = ((7 - i) * a0 + i * a1) / 7;
        }
        else
        {
            for (int i = 1; i <= 4; i++) alpha[i + 1] = ((5 - i) * a0 + i * a1) / 5;
            alpha[6] = 0;
            alpha[7] = 255;
        }
        ulong bits = 0;
        for (int i = 0; i < 6; i++) bits |= (ulong)block[2 + i] << (8 * i);
        for (int i = 0; i < 16; i++)
            pixels[i] = (pixels[i] & 0x00FFFFFF) | (uint)alpha[(int)(bits >> (3 * i)) & 7] << 24;
    }

    static void ColorBlock(ReadOnlySpan<byte> block, Span<uint> pixels, bool dxt1)
    {
        var c0 = (ushort)(block[0] | block[1] << 8);
        var c1 = (ushort)(block[2] | block[3] << 8);
        var (r0, g0, b0) = Rgb565(c0);
        var (r1, g1, b1) = Rgb565(c1);
        Span<uint> palette = stackalloc uint[4];
        palette[0] = Pack(r0, g0, b0, 255);
        palette[1] = Pack(r1, g1, b1, 255);
        if (c0 > c1 || !dxt1)
        {
            palette[2] = Pack((2 * r0 + r1) / 3, (2 * g0 + g1) / 3, (2 * b0 + b1) / 3, 255);
            palette[3] = Pack((r0 + 2 * r1) / 3, (g0 + 2 * g1) / 3, (b0 + 2 * b1) / 3, 255);
        }
        else
        {
            palette[2] = Pack((r0 + r1) / 2, (g0 + g1) / 2, (b0 + b1) / 2, 255);
            palette[3] = 0; // transparent black
        }
        var indices = (uint)(block[4] | block[5] << 8 | block[6] << 16 | block[7] << 24);
        for (int i = 0; i < 16; i++) pixels[i] = palette[(int)(indices >> (2 * i)) & 3];
    }

    static (int R, int G, int B) Rgb565(ushort c)
    {
        int r = c >> 11 & 31, g = c >> 5 & 63, b = c & 31;
        return (r << 3 | r >> 2, g << 2 | g >> 4, b << 3 | b >> 2);
    }

    static uint Pack(int r, int g, int b, int a) => (uint)(r | g << 8 | b << 16 | a << 24);
}
