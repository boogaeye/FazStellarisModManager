using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FazStellarisModmanager.Services;

/// <summary>Turns a picture (png/jpg/bmp/gif) into a PNG whose longer side is at most <see cref="MaxSize"/> px. Thread-safe (no shared WPF objects).</summary>
public static class ImageShrinker
{
    public const int MaxSize = 64;

    /// <summary>The small PNG, or null when the bytes can't be decoded.</summary>
    public static byte[]? ToSmallPng(byte[] image)
    {
        try
        {
            using var input = new MemoryStream(image);
            var frame = BitmapDecoder.Create(input, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
            var scale = Math.Min(1.0, (double)MaxSize / Math.Max(frame.PixelWidth, frame.PixelHeight));
            BitmapSource result = scale < 1 ? new TransformedBitmap(frame, new ScaleTransform(scale, scale)) : frame;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(result));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }
}
