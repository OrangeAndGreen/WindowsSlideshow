using System.Globalization;
using System.Text;

namespace ViggiSlideshow.Core;

public enum Corner { TopLeft, BottomLeft, BottomRight, TopRight }

/// <summary>Facts about the file on screen, gathered off the UI thread.</summary>
public sealed record MediaInfo
{
    public required string Path { get; init; }
    public required MediaKind Kind { get; init; }
    public long Bytes { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public DateTime? Taken { get; init; }
    public DateTime? FileDate { get; init; }
    public string? Camera { get; init; }
    public TimeSpan? Duration { get; init; }
    public int? FilesInFolder { get; init; }
}

public sealed record LabelOptions(bool FileName, bool Info, bool Debug, bool Help, bool Settings);

/// <summary>Builds the text of the corner label.</summary>
public static class LabelText
{
    public static Corner Next(this Corner corner) => (Corner)(((int)corner + 1) % 4);

    public static string Compose(LabelOptions options, MediaInfo? info, string root,
        Func<string>? debug = null, Func<string>? settings = null)
    {
        var sections = new List<string>();

        if (info is not null)
        {
            var lines = new List<string>();
            if (options.FileName || options.Info)
                lines.Add(DisplayPath(info.Path, root));
            if (options.Info)
                lines.AddRange(InfoLines(info));
            if (lines.Count > 0)
                sections.Add(string.Join("\n", lines));
        }

        if (options.Debug && debug is not null)
            sections.Add(debug());
        if (options.Settings && settings is not null)
            sections.Add(settings());
        if (options.Help)
            sections.Add(HelpText());

        return string.Join("\n\n", sections);
    }

    /// <summary>The path below the slideshow folder, or the full path when it's elsewhere.</summary>
    public static string DisplayPath(string path, string root)
    {
        if (root.Length == 0)
            return path;
        string relative = System.IO.Path.GetRelativePath(root, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative) ? path : relative;
    }

    public static IEnumerable<string> InfoLines(MediaInfo info)
    {
        var size = new StringBuilder();
        if (info.Width is { } w && info.Height is { } h)
            size.Append(CultureInfo.InvariantCulture, $"{w} × {h}  ·  ");
        size.Append(FormatBytes(info.Bytes));
        if (info.Duration is { } duration)
            size.Append("  ·  ").Append(FormatDuration(duration));
        yield return size.ToString();

        if (info.Taken is { } taken)
            yield return "Taken " + taken.ToString("ddd d MMM yyyy, h:mm tt", CultureInfo.CurrentCulture);
        else if (info.FileDate is { } fileDate)
            yield return "File date " + fileDate.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture);

        if (!string.IsNullOrWhiteSpace(info.Camera))
            yield return info.Camera!;

        if (info.FilesInFolder is { } count)
            yield return count == 1 ? "1 file in this folder" : $"{count:N0} files in this folder";
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.00} GB",
    };

    public static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                                 : duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    /// <summary>Joins a camera make and model without repeating the make ("Canon Canon EOS 5D").</summary>
    public static string? CameraName(string? make, string? model)
    {
        make = make?.Trim().TrimEnd('\0');
        model = model?.Trim().TrimEnd('\0');
        if (string.IsNullOrEmpty(model))
            return string.IsNullOrEmpty(make) ? null : make;
        if (string.IsNullOrEmpty(make) || model.StartsWith(make, StringComparison.OrdinalIgnoreCase))
            return model;

        // "NIKON CORPORATION" + "NIKON D90": the model already says who made it
        string firstWord = make.Split(' ')[0];
        if (model.StartsWith(firstWord, StringComparison.OrdinalIgnoreCase))
            return model;
        return $"{make} {model}";
    }

    public static string HelpText()
    {
        var help = KeyMap.Help;
        int width = help.Max(row => row.Keys.Length);
        return "Keys\n" + string.Join("\n", help.Select(row => $"{row.Keys.PadRight(width)}   {row.Description}"));
    }
}
