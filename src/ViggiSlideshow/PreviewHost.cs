using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ViggiSlideshow.Core;

namespace ViggiSlideshow;

/// <summary>
/// The little picture in Windows' Screen Saver Settings: photos only, no labels, drawn inside
/// the window Windows hands over, and gone as soon as that window is.
/// </summary>
internal sealed class PreviewHost
{
    private readonly nint _parent;
    private HwndSource? _source;
    private Slideshow? _slideshow;
    private readonly CancellationTokenSource _scanCancel = new();

    public PreviewHost(nint parent)
    {
        _parent = parent;
    }

    public void Start()
    {
        Native.GetClientRect(_parent, out Native.Rect client);

        var parameters = new HwndSourceParameters("ViggiSlideshowPreview")
        {
            ParentWindow = _parent,
            WindowStyle = Native.WsChild | Native.WsVisible | Native.WsClipChildren,
            PositionX = 0,
            PositionY = 0,
            Width = Math.Max(1, client.Width),
            Height = Math.Max(1, client.Height),
        };
        _source = new HwndSource(parameters);
        _source.Disposed += (_, _) => Exit();

        var surface = new SlideshowSurface(textScale: 0.5);
        _source.RootVisual = surface;

        // Never saved, so the preview can't change the real settings
        Settings settings = Settings.Load(new RegistrySettingsStore());
        settings.VideoMode = VideoMode.PhotosOnly;
        settings.Random = true;

        var library = new MediaLibrary(hidden: HiddenFiles.Load(HiddenFiles.DefaultPath));
        string folder = settings.Folder;
        Task.Run(() =>
        {
            if (Directory.Exists(folder))
                MediaScanner.Scan(folder, new RealFileSystem(), library, new Random(), _scanCancel.Token);
            else
                library.ScanComplete = true;
        });

        _slideshow = new Slideshow(surface, settings, library, vlc: null, audio: false, labels: false, client.Width, client.Height)
        {
            Skipped = new SkippedFiles(SkippedFiles.DefaultPath),
        };
        _slideshow.Start();

        // Windows closes the preview window without telling us
        var watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        watch.Tick += (_, _) =>
        {
            if (!Native.IsWindow(_parent))
                Exit();
        };
        watch.Start();
    }

    private void Exit()
    {
        _scanCancel.Cancel();
        _slideshow?.Dispose();
        Application.Current.Shutdown();
    }
}
