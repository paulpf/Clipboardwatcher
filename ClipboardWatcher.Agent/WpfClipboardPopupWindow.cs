using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;

namespace ClipboardWatcher.Agent;

internal sealed class WpfClipboardPopupWindow : Window
{
    private readonly DispatcherTimer _closeTimer;

    public WpfClipboardPopupWindow(string title, string content, int durationMs, BitmapSource? thumbnail = null)
    {
        Width = 420;
        Height = thumbnail is null ? 130 : 156;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;

        Content = BuildContent(title, content, thumbnail);
        Opacity = 0;

        _closeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(durationMs)
        };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            BeginFadeOut();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        PositionBottomRight();
        BeginFadeIn();
        _closeTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closeTimer.Stop();
        base.OnClosed(e);
    }

    private static Border BuildContent(string title, string content, BitmapSource? thumbnail)
    {
        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = WpfBrushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var bodyBlock = new TextBlock
        {
            Text = content,
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 13,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(220, 230, 242)),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = thumbnail is null ? 62 : 96
        };

        var panel = new StackPanel();
        panel.Children.Add(titleBlock);
        panel.Children.Add(bodyBlock);

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(panel, 0);
        layout.Children.Add(panel);

        if (thumbnail is not null)
        {
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var image = new System.Windows.Controls.Image
            {
                Source = thumbnail,
                Width = thumbnail.PixelWidth,
                Height = thumbnail.PixelHeight,
                Stretch = Stretch.Uniform
            };

            var imageContainer = new Border
            {
                Margin = new Thickness(12, 0, 0, 0),
                Padding = new Thickness(4),
                Background = new SolidColorBrush(WpfColor.FromArgb(40, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(WpfColor.FromArgb(120, 148, 163, 184)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = image
            };

            Grid.SetColumn(imageContainer, 1);
            layout.Children.Add(imageContainer);
        }

        return new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 14, 16, 14),
            Background = new LinearGradientBrush(
                WpfColor.FromRgb(30, 41, 59),
                WpfColor.FromRgb(15, 23, 42),
                new WpfPoint(0, 0),
                new WpfPoint(1, 1)),
            BorderBrush = new SolidColorBrush(WpfColor.FromArgb(140, 96, 165, 250)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect
            {
                BlurRadius = 22,
                ShadowDepth = 3,
                Opacity = 0.35,
                Color = WpfColor.FromRgb(0, 0, 0)
            },
            Child = layout
        };
    }

    private void PositionBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        const double margin = 14;
        Left = workArea.Right - Width - margin;
        Top = workArea.Bottom - Height - margin;
    }

    private void BeginFadeIn()
    {
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
        {
            FillBehavior = FillBehavior.HoldEnd
        };
        BeginAnimation(OpacityProperty, animation);
    }

    private void BeginFadeOut()
    {
        var animation = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(170))
        {
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, animation);
    }
}
