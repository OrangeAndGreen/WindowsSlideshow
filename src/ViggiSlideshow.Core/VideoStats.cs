namespace ViggiSlideshow.Core;

/// <summary>
/// How smoothly one video played, logged when it closes, to find where frames go missing.
/// The player's own counts, where it keeps them, say whether decoding and scaling kept up; the
/// handed-over and drawn counts say whether the UI thread kept up with the frames it was given.
/// </summary>
/// <param name="Player">What played it: VLC, or Media Foundation for videos scaled on the graphics card.</param>
/// <param name="SourceFps">Frame rate the file says it has; 0 if unknown.</param>
/// <param name="PlayedMs">How far into the video playback got.</param>
/// <param name="Decoded">Frames the player decoded; null if it doesn't count them.</param>
/// <param name="PlayerShown">Frames the player sent on to be shown.</param>
/// <param name="PlayerLost">Frames the player dropped for being late.</param>
/// <param name="HandedOver">Frames the player put in our buffer.</param>
/// <param name="Drawn">Frames copied onto the screen's bitmap; fewer than handed over means the UI thread fell behind.</param>
/// <param name="CopyMs">Total time spent copying frames on the UI thread.</param>
/// <param name="MostPlaying">Most videos playing at once, across all screens, while this one played.</param>
public sealed record VideoStats(
    string Player, double SourceFps, long PlayedMs, long? Decoded, long? PlayerShown, long? PlayerLost,
    long HandedOver, long Drawn, double CopyMs, int MostPlaying)
{
    /// <summary>Frames that should have been shown for the time played; 0 if the frame rate is unknown.</summary>
    public long Expected => SourceFps > 0 ? (long)Math.Round(SourceFps * PlayedMs / 1000.0) : 0;

    /// <summary>Frames drawn per second of video played.</summary>
    public double DrawnFps => PlayedMs > 0 ? Drawn * 1000.0 / PlayedMs : 0;

    public string Describe() =>
        $"{SourceFps:0.##} fps source, played {PlayedMs / 1000.0:0.0} s, expected {Expected}; " +
        (Decoded is null ? $"{Player}; " : $"{Player} decoded {Decoded}, showed {PlayerShown}, lost {PlayerLost}; ") +
        $"handed over {HandedOver}, drawn {Drawn} ({DrawnFps:0.#} fps), " +
        $"copy {(Drawn > 0 ? CopyMs / Drawn : 0):0.0} ms avg; " +
        $"{MostPlaying} playing at most";
}
