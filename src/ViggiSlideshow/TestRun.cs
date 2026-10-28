using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using ViggiSlideshow.Core;

namespace ViggiSlideshow;

/// <summary>
/// A soak test on real files, for development: <c>/test folder count outdir [mixed|photos|videos] [seconds]</c>.
/// Runs the real slideshow in an off-screen window, saves a snapshot of each file shown, and
/// writes a summary. It counts as a hang if nothing is shown or skipped for 30 seconds.
/// Nothing is saved to the real settings.
/// </summary>
internal sealed class TestRun
{
    private static readonly TimeSpan HangTimeout = TimeSpan.FromSeconds(30);

    private readonly string _folder;
    private readonly int _count;
    private readonly string _outDir;
    private readonly VideoMode _mode;
    private readonly int _videoSeconds;
    private readonly List<string> _lines = new();
    private readonly Stopwatch _sinceLast = Stopwatch.StartNew();
    private int _events, _shown, _failed;
    private Slideshow? _slideshow;
    private SlideshowSurface? _surface;

    public TestRun(IReadOnlyList<string> args)
    {
        _folder = args[1];
        _count = int.Parse(args[2]);
        _outDir = args[3];
        _mode = args.Count > 4 && Enum.TryParse(args[4], ignoreCase: true, out VideoMode mode) ? mode
            : args.Count > 4 && args[4].Equals("videos", StringComparison.OrdinalIgnoreCase) ? VideoMode.VideosOnly
            : args.Count > 4 && args[4].Equals("photos", StringComparison.OrdinalIgnoreCase) ? VideoMode.PhotosOnly
            : VideoMode.Mixed;
        _videoSeconds = args.Count > 5 ? int.Parse(args[5]) : 4;
    }

    public void Start()
    {
        Directory.CreateDirectory(_outDir);
        var settings = new Settings
        {
            Folder = _folder,
            Random = true,
            VideoMode = _mode,
            VideoMaxSeconds = _videoSeconds,
            IntervalMs = 1500,
            FadeMs = 0,
            Mute = true,
            ShowFileName = true,
            ShowInfo = true,
        };

        var library = new MediaLibrary();
        Task.Run(() => MediaScanner.Scan(_folder, new RealFileSystem(), library, new Random(), CancellationToken.None));

        // The same VLC setup as the real screensaver, so the frame counts match it
        LibVLC vlc = ScreensaverSession.CreateVlc() ?? throw new InvalidOperationException("VLC didn't load");

        _surface = new SlideshowSurface();
        var window = new Window
        {
            Title = "Viggi Slideshow test",
            Width = 960,
            Height = 540,
            Left = -20000,
            Top = 0,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = _surface,
        };
        window.Show();

        _slideshow = new Slideshow(_surface, settings, library, vlc, audio: false, labels: true, 960, 540);
        _slideshow.FileShown += path => Record("SHOWN ", path, snapshot: true);
        _slideshow.FileFailed += (path, reason) => Record("FAILED", $"{path}  ({reason})", snapshot: false);

        var hangCheck = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        hangCheck.Tick += (_, _) =>
        {
            if (_sinceLast.Elapsed > HangTimeout)
            {
                _lines.Add($"HANG   nothing happened for {HangTimeout.TotalSeconds:0} s after {_slideshow.Current}");
                Finish(2);
            }
        };
        hangCheck.Start();
        _slideshow.Start();
    }

    private void Record(string kind, string text, bool snapshot)
    {
        _lines.Add($"{kind} {_sinceLast.Elapsed.TotalSeconds,5:0.0}s {Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024),5} MB  {text}");
        _sinceLast.Restart();
        if (kind.StartsWith("SHOWN")) _shown++; else _failed++;

        int index = ++_events;
        if (snapshot)
        {
            // Long enough for the label and a few video frames to arrive
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Snapshot(Path.Combine(_outDir, $"{index:000}.png"));
            };
            timer.Start();
        }

        if (index >= _count)
        {
            var end = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            end.Tick += (_, _) => Finish(0);
            end.Start();
        }
    }

    private void Snapshot(string file)
    {
        try
        {
            var bitmap = new RenderTargetBitmap(960, 540, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(_surface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(file);
            encoder.Save(stream);
        }
        catch (Exception e)
        {
            _lines.Add($"SNAPSHOT ERROR {e.Message}");
        }
    }

    private void Finish(int code)
    {
        _lines.Add($"DONE   {_shown} shown, {_failed} skipped, {Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024)} MB in use");
        File.WriteAllLines(Path.Combine(_outDir, "summary.txt"), _lines);
        _slideshow?.Dispose();
        VideoPlayback.WaitForTeardowns(TimeSpan.FromSeconds(3)).ContinueWith(_ => Environment.Exit(code));
    }
}
