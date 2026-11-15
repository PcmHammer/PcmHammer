// SPDX-License-Identifier: GPL-3.0-only
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace PcmHacking.UI;

/// <summary>
/// A <see cref="Window"/> that wears the PcmHammer family's custom title bar on Windows: the client
/// area is extended into a 30px Mica strip, and the app icon and title are drawn into that strip. Both
/// the PcmHammer flasher and the PcmLogger derive from this so the two apps read as one family.
///
/// Why draw our own icon/title: extending the client area replaces the native title bar, so the OS no
/// longer paints <see cref="Window.Icon"/>, and the managed chrome's own title can't be repositioned
/// (it is pinned as a local value a style can't move). So we hide the chrome's title and draw our own.
///
/// On Linux and other platforms none of this runs - the native title bar already shows the icon and
/// title - so a derived window looks and behaves exactly as a plain <see cref="Window"/> there.
///
/// A derived window only sets <see cref="Window.Icon"/>, <see cref="Window.Title"/> and
/// <see cref="TitleBarIconSource"/> (a decodable image, e.g. a PNG, for the strip). Its own content is
/// authored normally; this base pushes it below the strip on Windows.
/// </summary>
public class ChromeWindow : Window
{
    private const double TitleStripHeight = 30;

    /// <summary>
    /// The image shown at the left of the custom title strip on Windows (e.g. "/Assets/titlebar_icon.png").
    /// A PNG is preferred so it decodes without relying on .ico support. Ignored off Windows, where the
    /// native title bar paints <see cref="Window.Icon"/> instead.
    /// </summary>
    public static readonly StyledProperty<string?> TitleBarIconSourceProperty =
        AvaloniaProperty.Register<ChromeWindow, string?>(nameof(TitleBarIconSource));

    public string? TitleBarIconSource
    {
        get => this.GetValue(TitleBarIconSourceProperty);
        set => this.SetValue(TitleBarIconSourceProperty, value);
    }

    private bool _wrapped;

    public ChromeWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Guard the whole custom chrome: if anything here fails, fall back to the native title bar
        // rather than take the window down with it.
        try
        {
            this.TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur];
            this.Background = Brushes.Transparent;
            this.ExtendClientAreaToDecorationsHint = true;
            this.ExtendClientAreaTitleBarHeightHint = TitleStripHeight;

            // Hide the managed chrome's own title text (we draw our own) and its full-screen button. The
            // chrome pins the title's layout as a local value, so Opacity - not IsVisible - is the lever
            // that takes. The caption buttons are vector glyphs, so the title is the only TextBlock in
            // the template.
            this.Styles.Add(HiddenInTemplate<TextBlock>(null));
            this.Styles.Add(HiddenInTemplate<Button>("PART_FullScreenButton"));

            // Wrap the content once the window has opened - NOT during the XAML load that sets Content.
            // Reparenting the content while it is still the window's Content throws ("already has a
            // parent"), and doing it inside a property-change notification fails the window silently.
            this.Opened += this.OnOpenedWrapContent;
        }
        catch
        {
            // ignore - native title bar
        }
    }

    private void OnOpenedWrapContent(object? sender, EventArgs e)
    {
        this.Opened -= this.OnOpenedWrapContent;

        if (_wrapped || this.Content is not Control inner)
        {
            return;
        }

        _wrapped = true;

        try
        {
            // Detach the app's content from the window before reparenting it under the title strip; a
            // control cannot have two parents. It keeps its own margins inside the host.
            this.Content = null;
            Decorator host = new() { Child = inner, Margin = new Thickness(0, TitleStripHeight, 0, 0) };
            this.Content = new Panel { Children = { host, this.BuildTitleBar() } };
        }
        catch
        {
            // Put the original content back if wrapping fails, so the window is never left blank.
            this.Content = inner;
        }
    }

    private Control BuildTitleBar()
    {
        // IsHitTestVisible=False so the strip still drags the window.
        StackPanel bar = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Height = TitleStripHeight,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(10, 0, 0, 0),
            IsHitTestVisible = false,
        };

        if (this.LoadTitleBarIcon() is { } icon)
        {
            bar.Children.Add(new Image
            {
                Source = icon,
                Width = 18,
                Height = 18,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        bar.Children.Add(new TextBlock
        {
            Text = this.Title,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });

        return bar;
    }

    private Bitmap? LoadTitleBarIcon()
    {
        string? source = this.TitleBarIconSource;
        if (string.IsNullOrEmpty(source))
        {
            return null;
        }

        // The asset lives in the derived app's assembly (this file is compiled into each app), so
        // resolve it against that assembly rather than a shared one.
        try
        {
            string? assembly = this.GetType().Assembly.GetName().Name;
            Uri uri = new($"avares://{assembly}{source}");
            return new Bitmap(AssetLoader.Open(uri));
        }
        catch
        {
            return null;
        }
    }

    private static Style HiddenInTemplate<T>(string? partName)
        where T : Control
    {
        Style style = new(x =>
        {
            Selector inTemplate = x.OfType<WindowDrawnDecorations>().Template().OfType<T>();
            return partName is null ? inTemplate : inTemplate.Name(partName);
        });

        style.Add(new Setter(Visual.OpacityProperty, 0.0));
        if (partName is not null)
        {
            style.Add(new Setter(InputElement.IsHitTestVisibleProperty, false));
            style.Add(new Setter(InputElement.FocusableProperty, false));
        }

        return style;
    }
}
