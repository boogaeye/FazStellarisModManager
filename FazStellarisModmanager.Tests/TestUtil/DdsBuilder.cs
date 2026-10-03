using System.Buffers.Binary;
using System.Text;

namespace FazStellarisModmanager.Tests.TestUtil;

/// <summary>Builds tiny DDS files for decoder tests.</summary>
public static class DdsBuilder
{
    /// <summary>Uncompressed 32-bit BGRA (masks R=0x00FF0000, G=0x0000FF00, B=0x000000FF, A=0xFF000000).</summary>
    public static byte[] Bgra32(int w, int h, params (byte R, byte G, byte B, byte A)[] pixels)
    {
        var d = new byte[128 + w * h * 4];
        Header(d, w, h, 0x41, null, 32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000);
        for (int i = 0; i < pixels.Length; i++)
        {
            d[128 + i * 4] = pixels[i].B;
            d[129 + i * 4] = pixels[i].G;
            d[130 + i * 4] = pixels[i].R;
            d[131 + i * 4] = pixels[i].A;
        }
        return d;
    }

    /// <summary>Uncompressed 24-bit BGR, no alpha.</summary>
    public static byte[] Bgr24(int w, int h, params (byte R, byte G, byte B)[] pixels)
    {
        var d = new byte[128 + w * h * 3];
        Header(d, w, h, 0x40, null, 24, 0x00FF0000, 0x0000FF00, 0x000000FF, 0);
        for (int i = 0; i < pixels.Length; i++)
        {
            d[128 + i * 3] = pixels[i].B;
            d[129 + i * 3] = pixels[i].G;
            d[130 + i * 3] = pixels[i].R;
        }
        return d;
    }

    public static byte[] Compressed(string fourCC, int w, int h, byte[] blocks)
    {
        var d = new byte[128 + blocks.Length];
        Header(d, w, h, 0x4, fourCC, 0, 0, 0, 0, 0);
        blocks.CopyTo(d, 128);
        return d;
    }

    static void Header(byte[] d, int w, int h, int pfFlags, string? fourCC, int bitCount, uint r, uint g, uint b, uint a)
    {
        Encoding.ASCII.GetBytes("DDS ").CopyTo(d, 0);
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(4), 124);
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(8), 0x1007);
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(12), h);
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(16), w);
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(76), 32);
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(80), pfFlags);
        if (fourCC is not null) Encoding.ASCII.GetBytes(fourCC).CopyTo(d, 84);
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(88), bitCount);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(92), r);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(96), g);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(100), b);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(104), a);
    }
}
