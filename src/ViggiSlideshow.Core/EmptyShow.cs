namespace ViggiSlideshow.Core;

/// <summary>What a screen does when the playlist has nothing to show.</summary>
public enum EmptyShowAction
{
    /// <summary>The scan may still find something: wait a moment and try again.</summary>
    KeepLooking,

    /// <summary>The mode allows nothing in the folder: show photos and videos for the rest of the run.</summary>
    FallBackToMixed,

    /// <summary>There is nothing at all to show: say so and check again now and then.</summary>
    NothingAtAll,
}

public static class EmptyShow
{
    /// <param name="mode">The mode the screen is showing.</param>
    /// <param name="canFallBack">False when the mode is forced (no VLC means photos only) or the screen has already fallen back.</param>
    /// <param name="random">Random draws only see what the scan has found so far; folder order has walked the whole tree.</param>
    public static EmptyShowAction Decide(VideoMode mode, bool canFallBack, bool random, bool scanComplete)
    {
        if (random && !scanComplete)
            return EmptyShowAction.KeepLooking;
        if (mode != VideoMode.Mixed && canFallBack)
            return EmptyShowAction.FallBackToMixed;
        return EmptyShowAction.NothingAtAll;
    }

    /// <summary>The message shown when a screen falls back to the mixed show.</summary>
    public static string FallbackMessage(VideoMode mode) => mode switch
    {
        VideoMode.PhotosOnly => "No photos here, showing photos and videos",
        VideoMode.VideosOnly => "No videos here, showing photos and videos",
        _ => "Showing photos and videos",
    };
}
