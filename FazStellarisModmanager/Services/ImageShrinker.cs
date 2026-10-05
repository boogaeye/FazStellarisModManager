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
            var probe = BitmapDecoder.Create(input, BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            int w = probe.PixelWidth, h = probe.PixelHeight;
            input.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = input;
            if (Math.Max(w, h) > MaxSize)
            {
                if (w >= h) bmp.DecodePixelWidth = MaxSize; else bmp.DecodePixelHeight = MaxSize;
            }
            bmp.EndInit();
            bmp.Freeze();
            var result = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
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
