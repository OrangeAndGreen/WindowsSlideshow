using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using LibVLCSharp.Shared;
using ViggiSlideshow.Core;

namespace ViggiSlideshow;

/// <summary>
/// The running screensaver: one window and slideshow per monitor, sharing the settings, the
/// library being scanned and VLC. Keys act on every screen; moving the mouse or clicking ends it.
/// </summary>
internal sealed class ScreensaverSession
{
    private const int WakeMovementPixels = 20;

    private readonly RegistrySettingsStore _store = new();
    private readonly Settings _settings;
    private readonly MediaLibrary _library = new(hidden: HiddenFiles.Load(HiddenFiles.DefaultPath));
    private readonly SkippedFiles _skipped = new(SkippedFiles.DefaultPath);
    private readonly CancellationTokenSource _scanCancel = new();
    private readonly List<(ScreensaverWindow Window, Slideshow Slideshow)> _screens = new();
    private LibVLC? _vlc;
    private Native.Point _startCursor;
    private bool _finishing;

    public ScreensaverSession()
    {
        _settings = Settings.Load(_store);
    }

    public void Start()
    {
        Log.Info($"Starting on {_settings.Folder} ({(_settings.Random ? "random" : "folder order")}, {_settings.VideoMode.Describe()}, {_library.Hidden.Count} hidden)");
        Native.GetCursorPos(out _startCursor);

        StartScan();
        _vlc = CreateVlc();

        foreach (Native.Monitor monitor in Native.Monitors())
        {
            var surface = new SlideshowSurface(textScale: Math.Clamp(monitor.Bounds.Height / 1080.0, 0.8, 2.0));
            var slideshow = new Slideshow(surface, _settings, _library, _vlc, audio: monitor.Primary, labels: true,
                monitor.Bounds.Width, monitor.Bounds.Height) { Skipped = _skipped };
            var window = new ScreensaverWindow(monitor.Bounds, surface);

            window.PreviewKeyDown += (_, e) =>
            {
                e.Handled = true;
                OnKey(e.Key == Key.System ? e.SystemKey : e.Key, slideshow);
            };
            window.PreviewMouseMove += (_, _) => CheckMouse();
            window.PreviewMouseDown += (_, _) => Finish();
            window.Closed += (_, _) => Finish();

            _screens.Add((window, slideshow));
        }

        // Show the others first so the primary screen ends up with keyboard focus
        foreach (var (window, _) in Enumerable.Reverse(_screens))
            window.Show();
        _screens[0].Window.Activate();

        foreach (var (_, slideshow) in _screens)
            slideshow.Start();
    }

    private void StartScan()
    {
        string folder = _settings.Folder;
        Task.Run(() =>
        {
            var timer = Stopwatch.StartNew();
            if (!Directory.Exists(folder))
            {
                Log.Error($"Folder not found: {folder}");
                _library.ScanComplete = true;
                return;
            }
            MediaScanner.Scan(folder, new RealFileSystem(), _library, new Random(), _scanCancel.Token,
                (directory, e) => Log.Error($"Reading folder {directory}", e));
            Log.Info($"Found {_library.ImageCount:N0} photos and {_library.VideoCount:N0} videos in {timer.Elapsed.TotalSeconds:0.0} s");
        });
    }

    internal static LibVLC? CreateVlc()
    {
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            var vlc = new LibVLC(
                "--quiet",
                "--no-osd",
                "--no-video-title-show",
                "--no-sub-autodetect-file",
                "--no-snapshot-preview",
                // VLC otherwise keeps the monitors awake while a video plays
                "--no-disable-screensaver",
                // Fast bilinear, not bicubic, for shrinking frames to the screen: the CPU scaling
                // dropped a third of 4K frames and two thirds of 5.3K GoPro frames
                "--swscale-mode=0");
            // Says whether the graphics card is decoding, or VLC fell back to the CPU
            vlc.Log += (_, e) =>
            {
                if (e.Message.Contains("hardware", StringComparison.OrdinalIgnoreCase)
                    || e.Message.Contains("D3D11VA", StringComparison.OrdinalIgnoreCase)
                    || e.Message.Contains("DXVA", StringComparison.OrdinalIgnoreCase))
                    Log.Info($"VLC {e.Module}: {e.Message}");
            };
            return vlc;
        }
        catch (Exception e)
        {
            Log.Error("VLC didn't load; showing photos only", e);
            return null;
        }
    }

    private void OnKey(Key key, Slideshow source)
    {
        KeyCommand command = KeyMap.Map(key.ToString());
        switch (command.Command)
        {
            case Command.Next:
                Each(s => s.Advance(1));
                break;
            case Command.Previous:
                Each(s => s.Advance(-1));
                break;
            case Command.Pause:
                bool pause = !source.Paused;
                Each(s => s.SetPaused(pause));
                Flash(pause ? "Paused" : "Playing");
                break;
            case Command.ToggleFileName:
                _settings.ShowFileName = !_settings.ShowFileName;
                SaveAndRelabel();
                break;
            case Command.ToggleInfo:
                _settings.ShowInfo = !_settings.ShowInfo;
                SaveAndRelabel();
                break;
            case Command.ToggleDebug:
                Each(s => s.ShowDebug = !s.ShowDebug);
                Each(s => s.UpdateLabel());
                break;
            case Command.ToggleHelp:
                Each(s => s.ShowHelp = !s.ShowHelp);
                Each(s => s.UpdateLabel());
                break;
            case Command.ToggleSettings:
                Each(s => s.ShowSettings = !s.ShowSettings);
                Each(s => s.UpdateLabel());
                break;
            case Command.ToggleMute:
                _settings.Mute = !_settings.Mute;
                Save();
                Each(s => s.ApplyMute());
                Flash(_settings.Mute ? "Sound off" : "Sound on");
                break;
            case Command.ToggleRandom:
                _settings.Random = !_settings.Random;
                Save();
                Each(s => s.SetRandom(_settings.Random));
                Flash(_settings.Random ? "Random order" : "Folder order");
                break;
            case Command.CycleVideos:
                SetVideoMode(_settings.VideoMode.Next());
                break;
            case Command.ToggleVideosOnly:
                SetVideoMode(_settings.VideoMode.ToggleVideosOnly());
                break;
            case Command.SetInterval:
                _settings.IntervalMs = Settings.IntervalForDigit(command.Digit);
                Save();
                Each(s => s.ApplyInterval());
                Flash($"{_settings.IntervalMs / 1000} seconds per photo{(command.Digit == 0 ? " (default)" : "")}");
                break;
            case Command.HideFile:
                HideFile(source);
                break;
            case Command.OpenFolder:
                OpenInExplorer(source.Current);
                Finish();
                break;
            case Command.Quit:
                Finish();
                break;
        }
    }

    // The file goes from every screen's history, but only the screen showing it moves on
    private void HideFile(Slideshow source)
    {
        if (source.Current is not { } path)
            return;
        bool added = source.Hide(path);
        foreach (var (_, slideshow) in _screens)
        {
            if (slideshow != source)
                slideshow.Hide(path);
        }
        Log.Info(added ? $"Hidden from the show: {path}" : $"Already hidden: {path}");
        source.Flash($"Hidden for good: {Path.GetFileName(path)}");
    }

    private void SetVideoMode(VideoMode mode)
    {
        _settings.VideoMode = mode;
        Save();
        Flash(mode.Describe());
        Each(s => s.ApplyVideoMode());
        Each(s => s.UpdateLabel());
    }

    private void SaveAndRelabel()
    {
        Save();
        Each(s => s.UpdateLabel());
    }

    private void Save() => _settings.Save(_store);

    private void Each(Action<Slideshow> action)
    {
        foreach (var (_, slideshow) in _screens)
            action(slideshow);
    }

    private void Flash(string message) => Each(s => s.Flash(message));

    private static void OpenInExplorer(string? path)
    {
        if (path is null)
            return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Error($"Opening Explorer at {path}", e);
        }
    }

    private void CheckMouse()
    {
        if (Native.GetCursorPos(out Native.Point now)
            && (Math.Abs(now.X - _startCursor.X) > WakeMovementPixels || Math.Abs(now.Y - _startCursor.Y) > WakeMovementPixels))
        {
            Finish();
        }
    }

    /// <summary>Hides everything at once, then tidies up. A video that won't stop can't hold the exit up.</summary>
    public void Finish()
    {
        if (_finishing)
            return;
        _finishing = true;

        foreach (var (window, _) in _screens)
            window.Hide();

        Log.Info("Exiting");
        _scanCancel.Cancel();
        Save();
        foreach (var (_, slideshow) in _screens)
            slideshow.Dispose();

        // Give VLC a moment to stop cleanly, then leave regardless
        VideoPlayback.WaitForTeardowns(TimeSpan.FromMilliseconds(1500)).ContinueWith(_ => Environment.Exit(0));
    }
}
