using System.Runtime.InteropServices;

namespace ViggiSlideshow;

internal static partial class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    private const uint MonitorInfoPrimary = 1;

    public const int WsChild = 0x40000000;
    public const int WsVisible = 0x10000000;
    public const int WsClipChildren = 0x02000000;

    private static readonly nint HwndTopmost = -1;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private delegate bool MonitorEnumProc(nint monitor, nint hdc, ref Rect rect, nint data);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hwnd);

    public sealed record Monitor(Rect Bounds, bool Primary);

    /// <summary>Every monitor's bounds in physical pixels, the primary one first.</summary>
    public static List<Monitor> Monitors()
    {
        var monitors = new List<Monitor>();
        EnumDisplayMonitors(0, 0, (nint handle, nint _, ref Rect _, nint _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(handle, ref info))
                monitors.Add(new Monitor(info.Monitor, (info.Flags & MonitorInfoPrimary) != 0));
            return true;
        }, 0);
        return monitors.OrderByDescending(m => m.Primary).ToList();
    }

    public const int WmWindowPosChanging = 0x0046;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public nint Hwnd, InsertAfter;
        public int X, Y, Cx, Cy;
        public uint Flags;
    }

    /// <summary>Rewrites a WM_WINDOWPOSCHANGING request so the window lands exactly on the bounds.</summary>
    public static unsafe void ForceBounds(nint windowPos, Rect bounds)
    {
        var pos = (WindowPos*)windowPos;
        pos->X = bounds.Left;
        pos->Y = bounds.Top;
        pos->Cx = bounds.Width;
        pos->Cy = bounds.Height;
        pos->Flags &= ~(SwpNoMove | SwpNoSize);
    }

    public static void CoverRect(nint hwnd, Rect bounds) =>
        SetWindowPos(hwnd, HwndTopmost, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SwpShowWindow | SwpNoActivate);
}
