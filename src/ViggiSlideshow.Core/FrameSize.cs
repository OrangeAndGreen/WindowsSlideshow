namespace ViggiSlideshow.Core;

public static class FrameSize
{
    /// <summary>
    /// The size to decode a video at: the source scaled down to fit the screen and a pixel
    /// budget, never scaled up, with even dimensions.
    /// </summary>
    public static (int Width, int Height) Fit(int sourceWidth, int sourceHeight, int maxWidth, int maxHeight, long maxPixels)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
            return (Math.Max(2, maxWidth), Math.Max(2, maxHeight));

        double scale = 1.0;
        if (maxWidth > 0) scale = Math.Min(scale, (double)maxWidth / sourceWidth);
        if (maxHeight > 0) scale = Math.Min(scale, (double)maxHeight / sourceHeight);
        if (maxPixels > 0) scale = Math.Min(scale, Math.Sqrt((double)maxPixels / ((long)sourceWidth * sourceHeight)));

        int width = Math.Max(2, (int)Math.Round(sourceWidth * scale) & ~1);
        int height = Math.Max(2, (int)Math.Round(sourceHeight * scale) & ~1);
        return (width, height);
    }

    /// <summary>The image decode width that fits the box, for an image whose EXIF rotation swaps its sides.</summary>
    public static int? DecodeWidth(int pixelWidth, int pixelHeight, bool sidesSwapped, int boxWidth, int boxHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0 || boxWidth <= 0 || boxHeight <= 0)
            return null;
        // On screen, a rotated image is pixelHeight wide and pixelWidth tall
        double shownWidth = sidesSwapped ? pixelHeight : pixelWidth;
        double shownHeight = sidesSwapped ? pixelWidth : pixelHeight;
        double scale = Math.Min(boxWidth / shownWidth, boxHeight / shownHeight);
        if (scale >= 1.0)
            return null;
        return Math.Max(1, (int)Math.Round(pixelWidth * scale));
    }
}
