using System.IO;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using ViggiSlideshow.Core;

namespace ViggiSlideshow;

/// <summary>
/// Runs the slideshow on one screen. Whatever goes wrong with a file (it won't decode, VLC
/// reports an error, a video never starts or stalls, loading takes too long), the file is
/// logged, skipped for the rest of the run, and the show moves on. Nothing here shows a dialog.
/// </summary>
internal sealed class Slideshow : IDisposable
{
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
    private const int FailuresBeforePause = 25;
    private const string VideosOff = "videos are off here";

    private readonly SlideshowSurface _surface;
    private readonly Settings _settings;
    private readonly MediaLibrary _library;
    private readonly LibVLC? _vlc;
    private readonly bool _audio;
    private readonly bool _labels;
    private readonly int _pixelWidth, _pixelHeight;
    private readonly Playlist _playlist;
    private readonly Dispatcher _ui;

    private readonly DispatcherTimer _photoTimer;
    private readonly DispatcherTimer _watchdogTimer;
    private readonly DispatcherTimer _retryTimer;

    private VideoPlayback? _video;
    private PlaybackWatchdog? _watchdog;
    private VideoDetails? _videoDetails;
    private MediaInfo? _info;
    private Corner _corner = Corner.TopRight;
    private int _showToken;
    private int _direction = 1;
    private int _failuresInARow;
    private bool _paused;
    private bool _disposed;

    // Set when the chosen mode found nothing in the folder (videos only, with no videos):
    // this screen shows photos and videos until the mode is changed
    private bool _fellBack;

    public bool ShowDebug { get; set; }
    public bool ShowHelp { get; set; }
    public bool ShowSettings { get; set; }

    /// <summary>The file on screen now.</summary>
    public string? Current { get; private set; }

    /// <summary>A file reached the screen.</summary>
    public event Action<string>? FileShown;

    /// <summary>Where skipped files are kept across runs; null keeps no record.</summary>
    public SkippedFiles? Skipped { get; init; }

    /// <summary>A file was skipped because it couldn't be shown, with the reason.</summary>
    public event Action<string, string>? FileFailed;

    /// <param name="vlc">Null to skip videos (the settings preview, or VLC failed to load).</param>
    /// <param name="audio">Whether this screen may play sound; only the primary one does.</param>
    public Slideshow(SlideshowSurface surface, Settings settings, MediaLibrary library, LibVLC? vlc, bool audio,
        bool labels, int pixelWidth, int pixelHeight)
    {
        _surface = surface;
        _settings = settings;
        _library = library;
        _vlc = vlc;
        _audio = audio;
        _labels = labels;
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
        _ui = surface.Dispatcher;

        var fileSystem = new RealFileSystem();
        _playlist = new Playlist(library, start => new SequentialWalker(fileSystem, settings.Folder, start),
            settings.Random, settings.LastFile);

        _photoTimer = new DispatcherTimer();
        _photoTimer.Tick += (_, _) =>
        {
            _photoTimer.Stop();
            Advance(1);
        };

        _watchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _watchdogTimer.Tick += (_, _) => CheckVideo();

        _retryTimer = new DispatcherTimer();
        _retryTimer.Tick += (_, _) =>
        {
            _retryTimer.Stop();
            Advance(_direction);
        };
    }

    private VideoMode Mode => _vlc is null ? VideoMode.PhotosOnly : _fellBack ? VideoMode.Mixed : _settings.VideoMode;

    public void Start() => Advance(1);

    // ---- Navigation ----

    public void Advance(int direction)
    {
        if (_disposed)
            return;

        _photoTimer.Stop();
        _retryTimer.Stop();
        _direction = direction;

        string? path = direction < 0 ? _playlist.Previous(Mode) : _playlist.Next(Mode);

        if (path is null && direction < 0)
        {
            // Already at the oldest file this run has shown
            _surface.Flash("That's the first one");
            _direction = 1;
            RestartPhotoTimer();
            return;
        }

        if (path is null)
        {
            VideoMode mode = Mode;
            switch (EmptyShow.Decide(mode, canFallBack: _vlc is not null && !_fellBack, _settings.Random, _library.ScanComplete))
            {
                case EmptyShowAction.KeepLooking:
                    _surface.SetStatus($"Looking for {Describe(mode)} in {_settings.Folder}…");
                    Retry(TimeSpan.FromMilliseconds(300));
                    break;
                case EmptyShowAction.FallBackToMixed:
                    // The setting stays as chosen, so a folder that has videos gets them next run
                    _fellBack = true;
                    Log.Info($"No {Describe(mode)} to show in {_settings.Folder}; showing photos and videos this run");
                    _surface.Flash(EmptyShow.FallbackMessage(mode));
                    Retry(TimeSpan.Zero);
                    break;
                default:
                    _surface.SetStatus($"No {Describe(mode)} found in {_settings.Folder}");
                    Retry(TimeSpan.FromSeconds(10));
                    break;
            }
            return;
        }

        _ = ShowAsync(path);
    }

    private async Task ShowAsync(string path)
    {
        int token = ++_showToken;
        MediaKind kind = MediaFiles.Classify(path);
        Task<MediaInfo> infoTask = Task.Run(() => MediaInfoReader.Read(path));

        if (kind == MediaKind.Video)
        {
            bool gpu = await NeedsGpuScaling(path);
            if (token != _showToken || _disposed)
                return;
            StartVideo(path, gpu);
        }
        else
        {
            LoadedImage? image = null;
            string? error = null;
            try
            {
                Task<LoadedImage> load = Task.Run(() => ImageLoader.Load(path, _pixelWidth, _pixelHeight));
                if (await Task.WhenAny(load, Task.Delay(LoadTimeout)) == load)
                    image = await load;
                else
                    error = "took too long to load";
            }
            catch (Exception e)
            {
                error = $"{e.GetType().Name}: {e.Message}";
            }

            if (token != _showToken || _disposed)
                return;
            if (image is null)
            {
                Fail(path, error ?? "couldn't be read");
                return;
            }

            StopVideo();
            _surface.Show(image.Bitmap, image.Bitmap.PixelWidth, image.Bitmap.PixelHeight, 0, _settings.FadeMs);
            Shown(path);
            _info = null;
            UpdateLabel();
            RestartPhotoTimer();

            infoTask = infoTask.ContinueWith(t => t.Result with { Width = image.Width, Height = image.Height },
                TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        try
        {
            MediaInfo info = await infoTask;
            if (token == _showToken && !_disposed)
            {
                _info = _videoDetails is { } details ? WithVideo(info, details) : info;
                // A video still starting isn't on screen yet; its first frame updates the label
                if (Current == path)
                    UpdateLabel();
            }
        }
        catch (Exception e)
        {
            Log.Error($"Reading info for {path}", e);
        }
    }

    private static async Task<bool> NeedsGpuScaling(string path)
    {
        Task<(int Width, int Height)?> read = Task.Run(() => MediaInfoReader.ReadVideoSize(path));
        if (await Task.WhenAny(read, Task.Delay(ProbeTimeout)) != read)
            return false;
        return await read is { } size && FrameSize.NeedsGpuScaling(size.Width, size.Height);
    }

    /// <param name="gpu">Play it with Media Foundation, which shrinks frames on the graphics card, rather than VLC.</param>
    private void StartVideo(string path, bool gpu)
    {
        if (_vlc is null)
        {
            Fail(path, VideosOff);
            return;
        }

        // The previous picture stays up until this video's first frame arrives, so a slow
        // start never shows a black screen
        StopVideo();
        _info = null;
        _videoDetails = null;

        VideoPlayback video;
        try
        {
            bool mute = !_audio || _settings.Mute;
            video = gpu
                ? new GpuVideoPlayback(path, _pixelWidth, _pixelHeight, mute, _ui)
                : new VlcVideoPlayback(_vlc, path, _pixelWidth, _pixelHeight, _audio, mute, _ui);
        }
        catch (Exception e)
        {
            Fail(path, $"{e.GetType().Name}: {e.Message}");
            return;
        }

        _video = video;
        _watchdog = new PlaybackWatchdog(DateTime.UtcNow, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(_settings.VideoMaxSeconds));

        bool shown = false;
        video.FrameShown += () =>
        {
            if (_video != video || video.Bitmap is not { } bitmap)
                return;
            _watchdog?.FrameShown(DateTime.UtcNow);
            if (_surface.IsShowing(bitmap))
                return;

            var (width, height) = Geometry(bitmap.PixelWidth, bitmap.PixelHeight, _videoDetails);
            _surface.Show(bitmap, width, height, 0, shown ? 0 : _settings.FadeMs);
            if (!shown)
            {
                shown = true;
                Shown(path);
                UpdateLabel();
            }
        };
        video.DetailsKnown += details =>
        {
            if (_video != video)
                return;
            _videoDetails = details;
            Log.Info($"Video {details.Width}x{details.Height}, pixel aspect {details.PixelAspect:0.###}, rotation {details.Rotation}, {details.LengthMs / 1000.0:0.0} s");
            if (video.Bitmap is { } bitmap)
            {
                var (width, height) = Geometry(bitmap.PixelWidth, bitmap.PixelHeight, details);
                _surface.Reshape(bitmap, width, height, 0);
            }
            if (_info is not null)
            {
                _info = WithVideo(_info, details);
                UpdateLabel();
            }
        };
        video.Progress += time =>
        {
            if (_video == video)
                _watchdog?.Progress(time, DateTime.UtcNow);
        };
        video.Ended += () =>
        {
            if (_video == video)
                Advance(1);
        };
        video.Failed += reason =>
        {
            if (_video != video)
                return;
            if (gpu && !shown)
            {
                Log.Info($"Media Foundation couldn't start {path} ({reason}); trying VLC");
                StartVideo(path, gpu: false);
                return;
            }
            Fail(path, reason);
        };

        if (_paused)
            SetPaused(false);
        _watchdogTimer.Start();
        video.Start();
        Log.Info($"Playing {path}{(gpu ? " (Media Foundation, scaled on the graphics card)" : "")}");
    }

    private void CheckVideo()
    {
        if (_video is not { } video || _watchdog is not { } watchdog)
        {
            _watchdogTimer.Stop();
            return;
        }

        if (watchdog.Check(DateTime.UtcNow) is not { } stop)
            return;

        if (stop.Failed)
        {
            Fail(video.Path, stop.Reason);
        }
        else
        {
            Log.Info($"Moving on from {video.Path}: {stop.Reason}");
            Advance(1);
        }
    }

    private void Shown(string path)
    {
        Current = path;
        _failuresInARow = 0;
        _corner = _corner.Next();
        _surface.SetStatus(null);
        if (!_settings.Random)
            _settings.LastFile = path;
        FileShown?.Invoke(path);
    }

    private void Fail(string path, string reason)
    {
        Log.Error($"Skipping {path}: {reason}");
        if (_video?.Path == path)
            StopVideo();
        _playlist.MarkBad(path);
        FileFailed?.Invoke(path, reason);
        RecordSkipped(path, reason);

        // A run of failures usually means the drive went away; don't spin
        _failuresInARow++;
        if (_failuresInARow >= FailuresBeforePause)
        {
            _failuresInARow = 0;
            _surface.SetStatus($"Having trouble reading files in {_settings.Folder}");
            Retry(TimeSpan.FromSeconds(5));
        }
        else
        {
            Retry(TimeSpan.Zero);
        }
    }

    // Off the UI thread, since the file is rewritten under a lock other screensavers share. A file
    // that's gone (the drive went away, or it was moved) isn't broken, so it isn't listed; nor is
    // a video skipped only because videos are off.
    private void RecordSkipped(string path, string reason)
    {
        if (Skipped is not { } skipped || reason == VideosOff)
            return;
        Task.Run(() =>
        {
            if (File.Exists(path))
                skipped.Record(path, reason);
        });
    }

    private void Retry(TimeSpan delay)
    {
        _retryTimer.Stop();
        _retryTimer.Interval = delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(1);
        _retryTimer.Start();
    }

    private void StopVideo()
    {
        _watchdogTimer.Stop();
        _video?.Dispose();
        _video = null;
        _watchdog = null;
        _videoDetails = null;
    }

    private void RestartPhotoTimer()
    {
        _photoTimer.Stop();
        if (_paused || _video is not null)
            return;
        _photoTimer.Interval = TimeSpan.FromMilliseconds(_settings.IntervalMs);
        _photoTimer.Start();
    }

    // ---- Commands from the keyboard ----

    public bool Paused => _paused;

    public void SetPaused(bool paused)
    {
        _paused = paused;
        _video?.SetPaused(paused);
        _watchdog?.SetPaused(paused, DateTime.UtcNow);
        if (paused)
            _photoTimer.Stop();
        else
            RestartPhotoTimer();
    }

    public void SetRandom(bool random) => _playlist.Random = random;

    /// <summary>
    /// Hides a file for good: it leaves this screen's history, and if it's on screen here the show
    /// moves on at once. False when it was already hidden.
    /// </summary>
    public bool Hide(string path)
    {
        bool added = _playlist.Hide(path);
        if (string.Equals(Current, path, StringComparison.OrdinalIgnoreCase))
            Advance(1);
        return added;
    }

    public void ApplyMute() => _video?.SetMute(!_audio || _settings.Mute);

    public void ApplyInterval() => RestartPhotoTimer();

    /// <summary>Moves on when what's showing is no longer allowed.</summary>
    public void ApplyVideoMode()
    {
        _fellBack = false;
        if (Current is { } current && !MediaFiles.Matches(MediaFiles.Classify(current), Mode))
            Advance(1);
    }

    public void Flash(string message) => _surface.Flash(message);

    public void UpdateLabel()
    {
        if (!_labels)
            return;

        var options = new LabelOptions(_settings.ShowFileName, _settings.ShowInfo, ShowDebug, ShowHelp, ShowSettings);
        MediaInfo? info = _info ?? (Current is { } current ? new MediaInfo { Path = current, Kind = MediaFiles.Classify(current) } : null);
        if (info is not null && options.Info && _info is null)
            info = null; // keep the label steady until the details have been read

        string text = LabelText.Compose(options, info, _settings.Folder, DebugText, SettingsText);
        _surface.SetLabel(text, ShowHelp || ShowDebug || ShowSettings ? Corner.TopLeft : _corner, table: ShowHelp || ShowDebug || ShowSettings);
    }

    private string DebugText() => string.Join("\n",
        "Debug",
        $"Found:      {_library.ImageCount:N0} photos, {_library.VideoCount:N0} videos{(_library.ScanComplete ? "" : " (still looking)")}",
        $"History:    {_playlist.History.Count} shown, {_playlist.History.StepsBack} back",
        $"Screen:     {_pixelWidth} × {_pixelHeight}",
        $"Video:      {(_videoDetails is { } d ? $"{d.Width} × {d.Height}, pixel aspect {d.PixelAspect:0.###}, rotation {d.Rotation}" : "-")}",
        $"Log:        {Log.FilePath}");

    private string SettingsText() => string.Join("\n",
        "Settings",
        $"Folder:     {_settings.Folder}",
        $"Order:      {(_settings.Random ? "Random" : "Folder order")}",
        $"Showing:    {_settings.VideoMode.Describe()}{(_fellBack ? $" (none here, so {Mode.Describe().ToLowerInvariant()})" : "")}",
        $"Sound:      {(_settings.Mute ? "Off" : "On")}",
        $"Each photo: {_settings.IntervalMs / 1000.0:0.#} s",
        $"Videos:     up to {_settings.VideoMaxSeconds} s",
        $"Fade:       {_settings.FadeMs} ms");

    public void Dispose()
    {
        _disposed = true;
        _photoTimer.Stop();
        _retryTimer.Stop();
        StopVideo();
    }

    // ---- Helpers ----

    // VLC hands over frames already turned upright, so only non-square pixels need correcting
    private static (double Width, double Height) Geometry(int pixelWidth, int pixelHeight, VideoDetails? details)
    {
        double aspect = details?.PixelAspect is > 0.2 and < 5 ? details.PixelAspect : 1.0;
        return (pixelWidth * aspect, pixelHeight);
    }

    private static MediaInfo WithVideo(MediaInfo info, VideoDetails details)
    {
        bool sideways = details.Rotation is 90 or 270;
        return info with
        {
            Width = details.Width > 0 ? (sideways ? details.Height : details.Width) : info.Width,
            Height = details.Height > 0 ? (sideways ? details.Width : details.Height) : info.Height,
            Duration = details.LengthMs > 0 ? TimeSpan.FromMilliseconds(details.LengthMs) : info.Duration,
        };
    }

    private static string Describe(VideoMode mode) => mode switch
    {
        VideoMode.PhotosOnly => "photos",
        VideoMode.VideosOnly => "videos",
        _ => "photos or videos",
    };
}
