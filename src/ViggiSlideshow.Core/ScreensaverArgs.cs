namespace ViggiSlideshow.Core;

public enum ScreensaverMode { Show, Preview, Configure }

/// <summary>
/// What Windows asked for: <c>/s</c> to run, <c>/p hwnd</c> to draw in the settings dialog's
/// preview, <c>/c</c> or <c>/c:hwnd</c> for settings. Windows varies the case and sometimes
/// joins the window handle with a colon.
/// </summary>
public readonly record struct ScreensaverArgs(ScreensaverMode Mode, nint Window = 0)
{
    public static ScreensaverArgs Parse(IReadOnlyList<string> args)
    {
        // Run with no arguments, as from a build or a double-click, it just runs
        if (args.Count == 0)
            return new ScreensaverArgs(ScreensaverMode.Show);

        string first = args[0].Trim();
        if (first.Length < 2 || (first[0] != '/' && first[0] != '-'))
            return new ScreensaverArgs(ScreensaverMode.Show);

        string rest = first.Length > 2 && first[2] == ':' ? first[3..] : (args.Count > 1 ? args[1] : "");
        nint window = long.TryParse(rest.Trim(), out long handle) ? (nint)handle : 0;

        return char.ToLowerInvariant(first[1]) switch
        {
            // A preview with no window to draw in has nothing to do; the app exits on Window == 0
            'p' => new ScreensaverArgs(ScreensaverMode.Preview, window),
            'c' => new ScreensaverArgs(ScreensaverMode.Configure, window),
            _ => new ScreensaverArgs(ScreensaverMode.Show),
        };
    }
}
