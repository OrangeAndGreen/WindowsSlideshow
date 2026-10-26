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
        SourceInitialized += (_, _) =>
        {
            nint handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle).AddHook(HoldBounds);
            Native.CoverRect(handle, _bounds);
        };
    }

    /// <summary>
    /// Pins every move and resize to the monitor. WPF otherwise resizes the window after we place
    /// it, on Show and on a DPI change, leaving it short of the screen.
    /// </summary>
    private nint HoldBounds(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Native.WmWindowPosChanging)
            Native.ForceBounds(lParam, _bounds);
        return 0;
    }
}
