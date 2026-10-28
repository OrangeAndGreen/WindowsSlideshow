using System.Runtime.InteropServices;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using ViggiSlideshow.Core;

namespace ViggiSlideshow;

/// <summary>
/// A video played by VLC into memory. VLC's callbacks and events never call back into VLC (they
/// only post to the UI thread), and stopping and freeing the player happens on a background thread.
/// </summary>
internal sealed class VlcVideoPlayback : VideoPlayback
{
    private readonly Media _media;
    private readonly MediaPlayer _player;

    // Kept in fields so the garbage collector can't free delegates VLC still holds
    private readonly MediaPlayer.LibVLCVideoFormatCb _formatCallback;
    private readonly MediaPlayer.LibVLCVideoCleanupCb _cleanupCallback;
    private readonly MediaPlayer.LibVLCVideoLockCb _lockCallback;
    private readonly MediaPlayer.LibVLCVideoDisplayCb _displayCallback;

    private bool _mute;

    public VlcVideoPlayback(LibVLC vlc, string path, int maxWidth, int maxHeight, bool audio, bool mute, Dispatcher ui)
        : base(path, maxWidth, maxHeight, ui)
    {
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

    protected override void Play()
    {
        if (!_player.Play())
            RaiseFailed("VLC wouldn't start it");
    }

    public override void SetMute(bool mute)
    {
        _mute = mute;
        if (!IsDisposed)
            _player.Mute = mute;
    }

    public override void SetPaused(bool paused)
    {
        if (!IsDisposed)
            _player.SetPause(paused);
    }

    protected override Task Close()
    {
        _player.EndReached -= OnEndReached;
        _player.EncounteredError -= OnError;
        _player.TimeChanged -= OnTimeChanged;
        _player.Playing -= OnPlaying;

        // Stop can block for a while on a bad file, so it never runs on the UI thread
        MediaPlayer player = _player;
        Media media = _media;
        return Task.Run(() =>
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
        });
    }

    protected override (string Player, long? Decoded, long? Shown, long? Lost) PlayerStats()
    {
        MediaStats stats = _media.Statistics;
        return ("VLC", stats.DecodedVideo, stats.DisplayedPictures, stats.LostPictures);
    }

    // ---- VLC threads ----

    private uint OnFormat(ref nint opaque, nint chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        var (w, h, pitch, rows) = SetFrameSize((int)width, (int)height);

        Marshal.Copy("RV32"u8.ToArray(), 0, chroma, 4);
        width = (uint)w;
        height = (uint)h;
        pitches = (uint)pitch;
        lines = (uint)rows;
        return 1;
    }

    private void OnCleanup(ref nint opaque)
    {
    }

    private nint OnLock(nint opaque, nint planes)
    {
        Marshal.WriteIntPtr(planes, WriteBuffer);
        return 0;
    }

    private void OnDisplay(nint opaque, nint picture) => FrameWritten();

    private void OnEndReached(object? sender, EventArgs e) => RaiseEnded();

    private void OnError(object? sender, EventArgs e) => RaiseFailed("VLC couldn't play it");

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e) => RaiseProgress(e.Time);

    private void OnPlaying(object? sender, EventArgs e) => Post(() =>
    {
        // VLC can drop a mute set before its audio output exists, so set it again now
        _player.Mute = _mute;
        ReadDetails();
    });

    // ---- UI thread ----

    protected override void ReadDetails()
    {
        if (IsDisposed || DetailsRaised)
            return;
        try
        {
            MediaTrack[] tracks = _media.Tracks;
            long length = _player.Length;
            int index = Array.FindIndex(tracks, t => t.TrackType == TrackType.Video);
            if (index >= 0 && tracks[index].Data.Video is { Width: > 0 } video)
            {
                if (video.FrameRateNum > 0 && video.FrameRateDen > 0)
                    SourceFps = (double)video.FrameRateNum / video.FrameRateDen;
                double aspect = video.SarNum > 0 && video.SarDen > 0 ? (double)video.SarNum / video.SarDen : 1.0;
                RaiseDetails(new VideoDetails((int)video.Width, (int)video.Height, aspect, Rotation(video.Orientation), length));
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
}
