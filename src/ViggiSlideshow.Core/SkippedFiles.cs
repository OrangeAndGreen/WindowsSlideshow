using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ViggiSlideshow.Core;

/// <summary>One file the show couldn't play: how often, when, and why the last time.</summary>
public sealed record SkippedFile(string Path, string Reason, int Count, DateTime FirstSeen, DateTime LastSeen);

/// <summary>
/// Files the show skipped because they couldn't be shown, kept across runs in a file of its own
/// next to the log, so broken files can be found together and fixed or moved. One tab-separated
/// line per file: count, first seen, last seen, path, and the latest reason, sorted by path so a
/// folder's broken files sit together. Delete a line once the file is fixed; a file that later
/// plays fine stays listed until then. Recording never throws; a problem is logged and the show
/// carries on. Each record rereads the file and replaces it whole, under a lock shared by every
/// screensaver process, so two writers never lose each other's counts.
/// </summary>
public sealed class SkippedFiles
{
    public const string FileName = "skipped.txt";
    private const string DateFormat = "yyyy-MM-dd HH:mm:ss";
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

    private static readonly string[] Header =
    [
        "# Files the slideshow skipped because they couldn't be shown. Delete a line once the file is fixed or moved.",
        "# count\tfirst seen\tlast seen\tpath\tlatest reason",
    ];

    private readonly object _lock = new();

    public SkippedFiles(string filePath)
    {
        FilePath = filePath;
    }

    /// <summary>The list in the log's folder, %LOCALAPPDATA%\ViggiSlideshow\skipped.txt.</summary>
    public static string DefaultPath => System.IO.Path.Combine(Log.Folder, FileName);

    public string FilePath { get; }

    /// <summary>Counts one more skip of the file and saves the list.</summary>
    public void Record(string path, string reason) => Record(path, reason, DateTime.Now);

    public void Record(string path, string reason, DateTime when)
    {
        if (path.Trim().Length == 0)
            return;
        try
        {
            lock (_lock)
            {
                using var mutex = new Mutex(false, MutexName(FilePath));
                bool held;
                try
                {
                    held = mutex.WaitOne(LockTimeout);
                }
                catch (AbandonedMutexException)
                {
                    held = true; // another process died holding it; the file itself is whole
                }
                if (!held)
                {
                    Log.Error($"Couldn't lock the skipped list {FilePath} to record {path}: {reason}");
                    return;
                }
                try
                {
                    Save(Update(ReadLines(), path, reason, when));
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"Saving {path} to the skipped list {FilePath}", e);
        }
    }

    /// <summary>Everything in the list now, or nothing when it isn't there or can't be read.</summary>
    public IReadOnlyList<SkippedFile> Load()
    {
        try
        {
            return Parse(ReadLines()).ToList();
        }
        catch (Exception e)
        {
            Log.Error($"Reading the skipped list {FilePath}", e);
            return [];
        }
    }

    private string[] ReadLines() => File.Exists(FilePath) ? File.ReadAllLines(FilePath) : [];

    // Written beside the list and moved over it, so a reader never sees half a file
    private void Save(IEnumerable<string> lines)
    {
        if (System.IO.Path.GetDirectoryName(FilePath) is { Length: > 0 } folder)
            Directory.CreateDirectory(folder);
        string temp = $"{FilePath}.{Environment.ProcessId}.tmp";
        File.WriteAllLines(temp, lines);
        File.Move(temp, FilePath, overwrite: true);
    }

    // Named after the file, so tests on their own files don't wait on the real one
    private static string MutexName(string filePath)
    {
        string key = System.IO.Path.GetFullPath(filePath).ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        return $@"Local\ViggiSlideshow.skipped.{hash}";
    }

    // ---- The file's text, kept pure so it can be tested ----

    /// <summary>The list's lines with one more skip of <paramref name="path"/> counted in.</summary>
    public static IEnumerable<string> Update(IEnumerable<string> lines, string path, string reason, DateTime when)
    {
        var records = new Dictionary<string, SkippedFile>(StringComparer.OrdinalIgnoreCase);
        foreach (SkippedFile record in Parse(lines))
            records[record.Path] = records.TryGetValue(record.Path, out SkippedFile? earlier) ? Combine(earlier, record) : record;

        string key = path.Trim();
        records.TryGetValue(key, out SkippedFile? existing);
        records[key] = Merge(existing, key, reason, when);
        return Format(records.Values);
    }

    /// <summary>One more skip on top of what was known about the file, if anything.</summary>
    public static SkippedFile Merge(SkippedFile? existing, string path, string reason, DateTime when)
    {
        string cleaned = Clean(reason);
        if (existing is null)
            return new SkippedFile(path, cleaned, 1, when, when);
        return existing with
        {
            Reason = cleaned,
            Count = existing.Count + 1,
            FirstSeen = existing.FirstSeen < when ? existing.FirstSeen : when,
            LastSeen = existing.LastSeen > when ? existing.LastSeen : when,
        };
    }

    /// <summary>The files in the list's lines. Blanks, # comments and lines that don't parse are left out.</summary>
    public static IEnumerable<SkippedFile> Parse(IEnumerable<string> lines)
    {
        foreach (string line in lines)
        {
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#'))
                continue;
            string[] fields = line.Split('\t');
            if (fields.Length < 4)
                continue;
            if (!int.TryParse(fields[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 1)
                continue;
            if (!TryParseDate(fields[1], out DateTime first) || !TryParseDate(fields[2], out DateTime last))
                continue;
            string path = fields[3].Trim();
            if (path.Length == 0)
                continue;
            string reason = fields.Length > 4 ? Clean(string.Join(" ", fields[4..])) : "";
            yield return new SkippedFile(path, reason, count, first, last);
        }
    }

    /// <summary>The file's lines: the header, then one line per file in path order.</summary>
    public static IEnumerable<string> Format(IEnumerable<SkippedFile> records)
    {
        foreach (string line in Header)
            yield return line;
        foreach (SkippedFile record in records.OrderBy(r => r.Path, NaturalComparer.Instance))
        {
            yield return string.Join('\t',
                record.Count.ToString(CultureInfo.InvariantCulture),
                record.FirstSeen.ToString(DateFormat, CultureInfo.InvariantCulture),
                record.LastSeen.ToString(DateFormat, CultureInfo.InvariantCulture),
                record.Path,
                Clean(record.Reason));
        }
    }

    // A path listed twice (two lists pasted together by hand) becomes one line
    private static SkippedFile Combine(SkippedFile a, SkippedFile b)
    {
        SkippedFile later = b.LastSeen >= a.LastSeen ? b : a;
        return later with
        {
            Count = a.Count + b.Count,
            FirstSeen = a.FirstSeen < b.FirstSeen ? a.FirstSeen : b.FirstSeen,
        };
    }

    private static bool TryParseDate(string text, out DateTime value) =>
        DateTime.TryParseExact(text.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    // Tabs and line breaks would break the line apart
    private static string Clean(string reason)
    {
        var builder = new StringBuilder(reason.Length);
        foreach (char c in reason)
            builder.Append(c is '\t' or '\r' or '\n' ? ' ' : c);
        string cleaned = builder.ToString().Trim();
        return cleaned.Length == 0 ? "no reason given" : cleaned;
    }
}
