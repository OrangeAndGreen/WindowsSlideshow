using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ViggiSlideshow.Core;

namespace ViggiSlideshow;

/// <summary>
/// One video, decoded into memory and copied into a <see cref="WriteableBitmap"/>, so it is drawn
/// by WPF like a photo: no separate video window, nothing hidden behind it, and labels and fades
/// work the same way. <see cref="VlcVideoPlayback"/> plays most videos;
/// <see cref="GpuVideoPlayback"/> plays the ones too big for VLC to shrink in time.
///
/// The player hands frames over on a thread of its own; they are swapped into a ready buffer and
/// copied onto the bitmap on the UI thread. Player threads never wait on the UI thread (they only
/// post to it), and closing the player happens off the UI thread, so a stuck file can never freeze
/// the screen. Every event arrives on the UI thread, and none arrives after <see cref="Dispose"/>.
/// </summary>
internal abstract class VideoPlayback : IDisposable
{
    // About 1920x1080: plenty for a screensaver, and keeps the per-frame copy cheap on a 4K screen
    private const long MaxPixels = 2_100_000;

    private readonly Dispatcher _ui;
    private readonly int _maxWidth, _maxHeight;
    private readonly object _frameLock = new();

    // The player draws into _writeBuffer; a finished frame is swapped into _readyBuffer for the UI to copy
    private nint _writeBuffer, _readyBuffer;
    private int _width, _height, _pitch;
    private bool _frameReady, _copyQueued;

    private static readonly List<Task> Teardowns = new();
    private static readonly HashSet<VideoPlayback> Live = new();

    // For the stats logged when the video closes
    private long _handedOver, _drawn, _copyTicks, _playedMs;
    private int _mostPlaying;

    private volatile bool _disposed;
    private bool _firstFrameRaised;
    private bool _detailsRaised;

    public string Path { get; }
    public WriteableBitmap? Bitmap { get; private set; }

    /// <summary>A frame is on the bitmap; raised again if the bitmap is replaced mid-video.</summary>
    public event Action? FrameShown;
    public event Action? Ended;
    public event Action<string>? Failed;
    public event Action<long>? Progress;
    public event Action<VideoDetails>? DetailsKnown;

    protected VideoPlayback(string path, int maxWidth, int maxHeight, Dispatcher ui)
    {
        Path = path;
        _ui = ui;
        _maxWidth = maxWidth;
        _maxHeight = maxHeight;
    }

    protected bool IsDisposed => _disposed;

    /// <summary>Frame rate the file says it has, for the stats; 0 if unknown.</summary>
    protected double SourceFps { get; set; }

    public void Start()
    {
        lock (Live)
        {
            Live.Add(this);
            foreach (VideoPlayback video in Live)
                video._mostPlaying = Math.Max(video._mostPlaying, Live.Count);
        }
        Play();
    }

    public abstract void SetMute(bool mute);

    public abstract void SetPaused(bool paused);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        LogStats();
        lock (Live)
            Live.Remove(this);

        Task teardown = Close().ContinueWith(_ =>
        {
            // No frames arrive once the player has closed
            lock (_frameLock)
                FreeBuffers();
        }, TaskScheduler.Default);
        lock (Teardowns)
        {
            Teardowns.RemoveAll(t => t.IsCompleted);
            Teardowns.Add(teardown);
        }
    }

    /// <summary>Waits, up to the timeout, for every closed video to finish stopping.</summary>
    public static Task WaitForTeardowns(TimeSpan timeout)
    {
        Task[] pending;
        lock (Teardowns)
            pending = Teardowns.ToArray();
        return Task.WhenAny(Task.WhenAll(pending), Task.Delay(timeout));
    }

    protected abstract void Play();

    /// <summary>
    /// Stops and frees the player without blocking the calling (UI) thread; the task completes
    /// once no more frames can arrive.
    /// </summary>
    protected abstract Task Close();

    /// <summary>The player's own frame counts for the stats, if it keeps them.</summary>
    protected abstract (string Player, long? Decoded, long? Shown, long? Lost) PlayerStats();

    /// <summary>Called on the UI thread when the first frame is shown, for players that learn the details then.</summary>
    protected virtual void ReadDetails()
    {
    }

    // ---- Player threads ----

    /// <summary>
    /// Sizes the frames for a source of this size, scaled down to fit the screen, and allocates
    /// buffers for them. Returns the size the player should draw at.
    /// </summary>
    protected (int Width, int Height, int Pitch, int Rows) SetFrameSize(int sourceWidth, int sourceHeight)
    {
        var (w, h) = FrameSize.Fit(sourceWidth, sourceHeight, _maxWidth, _maxHeight, MaxPixels);
        int pitch = (w * 4 + 31) & ~31;
        int rows = (h + 15) & ~15;

        lock (_frameLock)
        {
            FreeBuffers();
            _writeBuffer = Marshal.AllocHGlobal(pitch * rows);
            _readyBuffer = Marshal.AllocHGlobal(pitch * rows);
            _width = w;
            _height = h;
            _pitch = pitch;
            _frameReady = false;
        }

        Post(() =>
        {
            Bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
            _firstFrameRaised = false;
        });
        return (w, h, pitch, rows);
    }

    /// <summary>The buffer the player draws the next frame into: BGRX rows of the pitch <see cref="SetFrameSize"/> returned.</summary>
    protected nint WriteBuffer
    {
        get
        {
            lock (_frameLock)
                return _writeBuffer;
        }
    }

    /// <summary>The player finished drawing into <see cref="WriteBuffer"/>; queue it for the screen.</summary>
    protected void FrameWritten()
    {
        bool queue = false;
        lock (_frameLock)
        {
            (_writeBuffer, _readyBuffer) = (_readyBuffer, _writeBuffer);
            _frameReady = true;
            _handedOver++;
            if (!_copyQueued)
            {
                _copyQueued = true;
                queue = true;
            }
        }
        if (queue)
            Post(CopyFrame, DispatcherPriority.Render);
    }

    protected void RaiseEnded() => Post(() => Ended?.Invoke());

    protected void RaiseFailed(string reason) => Post(() => Failed?.Invoke(reason));

    protected void RaiseProgress(long timeMs) => Post(() =>
    {
        _playedMs = timeMs;
        Progress?.Invoke(timeMs);
    });

    /// <summary>Raises <see cref="DetailsKnown"/> once; called on the UI thread.</summary>
    protected void RaiseDetails(VideoDetails details)
    {
        if (_detailsRaised)
            return;
        _detailsRaised = true;
        DetailsKnown?.Invoke(details);
    }

    protected bool DetailsRaised => _detailsRaised;

    protected void Post(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        if (_disposed)
            return;
        _ui.BeginInvoke(priority, () =>
        {
            if (!_disposed)
                action();
        });
    }

    // ---- UI thread ----

    private void CopyFrame()
    {
        if (_disposed || Bitmap is not { } bitmap)
        {
            lock (_frameLock)
                _copyQueued = false;
            return;
        }

        lock (_frameLock)
        {
            _copyQueued = false;
            if (!_frameReady || bitmap.PixelWidth != _width || bitmap.PixelHeight != _height || _readyBuffer == 0)
                return;
            _frameReady = false;

            long started = Stopwatch.GetTimestamp();
            bitmap.Lock();
            try
            {
                int rowBytes = _width * 4;
                unsafe
                {
                    byte* source = (byte*)_readyBuffer;
                    byte* target = (byte*)bitmap.BackBuffer;
                    for (int row = 0; row < _height; row++)
                        Buffer.MemoryCopy(source + (long)row * _pitch, target + (long)row * bitmap.BackBufferStride, rowBytes, rowBytes);
                }
                bitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
            }
            finally
            {
                bitmap.Unlock();
                _drawn++;
                _copyTicks += Stopwatch.GetTimestamp() - started;
            }
        }

        if (!_firstFrameRaised)
        {
            _firstFrameRaised = true;
            ReadDetails();
            FrameShown?.Invoke();
        }
    }

    private void LogStats()
    {
        try
        {
            long handedOver;
            lock (_frameLock)
                handedOver = _handedOver;
            if (handedOver == 0)
                return;
            var (player, decoded, shown, lost) = PlayerStats();
            var stats = new VideoStats(player, SourceFps, _playedMs, decoded, shown, lost,
                handedOver, _drawn, Stopwatch.GetElapsedTime(0, _copyTicks).TotalMilliseconds, _mostPlaying);
            Log.Info($"Frames for {Path}: {stats.Describe()}");
        }
        catch (Exception e)
        {
            Log.Error($"Reading video stats for {Path}", e);
        }
    }

    private void FreeBuffers()
    {
        if (_writeBuffer != 0) Marshal.FreeHGlobal(_writeBuffer);
        if (_readyBuffer != 0) Marshal.FreeHGlobal(_readyBuffer);
        _writeBuffer = _readyBuffer = 0;
    }
}

/// <param name="PixelAspect">Width of a pixel relative to its height; not 1 for DV and DVD video.</param>
/// <param name="Rotation">
/// Degrees the file says to turn the picture. VLC has already turned the frames it hands over,
/// so this only matters for reporting the size as it appears.
/// </param>
internal sealed record VideoDetails(int Width, int Height, double PixelAspect, int Rotation, long LengthMs);
