using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using ViggiSlideshow.Core;

namespace ViggiSlideshow;

/// <summary>
/// One video, played by VLC into memory and copied into a <see cref="WriteableBitmap"/>, so it
/// is drawn by WPF like a photo: no separate video window, nothing hidden behind it, and labels
/// and fades work the same way.
///
/// The rules that keep it from hanging: VLC's callbacks and events never call back into VLC and
/// never wait on the UI thread (they only post to it), and stopping and freeing the player
/// happens on a background thread, so a stuck file can never freeze the screen. Every event
/// arrives on the UI thread, and none arrives after <see cref="Dispose"/>.
/// </summary>
internal sealed class VideoPlayback : IDisposable
{
    // About 1920x1080: plenty for a screensaver, and keeps the per-frame copy cheap on a 4K screen
    private const long MaxPixels = 2_100_000;

    private readonly Dispatcher _ui;
    private readonly Media _media;
    private readonly MediaPlayer _player;
    private readonly int _maxWidth, _maxHeight;
    private readonly object _frameLock = new();

    // Kept in fields so the garbage collector can't free delegates VLC still holds
    private readonly MediaPlayer.LibVLCVideoFormatCb _formatCallback;
    private readonly MediaPlayer.LibVLCVideoCleanupCb _cleanupCallback;
    private readonly MediaPlayer.LibVLCVideoLockCb _lockCallback;
    private readonly MediaPlayer.LibVLCVideoDisplayCb _displayCallback;

    // VLC draws into _writeBuffer; a finished frame is swapped into _readyBuffer for the UI to copy
    private nint _writeBuffer, _readyBuffer;
    private int _width, _height, _pitch;
    private bool _frameReady, _copyQueued;

    private static readonly List<Task> Teardowns = new();

    private volatile bool _disposed;
    private bool _mute;
    private bool _firstFrameRaised;
    private bool _infoRaised;

    public string Path { get; }
    public WriteableBitmap? Bitmap { get; private set; }

    /// <summary>A frame is on the bitmap; raised again if the bitmap is replaced mid-video.</summary>
    public event Action? FrameShown;
    public event Action? Ended;
    public event Action<string>? Failed;
    public event Action<long>? Progress;
    public event Action<VideoDetails>? DetailsKnown;

    public VideoPlayback(LibVLC vlc, string path, int maxWidth, int maxHeight, bool audio, bool mute, Dispatcher ui)
    {
        Path = path;
        _ui = ui;
        _maxWidth = maxWidth;
        _maxHeight = maxHeight;

        _media = audio ? new Media(vlc, path, FromType.FromPath) : new Media(vlc, path, FromType.FromPath, ":no-audio");
        _mute = mute;
        // Hardware decoding, copied back to memory: 4K HEVC from GoPros and drones is far too slow without it
        _player = new MediaPlayer(_media) { Mute = mute, EnableHardwareDecoding = true };

        _formatCallback = OnFormat;
        _cleanupCallback = OnCleanup;
        _lockCallback = OnLock;
        _displayCallback = OnDisplay;
        _player.SetVideoFormatCallbacks(_formatCallback, _cleanupCallback);
        _player.SetVideoCallbacks(_lockCallback, null, _displayCallback);

        _player.EndReached += OnEndReached;
        _player.EncounteredError += OnError;
        _player.TimeChanged += OnTimeChanged;
        _player.Playing += OnPlaying;
    }

    public void Start()
    {
        if (!_player.Play())
            Post(() => Failed?.Invoke("VLC wouldn't start it"));
    }

    public void SetMute(bool mute)
    {
        _mute = mute;
        if (!_disposed)
            _player.Mute = mute;
    }

    public void SetPaused(bool paused)
    {
        if (!_disposed)
            _player.SetPause(paused);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _player.EndReached -= OnEndReached;
        _player.EncounteredError -= OnError;
        _player.TimeChanged -= OnTimeChanged;
        _player.Playing -= OnPlaying;

        // Stop can block for a while on a bad file, so it never runs on the UI thread
        MediaPlayer player = _player;
        Media media = _media;
        Task teardown = Task.Run(() =>
        {
            try
            {
                player.Stop();
                player.Dispose();
                media.Dispose();
            }
            catch (Exception e)
            {
                Log.Error($"Closing video {Path}", e);
            }
            finally
            {
                // No callbacks run once Stop has returned
                lock (_frameLock)
                    FreeBuffers();
            }
        });
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

    // ---- VLC threads ----

    private uint OnFormat(ref nint opaque, nint chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        var (w, h) = FrameSize.Fit((int)width, (int)height, _maxWidth, _maxHeight, MaxPixels);
        int pitch = (w * 4 + 31) & ~31;
        int rows = (h + 15) & ~15;

        Marshal.Copy("RV32"u8.ToArray(), 0, chroma, 4);
        width = (uint)w;
        height = (uint)h;
        pitches = (uint)pitch;
        lines = (uint)rows;

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
        return 1;
    }

    private void OnCleanup(ref nint opaque)
    {
    }

    private nint OnLock(nint opaque, nint planes)
    {
        lock (_frameLock)
            Marshal.WriteIntPtr(planes, _writeBuffer);
        return 0;
    }

    private void OnDisplay(nint opaque, nint picture)
    {
        bool queue = false;
        lock (_frameLock)
        {
            (_writeBuffer, _readyBuffer) = (_readyBuffer, _writeBuffer);
            _frameReady = true;
            if (!_copyQueued)
            {
                _copyQueued = true;
                queue = true;
            }
        }
        if (queue)
            Post(CopyFrame, DispatcherPriority.Render);
    }

    private void OnEndReached(object? sender, EventArgs e) => Post(() => Ended?.Invoke());

    private void OnError(object? sender, EventArgs e) => Post(() => Failed?.Invoke("VLC couldn't play it"));

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        long time = e.Time;
        Post(() => Progress?.Invoke(time));
    }

    private void OnPlaying(object? sender, EventArgs e) => Post(() =>
    {
        // VLC can drop a mute set before its audio output exists, so set it again now
        _player.Mute = _mute;
        ReadDetails();
    });

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
            }
        }

        if (!_firstFrameRaised)
        {
            _firstFrameRaised = true;
            ReadDetails();
            FrameShown?.Invoke();
        }
    }

    private void ReadDetails()
    {
        if (_disposed || _infoRaised)
            return;
        try
        {
            MediaTrack[] tracks = _media.Tracks;
            long length = _player.Length;
            int index = Array.FindIndex(tracks, t => t.TrackType == TrackType.Video);
            if (index >= 0 && tracks[index].Data.Video is { Width: > 0 } video)
            {
                _infoRaised = true;
                double aspect = video.SarNum > 0 && video.SarDen > 0 ? (double)video.SarNum / video.SarDen : 1.0;
                DetailsKnown?.Invoke(new VideoDetails((int)video.Width, (int)video.Height, aspect, Rotation(video.Orientation), length));
            }
        }
        catch (Exception e)
        {
            Log.Error($"Reading video details for {Path}", e);
        }
    }

    private static int Rotation(VideoOrientation orientation) => orientation switch
    {
        VideoOrientation.RightTop => 90,
        VideoOrientation.BottomRight => 180,
        VideoOrientation.LeftBottom => 270,
        _ => 0,
    };

    private void Post(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        if (_disposed)
            return;
        _ui.BeginInvoke(priority, () =>
        {
            if (!_disposed)
                action();
        });
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
