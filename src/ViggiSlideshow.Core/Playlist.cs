namespace ViggiSlideshow.Core;

/// <summary>
/// Decides what to show next for one screen: from history after going back, otherwise a
/// random draw from the library or the next file in folder order.
/// </summary>
public sealed class Playlist
{
    private readonly MediaLibrary _library;
    private readonly Func<string?, SequentialWalker> _walkerFactory;
    private readonly History _history;
    private SequentialWalker? _walker;
    private bool _random;

    /// <param name="walkerFactory">Makes a walker that starts after the given file (or at the start).</param>
    /// <param name="resumeAfter">Where sequential order starts when nothing has been shown yet.</param>
    public Playlist(MediaLibrary library, Func<string?, SequentialWalker> walkerFactory, bool random,
        string? resumeAfter = null, int historyCapacity = 1000)
    {
        _library = library;
        _walkerFactory = walkerFactory;
        _history = new History(historyCapacity);
        _random = random;
        ResumeAfter = resumeAfter;
    }

    public string? ResumeAfter { get; }
    public string? Current => _history.Current;
    public History History => _history;

    public bool Random
    {
        get => _random;
        set
        {
            if (_random == value)
                return;
            _random = value;
            // Sequential order carries on from whatever is on screen now
            _walker = null;
        }
    }

    public string? Next(VideoMode mode)
    {
        // Replay anything ahead of us in history first, skipping what the mode now hides
        while (_history.Forward() is { } seen)
        {
            if (MediaFiles.Matches(MediaFiles.Classify(seen), mode) && !_library.IsBad(seen))
                return seen;
        }

        string? next = _random ? _library.DrawRandom(mode) : NextInOrder(mode);
        if (next is not null)
            _history.Push(next);
        return next;
    }

    /// <summary>
    /// Steps back to the nearest earlier file the mode allows, so going back in videos-only
    /// mode doesn't turn up the photos shown before it. Null when there is none.
    /// </summary>
    public string? Previous(VideoMode mode = VideoMode.Mixed) =>
        _history.Back(path => MediaFiles.Matches(MediaFiles.Classify(path), mode) && !_library.IsBad(path));

    public void MarkBad(string path)
    {
        _library.MarkBad(path);
        _history.Remove(path);
    }

    /// <summary>Hides a file for good and drops it from this screen's history.</summary>
    public bool Hide(string path)
    {
        bool added = _library.Hide(path);
        _history.Remove(path);
        return added;
    }

    private string? NextInOrder(VideoMode mode)
    {
        _walker ??= _walkerFactory(_history.Current ?? ResumeAfter);

        // The walker yields each file once per pass, so meeting the first bad file again means
        // every file the mode allows is bad or hidden (say, every video hidden with X)
        string? firstBad = null;
        for (int guard = 0; guard < 100_000; guard++)
        {
            string? path = _walker.Next(mode);
            if (path is null || !_library.IsBad(path))
                return path;
            if (firstBad is null)
                firstBad = path;
            else if (string.Equals(firstBad, path, StringComparison.OrdinalIgnoreCase))
                return null;
        }
        return null;
    }
}
