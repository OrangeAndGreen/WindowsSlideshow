namespace ViggiSlideshow.Core;

/// <summary>
/// Files hidden from the show for good (the X key). Kept as a plain text file, one full path per
/// line, so it can be edited by hand: blank lines and lines starting with # are ignored, and paths
/// match whatever their case. Reading and saving never throw; a problem is logged and the show
/// carries on. Safe to use from the scan thread and the UI thread at once.
/// </summary>
public sealed class HiddenFiles
{
    public const string FileName = "hidden.txt";

    private readonly object _lock = new();
    private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="filePath">Where the list is saved; null keeps it in memory only.</param>
    public HiddenFiles(string? filePath = null, IEnumerable<string>? paths = null)
    {
        FilePath = filePath;
        if (paths is not null)
        {
            foreach (string path in paths)
                _paths.Add(path);
        }
    }

    /// <summary>The list in the log's folder, %LOCALAPPDATA%\ViggiSlideshow\hidden.txt.</summary>
    public static string DefaultPath => Path.Combine(Log.Folder, FileName);

    public string? FilePath { get; }

    public int Count { get { lock (_lock) return _paths.Count; } }

    /// <summary>Reads the list, or starts an empty one when the file isn't there or can't be read.</summary>
    public static HiddenFiles Load(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                return new HiddenFiles(filePath, Parse(File.ReadAllLines(filePath)));
        }
        catch (Exception e)
        {
            Log.Error($"Reading the hidden list {filePath}", e);
        }
        return new HiddenFiles(filePath);
    }

    /// <summary>The paths in the file's lines, skipping blanks and # comments.</summary>
    public static IEnumerable<string> Parse(IEnumerable<string> lines)
    {
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;
            if (Normalize(trimmed) is { } path)
                yield return path;
        }
    }

    public bool Contains(string path)
    {
        if (Normalize(path) is not { } normalized)
            return false;
        lock (_lock)
            return _paths.Contains(normalized);
    }

    /// <summary>Hides a file and appends it to the saved list. False when it was already hidden.</summary>
    public bool Add(string path)
    {
        if (Normalize(path) is not { } normalized)
            return false;
        lock (_lock)
        {
            if (!_paths.Add(normalized))
                return false;
            Append(normalized);
            return true;
        }
    }

    private void Append(string path)
    {
        if (FilePath is null)
            return;
        try
        {
            if (Path.GetDirectoryName(FilePath) is { Length: > 0 } folder)
                Directory.CreateDirectory(folder);

            // A list edited by hand may not end in a newline; don't glue the new path onto its last line
            string prefix = EndsWithoutNewline(FilePath) ? Environment.NewLine : "";
            File.AppendAllText(FilePath, prefix + path + Environment.NewLine);
        }
        catch (Exception e)
        {
            Log.Error($"Saving {path} to the hidden list {FilePath}", e);
        }
    }

    private static bool EndsWithoutNewline(string filePath)
    {
        var file = new FileInfo(filePath);
        if (!file.Exists || file.Length == 0)
            return false;
        using FileStream stream = file.OpenRead();
        stream.Seek(-1, SeekOrigin.End);
        return stream.ReadByte() != '\n';
    }

    // Quotes from "Copy as path" and forward slashes typed by hand still match
    private static string? Normalize(string path)
    {
        string trimmed = path.Trim().Trim('"').Trim();
        if (trimmed.Length == 0)
            return null;
        trimmed = trimmed.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception)
        {
            return trimmed;
        }
    }
}
