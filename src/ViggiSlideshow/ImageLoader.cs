using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ViggiSlideshow.Core;

namespace ViggiSlideshow;

/// <param name="Width">The photo's own width as shown, after EXIF rotation.</param>
internal sealed record LoadedImage(BitmapSource Bitmap, int Width, int Height);

/// <summary>Decodes a photo at the size it will be shown, turned the right way up. Safe on any thread.</summary>
internal static class ImageLoader
{
    public static LoadedImage Load(string path, int boxWidth, int boxHeight)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);

        BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        BitmapFrame frame = decoder.Frames[0];
        int pixelWidth = frame.PixelWidth;
        int pixelHeight = frame.PixelHeight;
        int orientation = ReadOrientation(frame);
        bool sidesSwapped = orientation is 5 or 6 or 7 or 8;

        stream.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = stream;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (FrameSize.DecodeWidth(pixelWidth, pixelHeight, sidesSwapped, boxWidth, boxHeight) is { } decodeWidth)
            bitmap.DecodePixelWidth = decodeWidth;
        bitmap.EndInit();

        BitmapSource result = Orient(bitmap, orientation);
        result.Freeze();
        return new LoadedImage(result, sidesSwapped ? pixelHeight : pixelWidth, sidesSwapped ? pixelWidth : pixelHeight);
    }

    private static int ReadOrientation(BitmapFrame frame)
    {
        if (frame.Metadata is not BitmapMetadata metadata)
            return 1;

        foreach (string query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
        {
            try
            {
                if (metadata.ContainsQuery(query) && metadata.GetQuery(query) is { } value)
                    return Convert.ToInt32(value);
            }
            catch (Exception)
            {
                // Some formats refuse queries they don't support; try the next one
            }
        }
        return 1;
    }

    // EXIF orientation: 1 normal, 3 upside down, 6 and 8 a quarter turn; 2, 4, 5, 7 are those mirrored
    private static BitmapSource Orient(BitmapSource source, int orientation)
    {
        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(270) } },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(90) } },
            8 => new RotateTransform(270),
            _ => null,
        };
        return transform is null ? source : new TransformedBitmap(source, transform);
    }
}
