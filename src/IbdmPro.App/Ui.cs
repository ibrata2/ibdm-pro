using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IbdmPro.App;

/// <summary>The IBDM Pro look: black and gold, Arabic, right to left.</summary>
public static class Theme
{
    public static readonly Brush Bg = Br("#0F0F11");
    public static readonly Brush Panel = Br("#16161A");
    public static readonly Brush Card = Br("#1E1E23");
    public static readonly Brush Hover = Br("#2A2A31");
    public static readonly Brush Pressed = Br("#33333B");
    public static readonly Brush Line = Br("#2C2C33");
    public static readonly Brush Gold = Br("#D4AF37");
    public static readonly Brush GoldHover = Br("#E2C25A");
    public static readonly Brush GoldSoft = Br("#33D4AF37");
    public static readonly Brush OnGold = Br("#111111");
    public static readonly Brush Ink = Br("#F2F2F4");
    public static readonly Brush Muted = Br("#9C9CA6");
    public static readonly Brush Red = Br("#E5533D");
    public static readonly Brush Green = Br("#4CC26B");
    public static readonly Brush Blue = Br("#3B9BF0");
    public static readonly Brush Track = Br("#2A2A30");

    public static readonly FontFamily Font = new("Segoe UI, Tahoma");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static Brush Br(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    // ---------- styles built in code (no XAML) ----------

    public static readonly Style Button = MakeButton(Card, Hover, Pressed, Ink, Line);
    public static readonly Style PrimaryButton = MakeButton(Gold, GoldHover, Br("#B8962A"), OnGold, Gold);
    public static readonly Style DangerButton = MakeButton(Br("#3A1E1A"), Br("#4A2620"), Br("#552B24"), Br("#FF8A75"), Br("#5A2A22"));
    public static readonly Style ToolButton = MakeButton(Brushes.Transparent, Hover, Pressed, Ink, Brushes.Transparent);
    public static readonly Style Choice = MakeChoice();
    public static readonly Style Nav = MakeNav();
    public static readonly Style Progress = MakeProgress();
    public static readonly Style Row = MakeRow();
    public static readonly Style Header = MakeHeader();
    public static readonly Style Input = MakeInput();
    public static readonly Style CheckBox = MakeCheck();

    /// <summary>A gold tick in a dark box (the default box draws a mirrored tick in right-to-left windows).</summary>
    private static Style MakeCheck()
    {
        var root = new FrameworkElementFactory(typeof(DockPanel));
        root.SetValue(System.Windows.Controls.Panel.BackgroundProperty, Brushes.Transparent);
        var box = new FrameworkElementFactory(typeof(Border), "Box");
        box.SetValue(FrameworkElement.WidthProperty, 18.0);
        box.SetValue(FrameworkElement.HeightProperty, 18.0);
        box.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        box.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        box.SetValue(Border.BorderBrushProperty, Br("#55555F"));
        box.SetValue(Border.BackgroundProperty, Card);
        box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
        box.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 0));
        box.SetValue(DockPanel.DockProperty, Dock.Left);
        var mark = new FrameworkElementFactory(typeof(TextBlock), "Mark");
        mark.SetValue(TextBlock.TextProperty, "\uE73E");
        mark.SetValue(TextBlock.FontFamilyProperty, Icons);
        mark.SetValue(TextBlock.FontSizeProperty, 12.0);
        mark.SetValue(TextBlock.ForegroundProperty, OnGold);
        mark.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        mark.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        mark.SetValue(FrameworkElement.FlowDirectionProperty, FlowDirection.LeftToRight);
        mark.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        box.AppendChild(mark);
        root.AppendChild(box);
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 0, 0));
        cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        root.AppendChild(cp);
        var t = new ControlTemplate(typeof(CheckBox)) { VisualTree = root };
        t.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true, Setters = { new Setter(Border.BorderBrushProperty, Gold, "Box") } });
        t.Triggers.Add(new Trigger
        {
            Property = ToggleButton.IsCheckedProperty, Value = true,
            Setters = { new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Mark"), new Setter(Border.BackgroundProperty, Gold, "Box"), new Setter(Border.BorderBrushProperty, Gold, "Box") },
        });
        t.Triggers.Add(new Trigger { Property = UIElement.IsEnabledProperty, Value = false, Setters = { new Setter(UIElement.OpacityProperty, 0.45) } });
        var s = new Style(typeof(CheckBox));
        s.Setters.Add(new Setter(Control.TemplateProperty, t));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
        s.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        return s;
    }

    private static Style MakeButton(Brush bg, Brush hover, Brush pressed, Brush fg, Brush border)
    {
        var bd = new FrameworkElementFactory(typeof(Border), "Bd");
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        bd.SetBinding(Border.BackgroundProperty, TP("Background"));
        bd.SetBinding(Border.BorderBrushProperty, TP("BorderBrush"));
        bd.SetBinding(Border.BorderThicknessProperty, TP("BorderThickness"));
        bd.SetBinding(Border.PaddingProperty, TP("Padding"));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        var t = new ControlTemplate(typeof(ButtonBase)) { VisualTree = bd };
        t.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true, Setters = { new Setter(Border.BackgroundProperty, hover, "Bd") } });
        t.Triggers.Add(new Trigger { Property = ButtonBase.IsPressedProperty, Value = true, Setters = { new Setter(Border.BackgroundProperty, pressed, "Bd") } });
        t.Triggers.Add(new Trigger { Property = UIElement.IsEnabledProperty, Value = false, Setters = { new Setter(UIElement.OpacityProperty, 0.4) } });
        var s = new Style(typeof(ButtonBase));
        s.Setters.Add(new Setter(Control.TemplateProperty, t));
        s.Setters.Add(new Setter(Control.BackgroundProperty, bg));
        s.Setters.Add(new Setter(Control.ForegroundProperty, fg));
        s.Setters.Add(new Setter(Control.BorderBrushProperty, border));
        s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 7, 16, 7)));
        s.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        s.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        s.Setters.Add(new Setter(UIElement.FocusableProperty, true));
        return s;
    }

    /// <summary>A RadioButton drawn as a selectable card (qualities, categories).</summary>
    private static Style MakeChoice()
    {
        var bd = new FrameworkElementFactory(typeof(Border), "Bd");
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        bd.SetBinding(Border.BackgroundProperty, TP("Background"));
        bd.SetBinding(Border.BorderBrushProperty, TP("BorderBrush"));
        bd.SetBinding(Border.PaddingProperty, TP("Padding"));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        var t = new ControlTemplate(typeof(RadioButton)) { VisualTree = bd };
        t.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true, Setters = { new Setter(Border.BackgroundProperty, Hover, "Bd") } });
        t.Triggers.Add(new Trigger
        {
            Property = ToggleButton.IsCheckedProperty, Value = true,
            Setters = { new Setter(Border.BorderBrushProperty, Gold, "Bd"), new Setter(Border.BackgroundProperty, GoldSoft, "Bd") },
        });
        var s = new Style(typeof(RadioButton));
        s.Setters.Add(new Setter(Control.TemplateProperty, t));
        s.Setters.Add(new Setter(Control.BackgroundProperty, Card));
        s.Setters.Add(new Setter(Control.BorderBrushProperty, Line));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 8, 12, 8)));
        s.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        s.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 6)));
        return s;
    }

    /// <summary>Side navigation entry.</summary>
    private static Style MakeNav()
    {
        var bd = new FrameworkElementFactory(typeof(Border), "Bd");
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        bd.SetValue(Border.PaddingProperty, new Thickness(10, 7, 10, 7));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        bd.AppendChild(cp);
        var t = new ControlTemplate(typeof(RadioButton)) { VisualTree = bd };
        t.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true, Setters = { new Setter(Border.BackgroundProperty, Hover, "Bd") } });
        t.Triggers.Add(new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true, Setters = { new Setter(Border.BackgroundProperty, GoldSoft, "Bd") } });
        var s = new Style(typeof(RadioButton));
        s.Setters.Add(new Setter(Control.TemplateProperty, t));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
        s.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        s.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1)));
        return s;
    }

    private static Style MakeProgress()
    {
        var grid = new FrameworkElementFactory(typeof(Grid));
        var track = new FrameworkElementFactory(typeof(Border), "PART_Track");
        track.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        track.SetValue(Border.BackgroundProperty, Track);
        var ind = new FrameworkElementFactory(typeof(Border), "PART_Indicator");
        ind.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        ind.SetBinding(Border.BackgroundProperty, TP("Foreground"));
        ind.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        grid.AppendChild(track);
        grid.AppendChild(ind);
        var s = new Style(typeof(ProgressBar));
        s.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ProgressBar)) { VisualTree = grid }));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Gold));
        s.Setters.Add(new Setter(FrameworkElement.HeightProperty, 6.0));
        return s;
    }

    private static Style MakeRow()
    {
        var bd = new FrameworkElementFactory(typeof(Border), "Bd");
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        bd.SetValue(Border.BorderBrushProperty, Line);
        bd.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
        bd.SetValue(Border.PaddingProperty, new Thickness(0, 6, 0, 6));
        var rp = new FrameworkElementFactory(typeof(GridViewRowPresenter));
        rp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(rp);
        var t = new ControlTemplate(typeof(ListViewItem)) { VisualTree = bd };
        t.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true, Setters = { new Setter(Border.BackgroundProperty, Card, "Bd") } });
        t.Triggers.Add(new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true, Setters = { new Setter(Border.BackgroundProperty, GoldSoft, "Bd") } });
        var s = new Style(typeof(ListViewItem));
        s.Setters.Add(new Setter(Control.TemplateProperty, t));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
        s.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        return s;
    }

    private static Style MakeHeader()
    {
        var bd = new FrameworkElementFactory(typeof(Border), "Bd");
        bd.SetValue(Border.BackgroundProperty, Panel);
        bd.SetValue(Border.BorderBrushProperty, Line);
        bd.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
        bd.SetValue(Border.PaddingProperty, new Thickness(8, 8, 8, 8));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        bd.AppendChild(cp);
        var t = new ControlTemplate(typeof(GridViewColumnHeader)) { VisualTree = bd };
        t.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true, Setters = { new Setter(Border.BackgroundProperty, Card, "Bd") } });
        var s = new Style(typeof(GridViewColumnHeader));
        s.Setters.Add(new Setter(Control.TemplateProperty, t));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Muted));
        s.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
        s.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        return s;
    }

    private static Style MakeInput()
    {
        var s = new Style(typeof(TextBox));
        s.Setters.Add(new Setter(Control.BackgroundProperty, Card));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
        s.Setters.Add(new Setter(Control.BorderBrushProperty, Line));
        s.Setters.Add(new Setter(TextBoxBase.CaretBrushProperty, Gold));
        s.Setters.Add(new Setter(TextBoxBase.SelectionBrushProperty, Gold));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        s.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        s.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        return s;
    }

    private static Binding TP(string path) => new(path) { RelativeSource = RelativeSource.TemplatedParent };
}

/// <summary>Small builders so windows read like a description of their layout.</summary>
public static class Ui
{
    public static TextBlock Text(string s, double size = 13, Brush? fg = null, bool bold = false, TextWrapping wrap = TextWrapping.NoWrap) => new()
    {
        Text = s,
        FontSize = size,
        Foreground = fg ?? Theme.Ink,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        TextWrapping = wrap,
        TextTrimming = wrap == TextWrapping.NoWrap ? TextTrimming.CharacterEllipsis : TextTrimming.None,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>English/number text (sizes, speeds, qualities) shown left-to-right inside the Arabic layout,
    /// so "94.8 MB" never turns into "MB 94.8".</summary>
    public static TextBlock Ltr(string s, double size = 13, Brush? fg = null, bool bold = false)
    {
        var t = Text(s, size, fg, bold);
        t.FlowDirection = FlowDirection.LeftToRight;
        t.HorizontalAlignment = HorizontalAlignment.Left; // = the right edge inside a right-to-left parent
        return t;
    }

    /// <summary>Left-to-right for text that starts with a Latin letter or digit (an English file name), else right-to-left.</summary>
    public static FlowDirection DirectionOf(string? s)
    {
        foreach (var c in s ?? "")
        {
            if (c is >= '\u0590' and <= '\u08FF' or >= '\uFB1D' and <= '\uFEFC') return FlowDirection.RightToLeft;
            if (char.IsLetter(c)) return FlowDirection.LeftToRight;
        }
        return FlowDirection.RightToLeft;
    }

    public static TextBlock Glyph(string g, double size = 16, Brush? fg = null) => new()
    {
        Text = g,
        FontFamily = Theme.Icons,
        FontSize = size,
        Foreground = fg ?? Theme.Ink,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        FlowDirection = FlowDirection.LeftToRight,
    };

    public static Button Button(string text, string? glyph = null, Style? style = null, Action? click = null)
    {
        var b = new Button { Style = style ?? Theme.Button };
        if (glyph is null) b.Content = text;
        else
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Glyph(glyph, 14, null));
            ((TextBlock)sp.Children[0]).SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground") { Source = b });
            var t = Text(text);
            t.SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground") { Source = b });
            t.Margin = new Thickness(8, 0, 0, 0);
            sp.Children.Add(t);
            b.Content = sp;
        }
        if (click is not null) b.Click += (_, _) => click();
        return b;
    }

    /// <summary>The big icon-over-text buttons of the main toolbar.</summary>
    public static Button Tool(string text, string glyph, Action click, Brush? color = null)
    {
        var sp = new StackPanel { MinWidth = 62 };
        sp.Children.Add(Glyph(glyph, 20, color ?? Theme.Gold));
        var t = Text(text, 11.5, Theme.Ink);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 4, 0, 0);
        sp.Children.Add(t);
        var b = new Button { Style = Theme.ToolButton, Content = sp, Padding = new Thickness(6, 6, 6, 5), Margin = new Thickness(1, 0, 1, 0), ToolTip = text };
        b.Click += (_, _) => click();
        return b;
    }

    public static TextBox Input(string text = "", bool multiline = false)
    {
        var t = new TextBox { Style = Theme.Input, Text = text };
        if (multiline)
        {
            t.AcceptsReturn = true;
            t.TextWrapping = TextWrapping.Wrap;
            t.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            t.VerticalContentAlignment = VerticalAlignment.Top;
        }
        return t;
    }

    public static CheckBox Check(string text, bool value) => new()
    {
        Style = Theme.CheckBox,
        Content = Text(text, 13, Theme.Ink, wrap: TextWrapping.Wrap),
        IsChecked = value,
        Foreground = Theme.Ink,
        Margin = new Thickness(0, 4, 0, 4),
        VerticalContentAlignment = VerticalAlignment.Center,
        Cursor = Cursors.Hand,
    };

    public static Border Card(UIElement child, Thickness? padding = null) => new()
    {
        Child = child,
        Background = Theme.Card,
        BorderBrush = Theme.Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = padding ?? new Thickness(14),
    };

    public static UIElement Label(string text) => new TextBlock
    {
        Text = text, FontSize = 12, Foreground = Theme.Muted, Margin = new Thickness(0, 10, 0, 4),
    };

    public static ImageSource AppIcon(int size = 256)
    {
        try
        {
            var uri = new Uri(size > 48 ? "pack://application:,,,/Assets/ibdm-256.png" : "pack://application:,,,/Assets/ibdm.ico");
            return BitmapFrame.Create(uri);
        }
        catch (Exception) { return new DrawingImage(); }
    }

    public static Image Logo(double size) => new() { Source = AppIcon(), Width = size, Height = size };

    /// <summary>Every window: dark, Arabic, right to left, dark title bar.</summary>
    public static void Setup(Window w, string title, double width, double height)
    {
        w.Title = title;
        w.Width = width;
        w.Height = height;
        w.Background = Theme.Bg;
        w.Foreground = Theme.Ink;
        w.FontFamily = Theme.Font;
        w.FontSize = 13;
        w.FlowDirection = FlowDirection.RightToLeft;
        w.Icon = AppIcon(32);
        w.UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);
        w.SourceInitialized += (_, _) => DarkTitleBar(w);
    }

    public static void DarkTitleBar(Window w)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            int on = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, 4);
            int caption = 0x00110F0F; // COLORREF 0x00BBGGRR of #0F0F11
            DwmSetWindowAttribute(hwnd, 35, ref caption, 4);
        }
        catch (Exception) { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Brings a window to the front even when another program (the browser) has focus.</summary>
    public static void ForceFront(Window w)
    {
        if (!w.IsVisible) w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Topmost = true;
        w.Activate();
        w.Topmost = false;
        w.Focus();
    }

    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception e) { Message(null, "تعذر الفتح", e.Message); }
    }

    public static void ShowInFolder(string file)
    {
        try
        {
            if (File.Exists(file)) Process.Start("explorer.exe", $"/select,\"{file}\"");
            else if (Directory.Exists(Path.GetDirectoryName(file))) Process.Start("explorer.exe", $"\"{Path.GetDirectoryName(file)}\"");
            else Message(null, "المجلد غير موجود", Path.GetDirectoryName(file) ?? file);
        }
        catch (Exception e) { Message(null, "تعذر فتح المجلد", e.Message); }
    }

    /// <summary>A dark question box. Returns the index of the button pressed, or -1 if closed.</summary>
    public static int Ask(Window? owner, string title, string message, params string[] buttons)
    {
        var w = new Window { ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.Height, ShowInTaskbar = owner is null };
        Setup(w, title, 440, 200);
        w.WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        if (owner is { IsVisible: true }) w.Owner = owner;
        var result = -1;
        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(Text(title, 16, Theme.Ink, bold: true));
        var msg = Text(message, 13, Theme.Muted, wrap: TextWrapping.Wrap);
        msg.Margin = new Thickness(0, 10, 0, 18);
        root.Children.Add(msg);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var b = Button(buttons[i], style: i == 0 ? Theme.PrimaryButton : Theme.Button, click: () => { result = index; w.Close(); });
            b.Margin = new Thickness(0, 0, 8, 0);
            if (i == 0) b.IsDefault = true;
            if (i == buttons.Length - 1) b.IsCancel = true;
            row.Children.Add(b);
        }
        root.Children.Add(row);
        w.Content = root;
        w.ShowDialog();
        return result;
    }

    public static void Message(Window? owner, string title, string message) => Ask(owner, title, message, "حسناً");
}
