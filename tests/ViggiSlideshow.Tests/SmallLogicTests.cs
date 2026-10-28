using ViggiSlideshow.Core;

namespace ViggiSlideshow.Tests;

public class MediaFilesTests
{
    [Theory]
    [InlineData(@"C:\p\a.JPG", MediaKind.Image)]
    [InlineData(@"C:\p\a.heic", MediaKind.Image)]
    [InlineData(@"C:\p\a.MOV", MediaKind.Video)]
    [InlineData(@"C:\p\a.mpg", MediaKind.Video)]
    [InlineData(@"C:\p\a.thm", MediaKind.None)]
    [InlineData(@"C:\p\._a.jpg", MediaKind.None)]
    [InlineData(@"C:\p\noextension", MediaKind.None)]
    public void Classifies_by_extension(string path, MediaKind expected) =>
        Assert.Equal(expected, MediaFiles.Classify(path));

    [Fact]
    public void Video_mode_cycles_through_all_three()
    {
        Assert.Equal(VideoMode.PhotosOnly, VideoMode.Mixed.Next());
        Assert.Equal(VideoMode.VideosOnly, VideoMode.PhotosOnly.Next());
        Assert.Equal(VideoMode.Mixed, VideoMode.VideosOnly.Next());
    }
}

public class NaturalComparerTests
{
    [Fact]
    public void Sorts_numbers_by_value_and_letters_without_case()
    {
        string[] names = ["IMG_10.jpg", "img_2.jpg", "IMG_1.jpg", "b.jpg", "A.jpg", "IMG_002.jpg"];

        Assert.Equal(["A.jpg", "b.jpg", "IMG_1.jpg", "IMG_002.jpg", "img_2.jpg", "IMG_10.jpg"],
            names.Order(NaturalComparer.Instance));
    }
}

public class SettingsTests
{
    private sealed class DictionaryStore : ISettingsStore
    {
        public readonly Dictionary<string, object> Values = new();
        public string? GetString(string name) => Values.TryGetValue(name, out var v) ? v as string : null;
        public int? GetInt(string name) => Values.TryGetValue(name, out var v) && v is int i ? i : null;
        public void SetString(string name, string value) => Values[name] = value;
        public void SetInt(string name, int value) => Values[name] = value;
    }

    [Fact]
    public void Reads_the_old_versions_registry_values()
    {
        var store = new DictionaryStore();
        store.SetString("Directory", @"H:\Pictures\For Slideshow\Recent Events");
        store.SetString("Label", "False");
        store.SetString("Info", "True");
        store.SetString("Random", "True");
        store.SetInt("Interval", 20000);
        store.SetString("Mute", "False");
        store.SetString("VideosOnly", "True");

        var settings = Settings.Load(store);

        Assert.Equal(@"H:\Pictures\For Slideshow\Recent Events", settings.Folder);
        Assert.False(settings.ShowFileName);
        Assert.True(settings.ShowInfo);
        Assert.Equal(20000, settings.IntervalMs);
        Assert.Equal(VideoMode.VideosOnly, settings.VideoMode);
        Assert.Equal(Settings.DefaultVideoMaxSeconds, settings.VideoMaxSeconds);
    }

    [Fact]
    public void Round_trips_and_clamps()
    {
        var store = new DictionaryStore();
        new Settings { Folder = @"D:\x", VideoMode = VideoMode.PhotosOnly, Mute = true, IntervalMs = 7000, LastFile = @"D:\x\1.jpg" }.Save(store);
        store.SetInt("FadeMs", 999_999);

        var settings = Settings.Load(store);

        Assert.Equal(@"D:\x", settings.Folder);
        Assert.Equal(VideoMode.PhotosOnly, settings.VideoMode);
        Assert.True(settings.Mute);
        Assert.Equal(7000, settings.IntervalMs);
        Assert.Equal(5000, settings.FadeMs);
        Assert.Equal(@"D:\x\1.jpg", settings.LastFile);
    }

    [Theory]
    [InlineData(1, 1000)]
    [InlineData(5, 5000)]
    [InlineData(6, 10000)]
    [InlineData(9, 60000)]
    [InlineData(0, Settings.DefaultIntervalMs)]
    public void Number_keys_set_the_interval(int digit, int expectedMs) =>
        Assert.Equal(expectedMs, Settings.IntervalForDigit(digit));
}

public class KeyMapTests
{
    [Theory]
    [InlineData("Right", Command.Next)]
    [InlineData("B", Command.Previous)]
    [InlineData("Return", Command.OpenFolder)]
    [InlineData("Escape", Command.Quit)]
    [InlineData("V", Command.CycleVideos)]
    [InlineData("M", Command.ToggleMute)]
    [InlineData("LeftShift", Command.None)]
    public void Maps_keys(string key, Command expected) => Assert.Equal(expected, KeyMap.Map(key).Command);

    [Theory]
    [InlineData("D7", 7)]
    [InlineData("NumPad3", 3)]
    public void Digits_set_the_interval(string key, int digit) =>
        Assert.Equal(new KeyCommand(Command.SetInterval, digit), KeyMap.Map(key));

    [Fact]
    public void Help_lists_every_command()
    {
        string help = LabelText.HelpText();
        foreach (var (keys, description) in KeyMap.Help)
            Assert.Contains(description, help);
    }
}

public class LabelTextTests
{
    private static readonly MediaInfo Photo = new()
    {
        Path = @"H:\Pics\2019\Beach\IMG_1.jpg",
        Kind = MediaKind.Image,
        Bytes = 2_500_000,
        Width = 4000,
        Height = 3000,
        Taken = new DateTime(2019, 7, 4, 14, 22, 0),
        Camera = "Canon EOS 5D",
        FilesInFolder = 12,
    };

    [Fact]
    public void File_name_alone_is_relative_to_the_slideshow_folder()
    {
        string text = LabelText.Compose(new LabelOptions(true, false, false, false, false), Photo, @"H:\Pics");

        Assert.Equal(@"2019\Beach\IMG_1.jpg", text);
    }

    [Fact]
    public void Info_adds_size_date_camera_and_folder_count()
    {
        string text = LabelText.Compose(new LabelOptions(false, true, false, false, false), Photo, @"H:\Pics");

        Assert.Contains("4000 × 3000", text);
        Assert.Contains("2.4 MB", text);
        Assert.Contains("2019", text);
        Assert.Contains("Canon EOS 5D", text);
        Assert.Contains("12 files in this folder", text);
    }

    [Fact]
    public void Nothing_to_show_gives_empty_text()
    {
        Assert.Equal("", LabelText.Compose(new LabelOptions(false, false, false, false, false), Photo, @"H:\Pics"));
    }

    [Theory]
    [InlineData("Canon", "Canon EOS 5D", "Canon EOS 5D")]
    [InlineData("NIKON CORPORATION", "NIKON D90", "NIKON D90")]
    [InlineData("Apple", "iPhone 12", "Apple iPhone 12")]
    [InlineData(null, "iPhone 12", "iPhone 12")]
    [InlineData("Sony", null, "Sony")]
    [InlineData(null, null, null)]
    public void Camera_name_doesnt_repeat_the_make(string? make, string? model, string? expected) =>
        Assert.Equal(expected, LabelText.CameraName(make, model));

    [Fact]
    public void Corners_cycle()
    {
        Assert.Equal(Corner.BottomLeft, Corner.TopLeft.Next());
        Assert.Equal(Corner.TopLeft, Corner.TopRight.Next());
    }

    [Fact]
    public void Formats_durations()
    {
        Assert.Equal("0:42", LabelText.FormatDuration(TimeSpan.FromSeconds(42)));
        Assert.Equal("1:02:03", LabelText.FormatDuration(new TimeSpan(1, 2, 3)));
    }
}

public class PlaybackWatchdogTests
{
    private static readonly DateTime T0 = new(2026, 1, 1);

    private static PlaybackWatchdog Watchdog() =>
        new(T0, startTimeout: TimeSpan.FromSeconds(10), stallTimeout: TimeSpan.FromSeconds(8), maxLength: TimeSpan.FromSeconds(60));

    [Fact]
    public void Gives_up_on_a_video_that_never_starts()
    {
        var watchdog = Watchdog();

        Assert.Null(watchdog.Check(T0.AddSeconds(9)));
        Assert.True(watchdog.Check(T0.AddSeconds(11))!.Failed);
    }

    [Fact]
    public void Gives_up_on_a_video_that_stalls()
    {
        var watchdog = Watchdog();
        watchdog.Progress(1000, T0.AddSeconds(1));
        watchdog.Progress(5000, T0.AddSeconds(5));
        watchdog.Progress(5000, T0.AddSeconds(10)); // same position: not progress

        Assert.Null(watchdog.Check(T0.AddSeconds(12)));
        Assert.NotNull(watchdog.Check(T0.AddSeconds(14)));
    }

    [Fact]
    public void Stops_a_video_at_the_longest_length()
    {
        var watchdog = Watchdog();
        watchdog.Progress(59_000, T0.AddSeconds(59));
        Assert.Null(watchdog.Check(T0.AddSeconds(59)));

        watchdog.Progress(60_000, T0.AddSeconds(60));
        Assert.False(watchdog.Check(T0.AddSeconds(60))!.Failed);
    }

    [Fact]
    public void Paused_time_isnt_a_stall()
    {
        var watchdog = Watchdog();
        watchdog.Progress(1000, T0.AddSeconds(1));
        watchdog.SetPaused(true, T0.AddSeconds(2));

        Assert.Null(watchdog.Check(T0.AddMinutes(10)));

        watchdog.SetPaused(false, T0.AddMinutes(10));
        Assert.Null(watchdog.Check(T0.AddMinutes(10).AddSeconds(5)));
        Assert.NotNull(watchdog.Check(T0.AddMinutes(10).AddSeconds(9)));
    }

    [Fact]
    public void Stops_a_video_that_plays_slower_than_real_time()
    {
        var watchdog = Watchdog();
        // Progress keeps coming, but the clock only reaches 30 s in 100 s of real time
        for (int second = 1; second <= 100; second++)
            watchdog.Progress(second * 300, T0.AddSeconds(second));

        Assert.Null(watchdog.Check(T0.AddSeconds(99)));
        WatchdogStop? stop = watchdog.Check(T0.AddSeconds(101));
        Assert.NotNull(stop);
        Assert.False(stop.Failed);
    }

    [Fact]
    public void Paused_time_doesnt_count_towards_the_limit()
    {
        var watchdog = Watchdog();
        watchdog.Progress(1000, T0.AddSeconds(1));
        watchdog.SetPaused(true, T0.AddSeconds(2));
        watchdog.SetPaused(false, T0.AddMinutes(30));
        watchdog.Progress(2000, T0.AddMinutes(30).AddSeconds(1));

        Assert.Null(watchdog.Check(T0.AddMinutes(30).AddSeconds(2)));
    }

    [Fact]
    public void A_shown_frame_counts_as_started()
    {
        var watchdog = Watchdog();
        watchdog.FrameShown(T0.AddSeconds(3));

        Assert.Null(watchdog.Check(T0.AddSeconds(10.5)));
        Assert.NotNull(watchdog.Check(T0.AddSeconds(12)));
    }
}

public class ScreensaverArgsTests
{
    [Theory]
    [InlineData(new string[0], ScreensaverMode.Show, 0)]
    [InlineData(new[] { "/s" }, ScreensaverMode.Show, 0)]
    [InlineData(new[] { "/S" }, ScreensaverMode.Show, 0)]
    [InlineData(new[] { "/p", "1234" }, ScreensaverMode.Preview, 1234)]
    [InlineData(new[] { "/P:5678" }, ScreensaverMode.Preview, 5678)]
    [InlineData(new[] { "/c:42" }, ScreensaverMode.Configure, 42)]
    [InlineData(new[] { "/c" }, ScreensaverMode.Configure, 0)]
    [InlineData(new[] { "-c" }, ScreensaverMode.Configure, 0)]
    [InlineData(new[] { "/p" }, ScreensaverMode.Preview, 0)]
    public void Parses_what_windows_passes(string[] args, ScreensaverMode mode, long window)
    {
        var parsed = ScreensaverArgs.Parse(args);

        Assert.Equal(mode, parsed.Mode);
        Assert.Equal((nint)window, parsed.Window);
    }
}

public class FrameSizeTests
{
    [Fact]
    public void Scales_down_to_the_screen_but_never_up()
    {
        Assert.Equal((1920, 1080), FrameSize.Fit(3840, 2160, 1920, 1200, 0));
        Assert.Equal((640, 480), FrameSize.Fit(640, 480, 1920, 1080, 0));
    }

    [Fact]
    public void Keeps_within_the_pixel_budget()
    {
        var (w, h) = FrameSize.Fit(3840, 2160, 3840, 2160, 2_100_000);

        Assert.True(w * h <= 2_100_000);
        Assert.Equal(0, w % 2);
        Assert.InRange(w / (double)h, 1.76, 1.79);
    }

    [Fact]
    public void Decode_width_accounts_for_rotation()
    {
        // A 4000x3000 photo stored sideways shows 4000 tall, so 1080 rows limit its stored width to 1080
        Assert.Equal(1080, FrameSize.DecodeWidth(4000, 3000, sidesSwapped: true, 1920, 1080));
        Assert.Equal(1440, FrameSize.DecodeWidth(4000, 3000, sidesSwapped: false, 1920, 1080));
        Assert.Null(FrameSize.DecodeWidth(800, 600, false, 1920, 1080));
    }

    [Fact]
    public void Only_videos_bigger_than_4K_are_scaled_on_the_graphics_card()
    {
        Assert.True(FrameSize.NeedsGpuScaling(5312, 2988));
        Assert.True(FrameSize.NeedsGpuScaling(2988, 5312));
        Assert.False(FrameSize.NeedsGpuScaling(3840, 2160));
        Assert.False(FrameSize.NeedsGpuScaling(1920, 1080));
        Assert.False(FrameSize.NeedsGpuScaling(0, 0));
    }
}

public class VideoStatsTests
{
    [Fact]
    public void Expected_frames_follow_the_frame_rate_and_time_played()
    {
        var stats = new VideoStats("VLC", 59.94, 10_000, 0, 0, 0, 0, 0, 0, 1);

        Assert.Equal(599, stats.Expected);
    }

    [Fact]
    public void Unknown_frame_rate_expects_nothing()
    {
        Assert.Equal(0, new VideoStats("VLC", 0, 10_000, 0, 0, 0, 0, 0, 0, 1).Expected);
        Assert.Equal(0, new VideoStats("VLC", 30, 0, 0, 0, 0, 0, 0, 0, 1).DrawnFps);
    }

    [Fact]
    public void Describes_every_count()
    {
        var stats = new VideoStats("VLC", 30, 10_000, 300, 250, 50, 250, 200, 600, 2);

        Assert.Equal(
            "30 fps source, played 10.0 s, expected 300; VLC decoded 300, showed 250, lost 50; " +
            "handed over 250, drawn 200 (20 fps), copy 3.0 ms avg; 2 playing at most",
            stats.Describe());
    }

    [Fact]
    public void Leaves_out_counts_the_player_doesnt_keep()
    {
        var stats = new VideoStats("Media Foundation", 30, 10_000, null, null, null, 300, 300, 600, 1);

        Assert.Equal(
            "30 fps source, played 10.0 s, expected 300; Media Foundation; " +
            "handed over 300, drawn 300 (30 fps), copy 2.0 ms avg; 1 playing at most",
            stats.Describe());
    }
}
