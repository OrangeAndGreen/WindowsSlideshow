namespace ViggiSlideshow.Core;

/// <summary>Where settings live: the registry in the app, a dictionary in tests.</summary>
public interface ISettingsStore
{
    string? GetString(string name);
    int? GetInt(string name);
    void SetString(string name, string value);
    void SetInt(string name, int value);
}

/// <summary>
/// The screensaver's settings. The value names match the old WinForms version's registry
/// values, so its settings carry over.
/// </summary>
public sealed class Settings
{
    public const int DefaultIntervalMs = 5000;
    public const int DefaultVideoMaxSeconds = 120;
    public const int DefaultFadeMs = 600;

    public string Folder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    public bool ShowFileName { get; set; }
    public bool ShowInfo { get; set; }
    public bool Random { get; set; } = true;
    public int IntervalMs { get; set; } = DefaultIntervalMs;
    public bool Mute { get; set; }
    public VideoMode VideoMode { get; set; } = VideoMode.Mixed;
    public int VideoMaxSeconds { get; set; } = DefaultVideoMaxSeconds;
    public int FadeMs { get; set; } = DefaultFadeMs;

    /// <summary>The last file shown in sequential order, where the next run carries on.</summary>
    public string? LastFile { get; set; }

    public static Settings Load(ISettingsStore store)
    {
        var settings = new Settings();

        if (store.GetString("Directory") is { Length: > 0 } folder)
            settings.Folder = folder;
        settings.ShowFileName = ReadBool(store, "Label", settings.ShowFileName);
        settings.ShowInfo = ReadBool(store, "Info", settings.ShowInfo);
        settings.Random = ReadBool(store, "Random", settings.Random);
        settings.IntervalMs = Clamp(store.GetInt("Interval"), 500, 3_600_000, DefaultIntervalMs);
        settings.Mute = ReadBool(store, "Mute", settings.Mute);

        // The old version only had a videos-only switch
        if (Enum.TryParse(store.GetString("Videos"), out VideoMode mode) && Enum.IsDefined(mode))
            settings.VideoMode = mode;
        else if (ReadBool(store, "VideosOnly", false))
            settings.VideoMode = VideoMode.VideosOnly;

        settings.VideoMaxSeconds = Clamp(store.GetInt("VideoMaxSeconds"), 5, 3600, DefaultVideoMaxSeconds);
        settings.FadeMs = Clamp(store.GetInt("FadeMs"), 0, 5000, DefaultFadeMs);
        settings.LastFile = store.GetString("LastFile") is { Length: > 0 } last ? last : null;
        return settings;
    }

    public void Save(ISettingsStore store)
    {
        store.SetString("Directory", Folder);
        store.SetString("Label", ShowFileName.ToString());
        store.SetString("Info", ShowInfo.ToString());
        store.SetString("Random", Random.ToString());
        store.SetInt("Interval", IntervalMs);
        store.SetString("Mute", Mute.ToString());
        store.SetString("Videos", VideoMode.ToString());
        store.SetString("VideosOnly", (VideoMode == VideoMode.VideosOnly).ToString());
        store.SetInt("VideoMaxSeconds", VideoMaxSeconds);
        store.SetInt("FadeMs", FadeMs);
        store.SetString("LastFile", LastFile ?? "");
    }

    /// <summary>The interval a number key sets: 1-5 seconds, 6-9 for 10/20/30/60, 0 for the default.</summary>
    public static int IntervalForDigit(int digit) => digit switch
    {
        >= 1 and <= 5 => digit * 1000,
        6 => 10_000,
        7 => 20_000,
        8 => 30_000,
        9 => 60_000,
        _ => DefaultIntervalMs,
    };

    private static bool ReadBool(ISettingsStore store, string name, bool fallback) =>
        bool.TryParse(store.GetString(name), out bool value) ? value : fallback;

    private static int Clamp(int? value, int min, int max, int fallback) =>
        value is { } v ? Math.Clamp(v, min, max) : fallback;
}
