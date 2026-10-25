using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace ViggiSlideshow;

/// <summary>A borderless, topmost window covering one monitor exactly.</summary>
internal sealed class ScreensaverWindow : Window
{
    private readonly Native.Rect _bounds;

    public ScreensaverWindow(Native.Rect bounds, SlideshowSurface surface)
    {
        _bounds = bounds;
        Title = "Viggi Slideshow";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        Cursor = Cursors.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = surface;

        // Physical pixels, which WPF's own Left/Top can't express across monitors of different scale
        SourceInitialized += (_, _) => Native.CoverRect(new WindowInteropHelper(this).Handle, _bounds);
        DpiChanged += (_, _) => Native.CoverRect(new WindowInteropHelper(this).Handle, _bounds);
    }
}
