using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using IbdmPro.Core;

namespace IbdmPro.App;

/// <summary>"إضافة رابط": paste any link — a file, or a page with a video (YouTube, Instagram, Facebook…).</summary>
public static class AddUrlDialog
{
    public static string? Ask(Window owner)
    {
        var w = new Window { ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        Ui.Setup(w, "إضافة رابط", 560, 220);
        if (owner.IsVisible) w.Owner = owner;
        string? result = null;
        var input = Ui.Input();
        input.FlowDirection = FlowDirection.LeftToRight;
        try
        {
            var clip = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
            if (Uri.TryCreate(clip, UriKind.Absolute, out var u) && u.Scheme.StartsWith("http")) input.Text = clip;
        }
        catch (Exception) { }

        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(Ui.Text("إضافة رابط تحميل", 16, Theme.Ink, bold: true));
        root.Children.Add(Ui.Text("الصق رابط ملف، أو رابط صفحة فيها فيديو (يوتيوب، انستغرام، فيسبوك، تيك توك…)", 12, Theme.Muted, wrap: TextWrapping.Wrap));
        input.Margin = new Thickness(0, 14, 0, 16);
        root.Children.Add(input);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var ok = Ui.Button("متابعة", "", Theme.PrimaryButton, () =>
        {
            var t = input.Text.Trim();
            if (!t.StartsWith("http", StringComparison.OrdinalIgnoreCase)) t = "https://" + t;
            if (Uri.TryCreate(t, UriKind.Absolute, out _)) { result = t; w.Close(); }
            else input.Focus();
        });
        ok.IsDefault = true;
        var cancel = Ui.Button("إلغاء", style: Theme.Button, click: w.Close);
        cancel.IsCancel = true;
        cancel.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(ok);
        row.Children.Add(cancel);
        root.Children.Add(row);
        w.Content = root;
        w.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        w.ShowDialog();
        return result;
    }
}

/// <summary>How to add the browser extension (browsers do not allow programs to install extensions themselves).</summary>
public static class ExtensionGuide
{
    public static void Show(Window owner)
    {
        var w = new Window { ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        Ui.Setup(w, "تثبيت إضافة المتصفح", 600, 560);
        if (owner.IsVisible) w.Owner = owner;
        var root = new StackPanel { Margin = new Thickness(24) };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(Ui.Logo(44));
        var titles = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Ui.Text("إضافة IBDM Pro للمتصفح", 17, Theme.Ink, bold: true));
        titles.Children.Add(Ui.Text("حتى يلتقط البرنامج أي تحميل من أي موقع بدل المتصفح", 12, Theme.Muted));
        head.Children.Add(titles);
        root.Children.Add(head);

        string[] steps =
        [
            "اضغط «فتح مجلد الإضافة» بالأسفل وخلي النافذة مفتوحة.",
            "اضغط «فتح صفحة الإضافات» (Chrome أو Edge).",
            "شغّل «وضع المطوّر / Developer mode» من أعلى الصفحة.",
            "اضغط «تحميل إضافة غير مُجمَّعة / Load unpacked» واختر مجلد extension.",
            "ثبّت أيقونة IBDM Pro بشريط المتصفح — وخلص! جرّب تحمّل أي ملف.",
        ];
        var list = new StackPanel { Margin = new Thickness(0, 18, 0, 8) };
        for (var i = 0; i < steps.Length; i++)
        {
            var r = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var n = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Theme.GoldSoft, Child = Ui.Text((i + 1).ToString(), 13, Theme.Gold, bold: true) };
            ((TextBlock)n.Child).HorizontalAlignment = HorizontalAlignment.Center;
            DockPanel.SetDock(n, Dock.Left);
            r.Children.Add(n);
            var t = Ui.Text(steps[i], 13, Theme.Ink, wrap: TextWrapping.Wrap);
            t.Margin = new Thickness(12, 0, 0, 0);
            r.Children.Add(t);
            list.Children.Add(r);
        }
        root.Children.Add(list);

        var buttons = new WrapPanel();
        void Add(string text, string glyph, Style style, Action a)
        {
            var b = Ui.Button(text, glyph, style, a);
            b.Margin = new Thickness(0, 0, 8, 8);
            buttons.Children.Add(b);
        }
        Add("فتح مجلد الإضافة", "", Theme.PrimaryButton, OpenExtensionFolder);
        Add("فتح صفحة الإضافات — Chrome", "", Theme.Button, () => Browser("chrome", "chrome://extensions"));
        Add("فتح صفحة الإضافات — Edge", "", Theme.Button, () => Browser("msedge", "edge://extensions"));
        root.Children.Add(buttons);

        var note = Ui.Text("بعد كل تحديث للبرنامج: اضغط زر التحديث ⟳ على بطاقة الإضافة بصفحة الإضافات.\nإذا كان البرنامج مسكّر، المتصفح يحمّل بشكل عادي — لذلك خليه يشتغل مع بدء Windows.", 12, Theme.Muted, wrap: TextWrapping.Wrap);
        note.Margin = new Thickness(0, 8, 0, 14);
        root.Children.Add(note);
        var close = Ui.Button("تم", style: Theme.Button, click: w.Close);
        close.IsCancel = true;
        close.HorizontalAlignment = HorizontalAlignment.Left;
        root.Children.Add(close);
        w.Content = root;
        w.ShowDialog();
    }

    public static void OpenExtensionFolder()
    {
        var dir = Paths.ExtensionDir;
        if (Directory.Exists(dir)) Ui.Open(dir);
        else Ui.Message(null, "مجلد الإضافة غير موجود", dir);
    }

    private static void Browser(string exe, string page)
    {
        try { Process.Start(new ProcessStartInfo(exe, page) { UseShellExecute = true }); }
        catch (Exception)
        {
            try { Clipboard.SetText(page); } catch (Exception) { }
            Ui.Message(null, "تعذر فتح المتصفح", $"افتح المتصفح والصق هذا العنوان (تم نسخه):\n{page}");
        }
    }
}

public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Ui.Setup(this, "حول IBDM Pro", 460, 520);
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(28), HorizontalAlignment = HorizontalAlignment.Center };
        var logo = Ui.Logo(96);
        logo.Margin = new Thickness(0, 0, 0, 12);
        root.Children.Add(logo);
        var name = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Center };
        name.Children.Add(Ui.Text("IBDM", 26, Theme.Ink, bold: true));
        name.Children.Add(Ui.Text(" Pro", 26, Theme.Gold, bold: true));
        root.Children.Add(name);
        root.Children.Add(Center(Ui.Text("الإصدار " + Program.Version, 13, Theme.Muted)));
        root.Children.Add(Center(Ui.Text("مدير التحميل — يلتقط التحميلات من المتصفح، يقسّم الملف على عدة اتصالات، ويحمّل الفيديو بالجودة اللي تختارها", 13, Theme.Ink, wrap: TextWrapping.Wrap), 14));

        var credit = Ui.Card(new StackPanel
        {
            Children =
            {
                Center(Ui.Text("من تصميم و تطوير و تعريب", 13, Theme.Muted)),
                Center(Ui.Text("ابراهيم الخزرجي", 20, Theme.Gold, bold: true)),
            },
        });
        credit.Margin = new Thickness(0, 18, 0, 14);
        root.Children.Add(credit);

        root.Children.Add(Center(Link("قناة البرامج على تيليكرام: t.me/ibraprogram", "https://t.me/ibraprogram")));
        root.Children.Add(Center(Ui.Text("للطلبات البرمجية: 07818814000", 13, Theme.Ink)));
        root.Children.Add(Center(Link("صفحة البرنامج والتحديثات على GitHub", "https://github.com/ibrata2/ibdm-pro/releases/latest")));
        root.Children.Add(Center(Ui.Text("يستخدم yt-dlp و FFmpeg (برامج مفتوحة المصدر)", 11, Theme.Muted), 14));
        var upd = Ui.Button("البحث عن أحدث إصدار", "\uE895", Theme.PrimaryButton, () => Updater.ShowWindow(this));
        upd.HorizontalAlignment = HorizontalAlignment.Center;
        upd.Margin = new Thickness(0, 16, 0, 0);
        root.Children.Add(upd);
        var ok = Ui.Button("إغلاق", style: Theme.Button, click: Close);
        ok.IsCancel = true;
        ok.HorizontalAlignment = HorizontalAlignment.Center;
        ok.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(ok);
        Content = root;
    }

    private static UIElement Center(TextBlock t, double top = 4)
    {
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.TextAlignment = TextAlignment.Center;
        t.Margin = new Thickness(0, top, 0, 0);
        return t;
    }

    private static TextBlock Link(string text, string url)
    {
        var h = new Hyperlink(new Run(text)) { Foreground = Theme.Gold, Cursor = System.Windows.Input.Cursors.Hand };
        h.Click += (_, _) => Ui.Open(url);
        return new TextBlock(h) { FontSize = 13 };
    }
}

/// <summary>Every setting, in five sections.</summary>
public sealed class SettingsWindow : Window
{
    private readonly Settings _s;
    private readonly DownloadManager _manager;
    private readonly ContentControl _page = new();
    private readonly Dictionary<string, UIElement> _pages = [];

    // general
    private readonly TextBox _root = Ui.Input();
    private readonly CheckBox _startup, _tray, _notify, _showInfo, _resume, _updates;
    private readonly RadioButton _themeAuto = new(), _themeDark = new(), _themeLight = new();
    // speed
    private readonly Slider _conn = new() { Minimum = 1, Maximum = 32, IsSnapToTickEnabled = true, TickFrequency = 1 };
    private readonly Slider _parallel = new() { Minimum = 1, Maximum = 10, IsSnapToTickEnabled = true, TickFrequency = 1 };
    private readonly TextBox _limit = Ui.Input();
    // capture
    private readonly CheckBox _capture;
    private readonly TextBox _exts = Ui.Input(multiline: true);
    private readonly TextBox _minSize = Ui.Input();
    private readonly TextBox _excluded = Ui.Input(multiline: true);
    // categories
    private readonly StackPanel _catRows = new();
    private readonly List<(Category Cat, TextBox Name, TextBox Folder, TextBox Exts)> _catEdits = [];
    // engine
    private readonly TextBlock _engineInfo = Ui.Text("", 12, Theme.Muted, wrap: TextWrapping.Wrap);

    public SettingsWindow(DownloadManager manager)
    {
        _manager = manager;
        _s = Settings.Load(); // a copy: Cancel throws it away
        Ui.Setup(this, "الإعدادات — IBDM Pro", 820, 600);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MinWidth = 700;
        MinHeight = 480;

        _startup = Ui.Check("تشغيل IBDM Pro مع بدء Windows (مهم حتى يلتقط التحميلات دائماً)", _s.StartWithWindows);
        _tray = Ui.Check("عند إغلاق النافذة يبقى البرنامج يعمل بجانب الساعة", _s.MinimizeToTray);
        _notify = Ui.Check("إشعار عند اكتمال التحميل", _s.NotifyOnComplete);
        _showInfo = Ui.Check("عرض نافذة «معلومات الملف» (الحجم والنوع ومكان الحفظ) قبل كل تحميل", _s.ShowInfoBeforeDownload);
        _resume = Ui.Check("استئناف التحميلات غير المكتملة تلقائياً عند تشغيل البرنامج", _s.ResumeOnStart);
        _capture = Ui.Check("التقاط التحميلات من المتصفح", _s.CaptureEnabled);
        _updates = Ui.Check("البحث عن تحديث تلقائياً عند تشغيل البرنامج", _s.CheckUpdatesOnStart);

        _pages["general"] = General();
        _pages["speed"] = Speed();
        _pages["capture"] = CapturePage();
        _pages["types"] = Types();
        _pages["engine"] = Engine();

        var nav = new StackPanel { Margin = new Thickness(10, 14, 10, 14) };
        void NavItem(string key, string text, string glyph)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Ui.Glyph(glyph, 14, Theme.Gold));
            var t = Ui.Text(text, 13);
            t.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(t);
            var rb = new RadioButton { Style = Theme.Nav, Content = row, GroupName = "settings" };
            rb.Checked += (_, _) => _page.Content = _pages[key];
            nav.Children.Add(rb);
            if (key == "general") rb.IsChecked = true;
        }
        NavItem("general", "عام", "");
        NavItem("speed", "السرعة والاتصالات", "");
        NavItem("capture", "الالتقاط من المتصفح", "");
        NavItem("types", "الأنواع والمجلدات", "");
        NavItem("engine", "الإضافة ومحرك الفيديو", "");

        var side = new Border { Width = 210, Background = Theme.Panel, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = nav };
        var save = Ui.Button("حفظ", "", Theme.PrimaryButton, Save);
        save.IsDefault = true;
        var cancel = Ui.Button("إلغاء", style: Theme.Button, click: Close);
        cancel.IsCancel = true;
        cancel.Margin = new Thickness(8, 0, 0, 0);
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 10, 20, 14) };
        bottom.Children.Add(save);
        bottom.Children.Add(cancel);

        var main = new DockPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        main.Children.Add(bottom);
        main.Children.Add(new ScrollViewer { Content = _page, Padding = new Thickness(22, 16, 22, 4), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var root = new DockPanel();
        DockPanel.SetDock(side, Dock.Left);
        root.Children.Add(side);
        root.Children.Add(main);
        Content = root;
    }

    private static StackPanel Page(string title, string? sub = null)
    {
        var p = new StackPanel();
        p.Children.Add(Ui.Text(title, 18, Theme.Ink, bold: true));
        if (sub is not null) p.Children.Add(Ui.Text(sub, 12, Theme.Muted, wrap: TextWrapping.Wrap));
        return p;
    }

    private UIElement General()
    {
        var p = Page("عام");
        p.Children.Add(Ui.Label("المجلد الرئيسي للتحميلات (يُنشأ بداخله مجلد لكل نوع)"));
        _root.Text = _s.DownloadRoot;
        _root.FlowDirection = FlowDirection.LeftToRight;
        var browse = Ui.Button("تغيير…", "", Theme.Button, () =>
        {
            var d = new Microsoft.Win32.OpenFolderDialog { Title = "اختر المجلد الرئيسي للتحميلات" };
            if (d.ShowDialog(this) == true) _root.Text = Path.Combine(d.FolderName, Path.GetFileName(d.FolderName).Equals("IBDM Pro", StringComparison.OrdinalIgnoreCase) ? "" : "IBDM Pro").TrimEnd('\\');
        });
        browse.Margin = new Thickness(8, 0, 0, 0);
        var row = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(browse);
        row.Children.Add(_root);
        p.Children.Add(row);
        var spacer = new Border { Height = 12 };
        p.Children.Add(spacer);
        foreach (var c in new[] { _startup, _tray, _notify, _showInfo, _resume, _updates }) p.Children.Add(c);

        p.Children.Add(Ui.Label("المظهر"));
        var themes = new WrapPanel();
        void ThemeChoice(RadioButton rb, string text, string glyph, string key)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Ui.Glyph(glyph, 14, Theme.Gold));
            var t = Ui.Text(text, 13);
            t.Margin = new Thickness(8, 0, 0, 0);
            sp.Children.Add(t);
            rb.Style = Theme.Choice;
            rb.Content = sp;
            rb.GroupName = "theme";
            rb.Margin = new Thickness(0, 0, 8, 6);
            rb.IsChecked = _s.Theme == key;
            themes.Children.Add(rb);
        }
        ThemeChoice(_themeAuto, "تلقائي — حسب Windows", "\uE7F4", "auto");
        ThemeChoice(_themeDark, "ليلي", "\uE708", "dark");
        ThemeChoice(_themeLight, "نهاري", "\uE706", "light");
        if (_themeAuto.IsChecked != true && _themeDark.IsChecked != true && _themeLight.IsChecked != true) _themeAuto.IsChecked = true;
        p.Children.Add(themes);
        p.Children.Add(Ui.Text($"الوضع الحالي: {(Theme.IsDark ? "ليلي" : "نهاري")}. تغيير المظهر يحتاج إعادة تشغيل البرنامج.", 12, Theme.Muted, wrap: TextWrapping.Wrap));

        var check = Ui.Button("البحث عن أحدث إصدار", "\uE895", Theme.Button, () => Updater.ShowWindow(this));
        check.HorizontalAlignment = HorizontalAlignment.Left;
        check.Margin = new Thickness(0, 16, 0, 0);
        p.Children.Add(check);
        return p;
    }

    private UIElement Speed()
    {
        var p = Page("السرعة والاتصالات", "كل ملف يتقسّم على عدة اتصالات تحمّل بنفس الوقت — هذا يسرّع التحميل كثيراً.");
        p.Children.Add(Ui.Label("عدد الاتصالات لكل ملف"));
        p.Children.Add(SliderRow(_conn, _s.Connections, v => $"{v} اتصال"));
        p.Children.Add(Ui.Label("عدد التحميلات بنفس الوقت"));
        p.Children.Add(SliderRow(_parallel, _s.MaxParallel, v => $"{v} تحميل"));
        p.Children.Add(Ui.Label("حد أقصى للسرعة KB/ث (0 = بدون حد)"));
        _limit.Text = _s.SpeedLimitKBps.ToString();
        _limit.Width = 140;
        _limit.HorizontalAlignment = HorizontalAlignment.Left;
        p.Children.Add(_limit);
        return p;
    }

    private static UIElement SliderRow(Slider s, int value, Func<int, string> label)
    {
        s.Value = value;
        var t = Ui.Text(label(value), 13, Theme.Gold, bold: true);
        t.Width = 90;
        s.ValueChanged += (_, _) => t.Text = label((int)s.Value);
        var row = new DockPanel();
        DockPanel.SetDock(t, Dock.Right);
        row.Children.Add(t);
        row.Children.Add(s);
        return row;
    }

    private UIElement CapturePage()
    {
        var p = Page("الالتقاط من المتصفح", "عندما تحمّل ملف من أي موقع، الإضافة توقف تحميل المتصفح وترسله لـ IBDM Pro.");
        p.Children.Add(_capture);
        p.Children.Add(Ui.Label("أنواع الملفات التي يلتقطها البرنامج (امتدادات مفصولة بمسافة)"));
        _exts.Text = string.Join(" ", _s.CaptureExtensions);
        _exts.Height = 110;
        _exts.FlowDirection = FlowDirection.LeftToRight;
        p.Children.Add(_exts);
        var reset = Ui.Button("استعادة القائمة الافتراضية", "", Theme.Button, () => _exts.Text = string.Join(" ", Settings.DefaultCaptureExtensions()));
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Margin = new Thickness(0, 6, 0, 0);
        p.Children.Add(reset);
        p.Children.Add(Ui.Label("لا تلتقط الملفات الأصغر من (KB) — 0 = التقط الكل"));
        _minSize.Text = _s.MinCaptureSizeKB.ToString();
        _minSize.Width = 140;
        _minSize.HorizontalAlignment = HorizontalAlignment.Left;
        p.Children.Add(_minSize);
        p.Children.Add(Ui.Label("مواقع يتركها البرنامج للمتصفح (موقع بكل سطر)"));
        _excluded.Text = string.Join(Environment.NewLine, _s.ExcludedSites);
        _excluded.Height = 90;
        _excluded.FlowDirection = FlowDirection.LeftToRight;
        p.Children.Add(_excluded);
        p.Children.Add(Ui.Text("تلميح: إذا تريد المتصفح يحمّل ملف معيّن بنفسه، عطّل الالتقاط مؤقتاً من أيقونة الإضافة.", 12, Theme.Muted, wrap: TextWrapping.Wrap));
        return p;
    }

    private UIElement Types()
    {
        var p = Page("الأنواع والمجلدات", "كل نوع ملفات يُحفظ بمجلد خاص داخل مجلد IBDM Pro. تقدر تغيّر اسم المجلد أو تختار مجلد بمكان ثاني.");
        p.Children.Add(_catRows);
        foreach (var c in _s.Categories) AddCatRow(c);
        var add = Ui.Button("إضافة نوع جديد", "", Theme.Button, () =>
        {
            var c = new Category { Name = "نوع جديد", Folder = "نوع جديد", Icon = "", Color = "#9AA0A6" };
            _s.Categories.Insert(Math.Max(0, _s.Categories.Count - 1), c);
            AddCatRow(c);
        });
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 8, 0, 0);
        p.Children.Add(add);
        return p;
    }

    private void AddCatRow(Category c)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        var icon = Ui.Glyph(c.Icon, 16, Theme.Br(c.Color));
        Grid.SetRowSpan(icon, 2);
        grid.Children.Add(icon);
        var name = Ui.Input(c.Name);
        name.Margin = new Thickness(6, 0, 6, 4);
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        var folder = Ui.Input(c.Folder);
        folder.FlowDirection = FlowDirection.LeftToRight;
        folder.Margin = new Thickness(0, 0, 6, 4);
        folder.ToolTip = "اسم المجلد داخل مجلد IBDM Pro، أو مسار كامل";
        Grid.SetColumn(folder, 2);
        grid.Children.Add(folder);
        var browse = Ui.Button("…", style: Theme.Button, click: () =>
        {
            var d = new Microsoft.Win32.OpenFolderDialog { Title = "مجلد " + c.Name };
            if (d.ShowDialog(this) == true) folder.Text = d.FolderName;
        });
        browse.Padding = new Thickness(10, 5, 10, 5);
        browse.Margin = new Thickness(0, 0, 0, 4);
        Grid.SetColumn(browse, 3);
        grid.Children.Add(browse);
        var exts = Ui.Input(string.Join(" ", c.Extensions));
        exts.FlowDirection = FlowDirection.LeftToRight;
        exts.FontSize = 12;
        exts.Margin = new Thickness(6, 0, 0, 0);
        exts.ToolTip = c.Name == Settings.OtherCategory ? "كل ملف لا ينتمي لنوع آخر" : "الامتدادات مفصولة بمسافة";
        if (c.Name == Settings.OtherCategory) { exts.IsEnabled = false; exts.Text = "(أي ملف آخر)"; name.IsEnabled = false; }
        Grid.SetRow(exts, 1);
        Grid.SetColumn(exts, 1);
        Grid.SetColumnSpan(exts, 3);
        grid.Children.Add(exts);
        var card = Ui.Card(grid, new Thickness(10));
        card.Margin = new Thickness(0, 10, 0, 0);
        _catRows.Children.Add(card);
        _catEdits.Add((c, name, folder, exts));
    }

    private UIElement Engine()
    {
        var p = Page("الإضافة ومحرك الفيديو");
        var server = IbdmApp.Instance.Server;
        var connected = server is not null && (DateTime.Now - server.LastExtensionContact).TotalMinutes < 3;
        p.Children.Add(Ui.Label("إضافة المتصفح"));
        p.Children.Add(Ui.Text(server is null ? "⚠ خادم الإضافة متوقف (المنفذ مستخدم)" : connected ? "✓ الإضافة متصلة بالبرنامج" : "الإضافة لم تتصل بعد — ثبّتها من الزر بالأسفل",
            13, server is null ? Theme.Red : connected ? Theme.Green : Theme.Muted));
        var guide = Ui.Button("طريقة تثبيت الإضافة", "", Theme.PrimaryButton, () => ExtensionGuide.Show(this));
        guide.HorizontalAlignment = HorizontalAlignment.Left;
        guide.Margin = new Thickness(0, 8, 0, 0);
        p.Children.Add(guide);

        p.Children.Add(Ui.Label("محرك تحميل الفيديو (yt-dlp)"));
        p.Children.Add(Ui.Text("مواقع الفيديو تغيّر طريقة عملها باستمرار. إذا فشل تحميل فيديو من موقع كان يشتغل، حدّث المحرك من هنا.", 12, Theme.Muted, wrap: TextWrapping.Wrap));
        var update = Ui.Button("تحديث محرك الفيديو", "", Theme.Button, null);
        update.Click += async (_, _) =>
        {
            update.IsEnabled = false;
            _engineInfo.Text = "جاري التحديث…";
            try { _engineInfo.Text = await YtDlp.UpdateAsync(); }
            catch (Exception e) { _engineInfo.Text = "تعذر التحديث: " + e.Message; }
            update.IsEnabled = true;
        };
        update.HorizontalAlignment = HorizontalAlignment.Left;
        update.Margin = new Thickness(0, 8, 0, 6);
        p.Children.Add(update);
        _engineInfo.FlowDirection = FlowDirection.LeftToRight;
        p.Children.Add(_engineInfo);

        p.Children.Add(Ui.Label("ملفات البرنامج"));
        var data = Ui.Button("فتح مجلد بيانات البرنامج", "", Theme.Button, () => Ui.Open(Paths.AppData));
        data.HorizontalAlignment = HorizontalAlignment.Left;
        p.Children.Add(data);
        return p;
    }

    private void Save()
    {
        _s.DownloadRoot = _root.Text.Trim().Length > 0 ? _root.Text.Trim() : new Settings().DownloadRoot;
        _s.StartWithWindows = _startup.IsChecked == true;
        _s.MinimizeToTray = _tray.IsChecked == true;
        _s.NotifyOnComplete = _notify.IsChecked == true;
        _s.ShowInfoBeforeDownload = _showInfo.IsChecked == true;
        _s.ResumeOnStart = _resume.IsChecked == true;
        _s.CheckUpdatesOnStart = _updates.IsChecked == true;
        var oldTheme = Settings.Current.Theme;
        _s.Theme = _themeDark.IsChecked == true ? "dark" : _themeLight.IsChecked == true ? "light" : "auto";
        _s.Connections = (int)_conn.Value;
        _s.MaxParallel = (int)_parallel.Value;
        _s.SpeedLimitKBps = int.TryParse(_limit.Text.Trim(), out var lim) && lim > 0 ? lim : 0;
        _s.CaptureEnabled = _capture.IsChecked == true;
        _s.CaptureExtensions = Words(_exts.Text);
        _s.MinCaptureSizeKB = int.TryParse(_minSize.Text.Trim(), out var min) && min > 0 ? min : 0;
        _s.ExcludedSites = _excluded.Text.Split(['\n', '\r', ' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().Replace("https://", "").Replace("http://", "").TrimEnd('/')).Where(x => x.Length > 0).Distinct().ToList();

        foreach (var (cat, name, folder, exts) in _catEdits)
        {
            if (cat.Name != Settings.OtherCategory)
            {
                var n = name.Text.Trim();
                if (n.Length > 0 && n != cat.Name && _s.Categories.All(c => c == cat || c.Name != n))
                {
                    foreach (var i in _manager.Items.Where(i => i.Category == cat.Name)) i.Category = n;
                    cat.Name = n;
                }
                cat.Extensions = Words(exts.Text);
            }
            cat.Folder = folder.Text.Trim().Length > 0 ? folder.Text.Trim() : cat.Name;
        }

        _s.Save();
        AutoStart.Apply(_s.StartWithWindows);
        _manager.Limiter.BytesPerSecond = _s.SpeedLimitKBps * 1024L;
        _manager.Save();
        Close();

        var willBeDark = _s.Theme == "dark" || (_s.Theme == "auto" && Theme.WindowsIsDark());
        if (_s.Theme != oldTheme && willBeDark != Theme.IsDark
            && Ui.Ask(Owner, "تغيير المظهر", "إعادة تشغيل IBDM Pro الآن لتطبيق الألوان الجديدة؟ (التحميلات تكمل بعد التشغيل)", "إعادة التشغيل الآن", "لاحقاً") == 0)
            IbdmApp.Instance.Restart();
    }

    private static List<string> Words(string text) =>
        text.Split([' ', ',', '\n', '\r', '\t', '؛', '،'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().TrimStart('.').ToLowerInvariant()).Where(x => x.Length > 0).Distinct().ToList();
}
