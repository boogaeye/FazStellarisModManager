using System.Buffers.Binary;
using System.IO.Compression;
using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class DdsAndPngTests
{
    static byte[] Px(byte[] rgba, int i) => rgba[(i * 4)..(i * 4 + 4)];

    [Fact]
    public void Decodes_uncompressed_32_and_24_bit()
    {
        var (w, h, rgba) = DdsDecoder.Decode(DdsBuilder.Bgra32(2, 1, (255, 0, 0, 255), (10, 20, 30, 40)));
        Assert.Equal((2, 1), (w, h));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Px(rgba, 0));
        Assert.Equal(new byte[] { 10, 20, 30, 40 }, Px(rgba, 1));

        var (_, _, rgb) = DdsDecoder.Decode(DdsBuilder.Bgr24(1, 1, (1, 2, 3)));
        Assert.Equal(new byte[] { 1, 2, 3, 255 }, Px(rgb, 0));
    }

    [Fact]
    public void Decodes_dxt1_opaque_and_transparent_blocks()
    {
        // color0 = pure red (0xF800) > color1 = pure blue (0x001F): 4-colour mode, all indices 0 -> red.
        var red = DdsDecoder.Decode(DdsBuilder.Compressed("DXT1", 4, 4, [0x00, 0xF8, 0x1F, 0x00, 0, 0, 0, 0])).Rgba;
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Px(red, 0));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Px(red, 15));

        // color0 <= color1: 3-colour mode, index 3 = transparent black. Image 2x2 (smaller than one block).
        var (w, h, clear) = DdsDecoder.Decode(DdsBuilder.Compressed("DXT1", 2, 2, [0x1F, 0x00, 0x00, 0xF8, 0xFF, 0xFF, 0xFF, 0xFF]));
        Assert.Equal((2, 2, 16), (w, h, clear.Length));
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, Px(clear, 3));
    }

    [Fact]
    public void Decodes_dxt3_and_dxt5_alpha()
    {
        byte[] colour = [0x00, 0xF8, 0x1F, 0x00, 0, 0, 0, 0];
        byte[] dxt3 = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F, .. colour]; // last pixel's alpha nibble = 0
        var a3 = DdsDecoder.Decode(DdsBuilder.Compressed("DXT3", 4, 4, dxt3)).Rgba;
        Assert.Equal(255, a3[3]);
        Assert.Equal(0, a3[15 * 4 + 3]);

        byte[] dxt5Index0 = [255, 0, 0, 0, 0, 0, 0, 0, .. colour];
        Assert.Equal(255, DdsDecoder.Decode(DdsBuilder.Compressed("DXT5", 4, 4, dxt5Index0)).Rgba[3]);
        byte[] dxt5Index1 = [255, 0, 0x49, 0x92, 0x24, 0x49, 0x92, 0x24, .. colour]; // every 3-bit index = 1 -> alpha1 = 0
        var a5 = DdsDecoder.Decode(DdsBuilder.Compressed("DXT5", 4, 4, dxt5Index1)).Rgba;
        Assert.Equal(0, a5[3]);
        Assert.Equal(0, a5[15 * 4 + 3]);
    }

    [Fact]
    public void Rejects_invalid_or_unsupported_files()
    {
        Assert.Throws<InvalidDataException>(() => DdsDecoder.Decode(new byte[10]));
        Assert.Throws<InvalidDataException>(() => DdsDecoder.Decode(DdsBuilder.Compressed("DX10", 4, 4, new byte[16])));
        var truncated = DdsBuilder.Bgra32(4, 4)[..130];
        Assert.Throws<InvalidDataException>(() => DdsDecoder.Decode(truncated));
    }

    [Fact]
    public void Encodes_a_valid_png()
    {
        var png = PngEncoder.Encode(1, 1, [255, 0, 0, 255]);

        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png, 12, 4));
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
        Assert.Equal(new byte[] { 0xAE, 0x42, 0x60, 0x82 }, png[^4..]); // IEND CRC

        var idatLength = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(33));
        Assert.Equal("IDAT", System.Text.Encoding.ASCII.GetString(png, 37, 4));
        using var z = new ZLibStream(new MemoryStream(png, 41, idatLength), CompressionMode.Decompress);
        var raw = new MemoryStream();
        z.CopyTo(raw);
        Assert.Equal(new byte[] { 0, 255, 0, 0, 255 }, raw.ToArray());
    }

    [Theory]
    [InlineData(0xFFFFFFFFu, 255)]
    [InlineData(0x80000000u, 127)]
    public void Full_width_mask_does_not_overflow(uint pixel, int expectedR)
    {
        var dds = DdsBuilder.Bgra32(1, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(92), 0xFFFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(96), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(100), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(104), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(128), pixel);

        var rgba = DdsDecoder.Decode(dds).Rgba;

        Assert.InRange(rgba[0], expectedR - 1, expectedR + 1);
    }

    [Fact]
    public void Linear_size_flag_ignores_pitch_field()
    {
        var dds = DdsBuilder.Bgra32(2, 1, (1, 2, 3, 4), (5, 6, 7, 8));
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(8), 0x1007 | 0x80000);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(20), 8 * 1000); // linear size, not a pitch
        var (_, _, rgba) = DdsDecoder.Decode(dds);
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, Px(rgba, 1));
    }

    [Fact]
    public void Bad_header_sizes_are_rejected()
    {
        var a = DdsBuilder.Bgra32(1, 1);
        BinaryPrimitives.WriteInt32LittleEndian(a.AsSpan(4), 100);
        Assert.Throws<InvalidDataException>(() => DdsDecoder.Decode(a));
        var b = DdsBuilder.Bgra32(1, 1);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(76), 16);
        Assert.Throws<InvalidDataException>(() => DdsDecoder.Decode(b));
    }

    [Fact]
    public void Png_rejects_non_positive_dimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PngEncoder.Encode(0, 1, new byte[4]));
        Assert.Throws<ArgumentOutOfRangeException>(() => PngEncoder.Encode(1, -1, new byte[4]));
    }
}
