using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IbdmPro.Core;

namespace IbdmPro.App;

/// <summary>Where downloads arrive from the browser or "Add link".</summary>
public static class Capture
{
    public static void Handle(CaptureRequest req)
    {
        if (req.Kind == "media" || Settings.Current.ShowInfoBeforeDownload) { InfoDialog.Open(req); return; }
        _ = Quick(req);
    }

    /// <summary>"Show file info before downloading" is off: check the link and start straight away.</summary>
    private static async Task Quick(CaptureRequest req)
    {
        var probe = await Http.ProbeAsync(req.Url, req.ToRequestInfo());
        if (probe.Error is not null || probe.LooksLikeWebPage) { InfoDialog.Open(req); return; }
        var s = Settings.Current;
        var name = FileNames.Clean(string.IsNullOrWhiteSpace(req.FileName) ? probe.FileName : Path.GetFileName(req.FileName));
        var cat = s.CategoryFor(name);
        var item = new DownloadItem
        {
            Kind = DownloadKind.File, Url = probe.FinalUrl, PageUrl = req.PageUrl, Request = req.ToRequestInfo(),
            FileName = name, TotalBytes = probe.Size, Resumable = probe.Resumable, Category = cat.Name, Folder = s.FolderFor(cat),
        };
        IbdmApp.Instance.Manager.Add(item, start: true);
        IbdmApp.Instance.Tray?.Notify("بدأ التحميل", $"{name} — {Fmt.Size(probe.Size)}", null);
    }
}

/// <summary>"معلومات الملف": shown before every download. It checks the link and shows the name, size and type,
/// lets the person pick the folder and category, and for videos lists every available quality with its size
/// (plus audio only), then starts now or adds the download for later.</summary>
public sealed class InfoDialog : Window
{
    private readonly CaptureRequest _req;
    private readonly CancellationTokenSource _cts = new();
    private readonly Settings _s = Settings.Current;

    private readonly StackPanel _body = new();
    private readonly TextBlock _headIcon = Ui.Glyph("", 26, Theme.Gold);
    private readonly Border _headTile = new() { Width = 52, Height = 52, CornerRadius = new CornerRadius(12), Background = Theme.GoldSoft };
    private readonly TextBlock _headTitle = Ui.Text("جاري فحص الرابط…", 16, Theme.Ink, bold: true);
    private readonly TextBlock _headSub = Ui.Text("", 12, Theme.Muted);
    private readonly Button _start, _later;

    private readonly TextBox _name = Ui.Input();
    private readonly TextBox _folder = Ui.Input();
    private readonly CheckBox _remember = Ui.Check("تذكر هذا المجلد لهذا النوع من الملفات", false);
    private readonly TextBlock _space = Ui.Text("", 12, Theme.Muted, wrap: TextWrapping.Wrap);
    private readonly TextBlock _sizeValue = Ui.Text("", 13, Theme.Ink, bold: true);
    private readonly TextBlock _typeValue = Ui.Text("", 13, Theme.Ink);
    private readonly WrapPanel _categories = new();
    private Category _category;
    private bool _folderEdited;

    private ProbeResult? _probe;
    private MediaInfo? _media;
    private MediaOption? _option;

    private InfoDialog(CaptureRequest req)
    {
        _req = req;
        Ui.Setup(this, "IBDM Pro — معلومات الملف", 640, 640);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        MinHeight = 360;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanMinimize;
        _category = _s.CategoryFor(req.FileName ?? FileNames.FromUrl(req.Url) ?? "");

        _start = Ui.Button("بدء التحميل", "", Theme.PrimaryButton, () => Finish(true));
        _later = Ui.Button("تحميل لاحقاً", "", Theme.Button, () => Finish(false));
        _start.IsEnabled = _later.IsEnabled = false;
        _start.IsDefault = true;
        var cancel = Ui.Button("إلغاء", style: Theme.Button, click: Close);
        cancel.IsCancel = true;

        // header
        _headTile.Child = _headIcon;
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(_headTile, Dock.Left);
        head.Children.Add(_headTile);
        var titles = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(_headTitle);
        titles.Children.Add(_headSub);
        head.Children.Add(titles);

        _body.Children.Add(new ProgressBar { Style = Theme.Progress, IsIndeterminate = true, Margin = new Thickness(0, 10, 0, 10) });
        _body.Children.Add(Ui.Text(_req.Url, 12, Theme.Muted));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        foreach (var b in new[] { _start, _later, cancel }) { b.Margin = new Thickness(0, 0, 8, 0); buttons.Children.Add(b); }

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        root.Children.Add(head);
        root.Children.Add(_body);
        root.Children.Add(buttons);
        root.Children.Add(Credit());
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        _folder.TextChanged += (_, _) => { UpdateSpace(); };
        _folder.PreviewKeyDown += (_, _) => _folderEdited = true;
        _name.TextChanged += (_, _) => UpdateSpace();
        Closed += (_, _) => _cts.Cancel();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += async (_, _) => { if (!_preview) await LoadAsync(); };
    }

    private bool _preview;

    /// <summary>For the build's snapshots: the dialog already filled with a checked file or video.</summary>
    public static InfoDialog Preview(CaptureRequest req, ProbeResult? probe, MediaInfo? media)
    {
        var d = new InfoDialog(req) { _preview = true, _probe = probe, _media = media };
        if (media is not null) d.ShowMedia(); else d.ShowFile();
        return d;
    }

    public static void Open(CaptureRequest req)
    {
        var d = new InfoDialog(req);
        d.Show();
        Ui.ForceFront(d);
    }

    private static UIElement Credit()
    {
        var t = Ui.Text("IBDM Pro — من تصميم و تطوير و تعريب ابراهيم الخزرجي", 11, Theme.Muted);
        t.Margin = new Thickness(0, 14, 0, 0);
        t.Opacity = 0.7;
        return t;
    }

    // ---------- checking the link ----------

    private async Task LoadAsync()
    {
        var info = _req.ToRequestInfo();
        var ct = _cts.Token;
        try
        {
            if (_req.Kind == "media")
            {
                // A video on a page: the page address gives yt-dlp every quality (YouTube, Facebook…);
                // otherwise the stream itself (m3u8/mp4); otherwise download the file as it is.
                var sources = new[] { _req.PageUrl, _req.Url }.Where(u => !string.IsNullOrEmpty(u) && !u!.StartsWith("blob:")).Distinct().ToList();
                foreach (var src in sources)
                {
                    _headTitle.Text = "جاري البحث عن الجودات المتوفرة…";
                    _headSub.Text = Host(src!);
                    try { _media = await YtDlp.GetInfoAsync(src!, info, ct); break; }
                    catch (InvalidOperationException e) { Log.Write($"media {src}: {e.Message}"); }
                }
                if (_media is not null && _media.Options.Count > 0) { ShowMedia(); return; }
                if (_req.Url.StartsWith("http"))
                {
                    _probe = await Http.ProbeAsync(_req.Url, info, ct);
                    if (_probe.Error is null && !_probe.LooksLikeWebPage) { ShowFile(); return; }
                }
                ShowError("لم يتم العثور على فيديو يمكن تحميله في هذه الصفحة.");
                return;
            }

            _probe = await Http.ProbeAsync(_req.Url, info, ct);
            if (_probe.Error is null && !_probe.LooksLikeWebPage) { ShowFile(); return; }

            // A page address (YouTube, Instagram…) pasted as a link: look for a video in it.
            _headTitle.Text = "جاري البحث عن فيديو في الصفحة…";
            try
            {
                _media = await YtDlp.GetInfoAsync(_req.Url, info, ct);
                if (_media.Options.Count > 0) { ShowMedia(); return; }
            }
            catch (InvalidOperationException e) { Log.Write($"media {_req.Url}: {e.Message}"); }

            if (_probe.Error is null) ShowFile(); // a web page with no video: download the page itself
            else ShowError(_probe.Error);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { ShowError(e.Message); }
    }

    private void ShowError(string message)
    {
        _headTitle.Text = "تعذر تحميل هذا الرابط";
        _headIcon.Text = "";
        _headIcon.Foreground = Theme.Red;
        _headTile.Background = Theme.Br("#33E5533D");
        _body.Children.Clear();
        _body.Children.Add(Ui.Card(Ui.Text(message, 13, Theme.Ink, wrap: TextWrapping.Wrap)));
        var url = Ui.Input(_req.Url);
        url.IsReadOnly = true;
        url.Margin = new Thickness(0, 10, 0, 0);
        _body.Children.Add(url);
        var retry = Ui.Button("إعادة المحاولة", "", Theme.Button, async () => { Reset(); await LoadAsync(); });
        retry.Margin = new Thickness(0, 10, 0, 0);
        retry.HorizontalAlignment = HorizontalAlignment.Left;
        _body.Children.Add(retry);
    }

    private void Reset()
    {
        _headIcon.Text = "";
        _headIcon.Foreground = Theme.Gold;
        _headTile.Background = Theme.GoldSoft;
        _headTitle.Text = "جاري فحص الرابط…";
        _body.Children.Clear();
        _body.Children.Add(new ProgressBar { Style = Theme.Progress, IsIndeterminate = true, Margin = new Thickness(0, 10, 0, 10) });
    }

    // ---------- a plain file ----------

    private void ShowFile()
    {
        var p = _probe!;
        var name = !string.IsNullOrWhiteSpace(_req.FileName) ? FileNames.Clean(Path.GetFileName(_req.FileName)) : p.FileName;
        if (Path.GetExtension(name).Length == 0 && Path.GetExtension(p.FileName).Length > 0) name += Path.GetExtension(p.FileName);
        var size = p.Size > 0 ? p.Size : _req.Size;
        _category = _s.CategoryFor(name);
        SetHead(_category, "معلومات الملف", Host(p.FinalUrl));

        _body.Children.Clear();
        _body.Children.Add(Ui.Label("اسم الملف"));
        _name.Text = name;
        _body.Children.Add(_name);

        var facts = new UniformGrid3();
        _sizeValue.Text = Fmt.Size(size);
        facts.Add("الحجم", _sizeValue);
        _typeValue.Text = TypeName(name, p.ContentType);
        facts.Add("النوع", _typeValue);
        facts.Add("التحميل المجزأ", Ui.Text(p.Resumable ? $"مدعوم ✓ ({_s.Connections} اتصالات)" : "غير مدعوم (اتصال واحد)", 13, p.Resumable ? Theme.Green : Theme.Muted));
        var card = Ui.Card(facts.Panel);
        card.Margin = new Thickness(0, 12, 0, 0);
        _body.Children.Add(card);

        AddWhere();
        _body.Children.Add(Ui.Label("الرابط"));
        var url = Ui.Input(p.FinalUrl);
        url.IsReadOnly = true;
        url.Foreground = Theme.Muted;
        url.FlowDirection = FlowDirection.LeftToRight;
        _body.Children.Add(url);

        _start.IsEnabled = _later.IsEnabled = true;
        _start.Focus();
        UpdateSpace();
    }

    // ---------- a video / audio page ----------

    private void ShowMedia()
    {
        var m = _media!;
        _body.Children.Clear();
        SetHead(_s.CategoryNamed("فيديو") ?? _category, "تحميل فيديو", (m.Site is { Length: > 0 } site && site != "Generic" ? site + " — " : "") + Host(m.Url));

        // thumbnail + title
        var top = new DockPanel();
        if (!string.IsNullOrEmpty(m.Thumbnail))
        {
            try
            {
                var img = new Image { Width = 160, Height = 90, Stretch = Stretch.UniformToFill, Source = new BitmapImage(new Uri(m.Thumbnail)) };
                var frame = new Border { Child = img, CornerRadius = new CornerRadius(8), ClipToBounds = true, Margin = new Thickness(0, 0, 14, 0), Background = Theme.Card };
                DockPanel.SetDock(frame, Dock.Left);
                top.Children.Add(frame);
            }
            catch (Exception) { }
        }
        var titleBox = new StackPanel();
        titleBox.Children.Add(Ui.Text(m.Title, 14, Theme.Ink, bold: true, wrap: TextWrapping.Wrap));
        if (m.Duration > 0) titleBox.Children.Add(Ui.Text("المدة: " + Fmt.Duration(m.Duration), 12, Theme.Muted));
        top.Children.Add(titleBox);
        _body.Children.Add(top);

        // qualities
        _body.Children.Add(Ui.Label("اختر الجودة (الحجم بجانب كل جودة)"));
        var list = new StackPanel();
        var defaultOption = m.Options.FirstOrDefault(o => !o.AudioOnly && o.Height is > 0 and <= 1080 && o.Ext == "mp4")
            ?? m.Options.FirstOrDefault(o => !o.AudioOnly) ?? m.Options[0];
        var addedAudioHeader = false;
        foreach (var o in m.Options)
        {
            if (o.AudioOnly && !addedAudioHeader)
            {
                addedAudioHeader = true;
                list.Children.Add(Ui.Label("صوت فقط"));
            }
            var row = new DockPanel();
            var size = Ui.Text(o.SizeText, 13, o.Size > 0 ? Theme.Gold : Theme.Muted, bold: o.Size > 0);
            DockPanel.SetDock(size, Dock.Right);
            row.Children.Add(size);
            var g = Ui.Glyph(o.AudioOnly ? "" : "", 15, o.AudioOnly ? Theme.Br("#A86BF0") : Theme.Br("#E5533D"));
            g.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(g, Dock.Left);
            row.Children.Add(g);
            row.Children.Add(Ui.Text(o.Label, 13, Theme.Ink));
            var rb = new RadioButton { Style = Theme.Choice, Content = row, GroupName = "q" + GetHashCode(), Tag = o, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            rb.Checked += (_, _) => SelectOption((MediaOption)rb.Tag);
            rb.MouseDoubleClick += (_, _) => { if (_start.IsEnabled) Finish(true); };
            list.Children.Add(rb);
            if (o == defaultOption) rb.IsChecked = true;
        }
        _body.Children.Add(list);

        _body.Children.Add(Ui.Label("اسم الملف"));
        _body.Children.Add(_name);
        AddWhere();

        _start.IsEnabled = _later.IsEnabled = true;
        SelectOption(defaultOption);
        _start.Focus();
    }

    private void SelectOption(MediaOption o)
    {
        _option = o;
        var stem = Path.GetFileNameWithoutExtension(_name.Text);
        if (string.IsNullOrWhiteSpace(stem) || _media is not null && _name.Text.Length == 0) stem = FileNames.Clean(_media?.Title, "video");
        _name.Text = stem + "." + o.Ext;
        var cat = o.AudioOnly ? _s.CategoryNamed("صوتيات") : _s.CategoryNamed("فيديو");
        if (cat is not null) SelectCategory(cat);
        UpdateSpace();
    }

    // ---------- where to save ----------

    private void AddWhere()
    {
        _body.Children.Add(Ui.Label("النوع (يحدد المجلد)"));
        _categories.Children.Clear();
        foreach (var c in _s.Categories)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Ui.Glyph(c.Icon, 13, Theme.Br(c.Color)));
            var t = Ui.Text(c.Name, 12.5);
            t.Margin = new Thickness(6, 0, 0, 0);
            sp.Children.Add(t);
            var chip = new RadioButton { Style = Theme.Choice, Content = sp, GroupName = "c" + GetHashCode(), Tag = c, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 6) };
            chip.Checked += (_, _) => SelectCategory((Category)chip.Tag, fromChip: true);
            _categories.Children.Add(chip);
        }
        _body.Children.Add(_categories);

        _body.Children.Add(Ui.Label("حفظ في"));
        var browse = Ui.Button("تغيير…", "", Theme.Button, Browse);
        browse.Margin = new Thickness(8, 0, 0, 0);
        var row = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(browse);
        _folder.FlowDirection = FlowDirection.LeftToRight;
        row.Children.Add(_folder);
        _body.Children.Add(row);
        _body.Children.Add(_remember);
        _space.Margin = new Thickness(0, 2, 0, 0);
        _body.Children.Add(_space);
        SelectCategory(_category);
    }

    private void SelectCategory(Category c, bool fromChip = false)
    {
        _category = c;
        foreach (RadioButton chip in _categories.Children)
            if (chip.Tag == c && chip.IsChecked != true) chip.IsChecked = true;
        if (!_folderEdited || fromChip)
        {
            _folder.Text = _s.FolderFor(c);
            _folderEdited = false;
        }
        SetHeadIcon(c);
    }

    private void Browse()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "اختر مجلد الحفظ" };
        try { Directory.CreateDirectory(_folder.Text); dlg.InitialDirectory = _folder.Text; } catch (Exception) { }
        if (dlg.ShowDialog(this) == true)
        {
            _folder.Text = dlg.FolderName;
            _folderEdited = true;
        }
    }

    private void UpdateSpace()
    {
        if (_folder.Text.Length == 0) return;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(_folder.Text));
            var free = root is null ? -1 : new DriveInfo(root).AvailableFreeSpace;
            var need = _option?.Size ?? (_probe?.Size > 0 ? _probe.Size : _req.Size);
            var path = Path.Combine(_folder.Text, FileNames.Clean(_name.Text));
            var exists = File.Exists(path) ? $"\nيوجد ملف بنفس الاسم — سيتم الحفظ باسم «{Path.GetFileName(FileNames.Unique(path))}»" : "";
            if (free >= 0 && need > 0 && free < need)
            {
                _space.Text = $"⚠ المساحة غير كافية: المتوفر {Fmt.Size(free)} والملف يحتاج {Fmt.Size(need)}" + exists;
                _space.Foreground = Theme.Red;
            }
            else
            {
                _space.Text = (free >= 0 ? $"المساحة المتوفرة على القرص: {Fmt.Size(free)}" : "") + exists;
                _space.Foreground = exists.Length > 0 ? Theme.Gold : Theme.Muted;
            }
        }
        catch (Exception) { _space.Text = ""; }
    }

    // ---------- start ----------

    private void Finish(bool startNow)
    {
        var name = FileNames.Clean(_name.Text.Trim());
        if (name.Length == 0) { _name.Focus(); return; }
        var folder = _folder.Text.Trim();
        try
        {
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
        }
        catch (Exception e) { Ui.Message(this, "المجلد غير صالح", e.Message); return; }

        if (_remember.IsChecked == true)
        {
            _category.Folder = folder;
            _s.Save();
        }

        DownloadItem item;
        if (_media is not null && _option is not null)
        {
            var o = _option;
            if (Path.GetExtension(name).TrimStart('.') != o.Ext) name = Path.GetFileNameWithoutExtension(name) + "." + o.Ext;
            var unique = Path.GetFileName(FileNames.Unique(Path.Combine(folder, name)));
            item = new DownloadItem
            {
                Kind = DownloadKind.Media, Url = _media.Url, PageUrl = _req.PageUrl ?? _media.Url, Request = _req.ToRequestInfo(),
                FileName = unique, Folder = folder, Category = _category.Name, TotalBytes = o.Size,
                FormatSpec = o.FormatSpec, AudioOnly = o.AudioOnly, AudioFormat = o.AudioFormat, Quality = o.Label,
            };
        }
        else if (_probe is not null)
        {
            item = new DownloadItem
            {
                Kind = DownloadKind.File, Url = _probe.FinalUrl, PageUrl = _req.PageUrl, Request = _req.ToRequestInfo(),
                FileName = name, Folder = folder, Category = _category.Name,
                TotalBytes = _probe.Size, Resumable = _probe.Resumable,
            };
        }
        else return;

        IbdmApp.Instance.Manager.Add(item, startNow);
        Close();
    }

    // ---------- helpers ----------

    private void SetHead(Category c, string title, string sub)
    {
        _headTitle.Text = title;
        _headSub.Text = sub;
        SetHeadIcon(c);
    }

    private void SetHeadIcon(Category c)
    {
        _headIcon.Text = c.Icon;
        _headIcon.Foreground = Theme.Br(c.Color);
        var col = (Color)ColorConverter.ConvertFromString(c.Color);
        var b = new SolidColorBrush(Color.FromArgb(0x30, col.R, col.G, col.B));
        b.Freeze();
        _headTile.Background = b;
    }

    private static string Host(string url)
    {
        try { return new Uri(url).Host; } catch (Exception) { return url; }
    }

    private string TypeName(string name, string? mime)
    {
        var ext = FileNames.ExtOf(name).ToUpperInvariant();
        var cat = _s.CategoryFor(name).Name;
        return ext.Length > 0 ? $"{ext} — {cat}" : (mime ?? cat);
    }

    /// <summary>Three label/value pairs side by side.</summary>
    private sealed class UniformGrid3
    {
        public readonly System.Windows.Controls.Primitives.UniformGrid Panel = new() { Rows = 1 };

        public void Add(string label, UIElement value)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(label, 11.5, Theme.Muted));
            if (value is FrameworkElement fe) fe.Margin = new Thickness(0, 4, 8, 0);
            sp.Children.Add(value);
            Panel.Children.Add(sp);
        }
    }
}
