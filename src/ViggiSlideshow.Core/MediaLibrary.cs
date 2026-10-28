namespace ViggiSlideshow.Core;

/// <summary>
/// Every media file found so far, filled by <see cref="MediaScanner"/> on a background thread
/// while the slideshow draws from it. Random draws come from a shuffle bag, so nothing repeats
/// until everything has been shown once. Files on the <see cref="HiddenFiles"/> list are left out.
/// </summary>
public sealed class MediaLibrary
{
    private readonly object _lock = new();
    private readonly Random _random;

    private readonly List<string> _images = new();
    private readonly List<string> _videos = new();

    // Indices not yet drawn in the current round
    private readonly List<int> _imageBag = new();
    private readonly List<int> _videoBag = new();

    private readonly HashSet<string> _bad = new(StringComparer.OrdinalIgnoreCase);
    private readonly HiddenFiles _hidden;
    private volatile bool _scanComplete;

    public MediaLibrary(Random? random = null, HiddenFiles? hidden = null)
    {
        _random = random ?? new Random();
        _hidden = hidden ?? new HiddenFiles();
    }

    public HiddenFiles Hidden => _hidden;

    public bool ScanComplete
    {
        get => _scanComplete;
        set => _scanComplete = value;
    }

    public int ImageCount { get { lock (_lock) return _images.Count; } }
    public int VideoCount { get { lock (_lock) return _videos.Count; } }

    public void Add(string path)
    {
        MediaKind kind = MediaFiles.Classify(path);
        if (kind == MediaKind.None || _hidden.Contains(path))
            return;

        lock (_lock)
        {
            if (kind == MediaKind.Image)
            {
                _imageBag.Add(_images.Count);
                _images.Add(path);
            }
            else
            {
                _videoBag.Add(_videos.Count);
                _videos.Add(path);
            }
        }
    }

    /// <summary>Keeps a file that failed to load from being drawn again this run.</summary>
    public void MarkBad(string path)
    {
        lock (_lock)
            _bad.Add(path);
    }

    /// <summary>
    /// Hides a file for good: it is added to the saved hidden list and, like a bad file, never
    /// drawn again this run. False when it was already hidden.
    /// </summary>
    public bool Hide(string path)
    {
        MarkBad(path);
        return _hidden.Add(path);
    }

    /// <summary>True for a file that failed this run or is on the hidden list.</summary>
    public bool IsBad(string path)
    {
        lock (_lock)
        {
            if (_bad.Contains(path))
                return true;
        }
        return _hidden.Contains(path);
    }

    /// <summary>A random file the mode allows, or null when none has been found yet.</summary>
    public string? DrawRandom(VideoMode mode)
    {
        lock (_lock)
        {
            bool useImages = mode != VideoMode.VideosOnly;
            bool useVideos = mode != VideoMode.PhotosOnly;

            // Every draw that lands on a bad file removes it from the bag, so this ends
            int attempts = _images.Count + _videos.Count + 2;
            while (attempts-- > 0)
            {
                int imagesLeft = useImages ? _imageBag.Count : 0;
                int videosLeft = useVideos ? _videoBag.Count : 0;

                if (imagesLeft + videosLeft == 0)
                {
                    // Round finished: refill the bags this mode draws from
                    bool refilled = false;
                    if (useImages && _images.Count > 0) { Refill(_imageBag, _images.Count); refilled = true; }
                    if (useVideos && _videos.Count > 0) { Refill(_videoBag, _videos.Count); refilled = true; }
                    if (!refilled)
                        return null;
                    continue;
                }

                int pick = _random.Next(imagesLeft + videosLeft);
                string path = pick < imagesLeft
                    ? _images[TakeAt(_imageBag, pick)]
                    : _videos[TakeAt(_videoBag, pick - imagesLeft)];

                if (!_bad.Contains(path))
                    return path;

                if (_bad.Count >= CountFor(useImages, useVideos))
                    return null;
            }
            return null;
        }
    }

    private int CountFor(bool images, bool videos) => (images ? _images.Count : 0) + (videos ? _videos.Count : 0);

    private static void Refill(List<int> bag, int count)
    {
        bag.Clear();
        for (int i = 0; i < count; i++)
            bag.Add(i);
    }

    // Removes and returns the bag entry at position, by swapping in the last entry
    private static int TakeAt(List<int> bag, int position)
    {
        int value = bag[position];
        int last = bag.Count - 1;
        bag[position] = bag[last];
        bag.RemoveAt(last);
        return value;
    }
}
