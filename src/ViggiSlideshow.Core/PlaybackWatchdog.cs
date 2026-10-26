namespace ViggiSlideshow.Core;

/// <summary>Why a video is being stopped. <see cref="Failed"/> is false for one that simply ran its course.</summary>
public sealed record WatchdogStop(string Reason, bool Failed);

/// <summary>
/// Decides when a video has to be abandoned, so a file that never starts, stalls part way or
/// never reports its end can't leave the screen stuck. The player's end and error events
/// are the normal way out; this is the backstop.
/// </summary>
public sealed class PlaybackWatchdog
{
    private readonly TimeSpan _startTimeout;
    private readonly TimeSpan _stallTimeout;
    private readonly long _maxLengthMs;
    private readonly TimeSpan _wallLimit;

    private DateTime _lastProgressAt;
    private TimeSpan _playedBefore;  // wall time played before the last pause
    private DateTime _playingSince;
    private long _lastTimeMs = -1;
    private bool _started;
    private bool _paused;

    public PlaybackWatchdog(DateTime now, TimeSpan startTimeout, TimeSpan stallTimeout, TimeSpan maxLength)
    {
        _startTimeout = startTimeout;
        _stallTimeout = stallTimeout;
        _maxLengthMs = (long)maxLength.TotalMilliseconds;
        // A video that decodes slower than real time never reaches the length cap by its own
        // clock, so the cap also applies to the time actually spent on it, with some slack
        _wallLimit = maxLength * 1.5 + TimeSpan.FromSeconds(10);
        _lastProgressAt = now;
        _playingSince = now;
    }

    /// <summary>The player reported a position. Only a position that moved counts as progress.</summary>
    public void Progress(long timeMs, DateTime now)
    {
        if (timeMs > _lastTimeMs)
        {
            _lastTimeMs = timeMs;
            _lastProgressAt = now;
            _started = true;
        }
    }

    /// <summary>A frame reached the screen.</summary>
    public void FrameShown(DateTime now)
    {
        if (!_started)
        {
            _started = true;
            _lastProgressAt = now;
        }
    }

    public void SetPaused(bool paused, DateTime now)
    {
        if (!_paused && paused)
        {
            _playedBefore += now - _playingSince;
        }
        else if (_paused && !paused)
        {
            // Paused time doesn't count as stalled, or towards the limit
            _lastProgressAt = now;
            _playingSince = now;
        }
        _paused = paused;
    }

    /// <summary>Why the video should be stopped now, or null to keep playing.</summary>
    public WatchdogStop? Check(DateTime now)
    {
        if (_paused)
            return null;
        if (_lastTimeMs >= _maxLengthMs)
            return new WatchdogStop("reached the longest video length", Failed: false);
        if (_playedBefore + (now - _playingSince) > _wallLimit)
            return new WatchdogStop("took too long, playing slower than real time", Failed: false);
        if (!_started && now - _lastProgressAt > _startTimeout)
            return new WatchdogStop("didn't start playing", Failed: true);
        if (_started && now - _lastProgressAt > _stallTimeout)
            return new WatchdogStop("stopped making progress", Failed: true);
        return null;
    }
}
