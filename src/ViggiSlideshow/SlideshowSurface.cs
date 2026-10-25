using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ViggiSlideshow.Core;

namespace ViggiSlideshow;

/// <summary>
/// What one screen draws: two picture layers that crossfade, the corner label, a short-lived
/// message line and a centred status line. Photos and video frames are both just bitmaps here.
/// </summary>
internal sealed class SlideshowSurface : Grid
{
    private sealed class Layer
    {
        public readonly Image Image = new() { Stretch = Stretch.Fill };
        public readonly Viewbox Box = new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Opacity = 0 };

        public Layer()
        {
            RenderOptions.SetBitmapScalingMode(Image, BitmapScalingMode.HighQuality);
            Box.Child = Image;
        }
    }

    private static readonly FontFamily TextFont = new("Segoe UI");
    private static readonly FontFamily TableFont = new("Cascadia Mono, Consolas");

    private readonly Layer[] _layers = [new Layer(), new Layer()];
    private int _front;

    private readonly Border _label;
    private readonly TextBlock _labelText;
    private readonly Border _message;
    private readonly TextBlock _messageText;
    private readonly TextBlock _status;
    private readonly DispatcherTimer _messageTimer;

    public SlideshowSurface(double textScale = 1.0)
    {
        Background = Brushes.Black;
        ClipToBounds = true;
        foreach (Layer layer in _layers)
            Children.Add(layer.Box);

        _labelText = new TextBlock { Foreground = Brushes.White, FontSize = 17 * textScale, TextWrapping = TextWrapping.Wrap };
        _label = new Border
        {
            Child = _labelText,
            Background = new SolidColorBrush(Color.FromArgb(0xA8, 0, 0, 0)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(28),
            Visibility = Visibility.Collapsed,
        };
        Children.Add(_label);

        _messageText = new TextBlock { Foreground = Brushes.White, FontSize = 22 * textScale, FontFamily = TextFont };
        _message = new Border
        {
            Child = _messageText,
            Background = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18, 10, 18, 10),
            Margin = new Thickness(0, 0, 0, 60),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        Children.Add(_message);

        // Text stays above both picture layers, which take z-index 0 and 1 as they swap
        Panel.SetZIndex(_label, 10);
        Panel.SetZIndex(_message, 11);

        _status = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
            FontSize = 20 * textScale,
            FontFamily = TextFont,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(40),
            Visibility = Visibility.Collapsed,
        };
        Panel.SetZIndex(_status, 12);
        Children.Add(_status);

        _messageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _messageTimer.Tick += (_, _) =>
        {
            _messageTimer.Stop();
            _message.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)));
        };

        SizeChanged += (_, _) => _label.MaxWidth = Math.Max(200, ActualWidth * 0.45);
    }

    /// <summary>Fades the new picture in over the old one.</summary>
    /// <param name="width">The width to lay the picture out at, which corrects non-square pixels.</param>
    public void Show(ImageSource source, double width, double height, int rotation, int fadeMs)
    {
        Layer outgoing = _layers[_front];
        _front = 1 - _front;
        Layer incoming = _layers[_front];

        incoming.Image.Source = source;
        SetGeometry(incoming, width, height, rotation);
        Panel.SetZIndex(incoming.Box, 1);
        Panel.SetZIndex(outgoing.Box, 0);

        var fade = TimeSpan.FromMilliseconds(Math.Max(0, fadeMs));
        if (fade == TimeSpan.Zero)
        {
            incoming.Box.BeginAnimation(OpacityProperty, null);
            outgoing.Box.BeginAnimation(OpacityProperty, null);
            incoming.Box.Opacity = 1;
            outgoing.Box.Opacity = 0;
            outgoing.Image.Source = null;
            return;
        }

        var fadeIn = new DoubleAnimation(0, 1, fade);
        incoming.Box.BeginAnimation(OpacityProperty, fadeIn);
        var fadeOut = new DoubleAnimation(0, fade);
        fadeOut.Completed += (_, _) =>
        {
            // Let go of the old bitmap, unless this layer has been reused since
            if (_layers[_front] != outgoing)
                outgoing.Image.Source = null;
        };
        outgoing.Box.BeginAnimation(OpacityProperty, fadeOut);
    }

    /// <summary>Corrects the shape or rotation of what is already showing, once a video reports it.</summary>
    public void Reshape(ImageSource source, double width, double height, int rotation)
    {
        Layer front = _layers[_front];
        if (front.Image.Source == source)
            SetGeometry(front, width, height, rotation);
    }

    public bool IsShowing(ImageSource source) => _layers[_front].Image.Source == source;

    public void SetLabel(string text, Corner corner, bool table)
    {
        if (text.Length == 0)
        {
            _label.Visibility = Visibility.Collapsed;
            return;
        }

        _labelText.Text = text;
        _labelText.FontFamily = table ? TableFont : TextFont;
        _label.HorizontalAlignment = corner is Corner.TopLeft or Corner.BottomLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        _label.VerticalAlignment = corner is Corner.TopLeft or Corner.TopRight ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        _label.Visibility = Visibility.Visible;
    }

    public void Flash(string message)
    {
        _messageText.Text = message;
        _message.BeginAnimation(OpacityProperty, null);
        _message.Opacity = 1;
        _messageTimer.Stop();
        _messageTimer.Start();
    }

    public void SetStatus(string? status)
    {
        _status.Text = status ?? "";
        _status.Visibility = status is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void SetGeometry(Layer layer, double width, double height, int rotation)
    {
        layer.Image.Width = Math.Max(1, width);
        layer.Image.Height = Math.Max(1, height);
        layer.Image.LayoutTransform = rotation == 0 ? Transform.Identity : new RotateTransform(rotation);
    }
}
