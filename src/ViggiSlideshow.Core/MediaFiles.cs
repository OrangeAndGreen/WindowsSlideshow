namespace ViggiSlideshow.Core;

public enum MediaKind { None, Image, Video }

/// <summary>Which kinds of media the slideshow shows.</summary>
public enum VideoMode { Mixed, PhotosOnly, VideosOnly }

public static class MediaFiles
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".heif",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".avi", ".mpg", ".mpeg", ".wmv", ".mkv", ".mts", ".m2ts", ".3gp", ".webm",
    };

    public static MediaKind Classify(string path)
    {
        string name = Path.GetFileName(path);

        // macOS leaves "._name.jpg" resource-fork files behind on shared drives; they aren't pictures
        if (name.StartsWith("._", StringComparison.Ordinal))
            return MediaKind.None;

        string extension = Path.GetExtension(name);
        if (ImageExtensions.Contains(extension))
            return MediaKind.Image;
        if (VideoExtensions.Contains(extension))
            return MediaKind.Video;
        return MediaKind.None;
    }

    public static bool IsMedia(string path) => Classify(path) != MediaKind.None;

    public static bool Matches(MediaKind kind, VideoMode mode) => mode switch
    {
        VideoMode.PhotosOnly => kind == MediaKind.Image,
        VideoMode.VideosOnly => kind == MediaKind.Video,
        _ => kind != MediaKind.None,
    };

    public static VideoMode Next(this VideoMode mode) => mode switch
    {
        VideoMode.Mixed => VideoMode.PhotosOnly,
        VideoMode.PhotosOnly => VideoMode.VideosOnly,
        _ => VideoMode.Mixed,
    };

    /// <summary>The W key: straight to videos only, and from there back to the mixed show.</summary>
    public static VideoMode ToggleVideosOnly(this VideoMode mode) =>
        mode == VideoMode.VideosOnly ? VideoMode.Mixed : VideoMode.VideosOnly;

    public static string Describe(this VideoMode mode) => mode switch
    {
        VideoMode.PhotosOnly => "Photos only",
        VideoMode.VideosOnly => "Videos only",
        _ => "Photos and videos",
    };
}
