using System.Windows;
using System.Windows.Controls;
using IbdmPro.Core;

namespace IbdmPro.App;

/// <summary>"ألبوم Pinterest": paste a board's link; every picture (original size) and video (best quality) of the
/// board is downloaded into a folder named after the board (board sections go in sub-folders).</summary>
public sealed class PinterestWindow : Window
{
    private readonly DownloadManager _manager;
    private readonly TextBox _link = Ui.Input();
    private readonly TextBox _folder = Ui.Input();
    private readonly ComboBox _queue = new() { MinWidth = 160 };
    private readonly CheckBox _startNow = Ui.Check(L.T("ابدأ التحميل الآن (وإلا تُضاف إلى القائمة متوقفة)"), true);
    private readonly TextBlock _status = Ui.Text("", 12.5, Theme.Muted, wrap: TextWrapping.Wrap);
    private readonly StackPanel _result = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock _boardName = Ui.Text("", 15, Theme.Ink, bold: true, wrap: TextWrapping.Wrap);
    private readonly TextBlock _counts = Ui.Text("", 13, Theme.Ink, wrap: TextWrapping.Wrap);
    private readonly Button _read, _download;
    private Pinterest.Board? _board;
    private List<Pinterest.Media> _media = [];
    private CancellationTokenSource? _cts;

    public static void Show(Window? owner, DownloadManager manager, string? url = null)
    {
        var w = new PinterestWindow(manager) { Owner = owner is { IsVisible: true } ? owner : null };
        if (!string.IsNullOrWhiteSpace(url)) { w._link.Text = url.Trim(); w.Loaded += async (_, _) => await w.ReadAsync(); }
        w.ShowDialog();
    }

    private PinterestWindow(DownloadManager manager)
    {
        _manager = manager;
        Ui.Setup(this, L.T("ألبوم Pinterest"), 600, 520);
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        root.Children.Add(Ui.Text(L.T("تحميل ألبوم كامل من Pinterest"), 16, Theme.Ink, bold: true));
        var hint = Ui.Text(L.T("الصق رابط الألبوم (اللوحة) من Pinterest. تتحمّل كل الصور بأعلى دقة وكل الفيديوهات بأفضل جودة، داخل مجلد باسم الألبوم."), 12, Theme.Muted, wrap: TextWrapping.Wrap);
        hint.Margin = new Thickness(0, 4, 0, 12);
        root.Children.Add(hint);

        root.Children.Add(Ui.Label(L.T("رابط الألبوم")));
        var linkRow = new DockPanel();
        _read = Ui.Button(L.T("قراءة الألبوم"), "", Theme.Button, async () => await ReadAsync());
        _read.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(_read, Dock.Right);
        linkRow.Children.Add(_read);
        _link.FlowDirection = FlowDirection.LeftToRight;
        linkRow.Children.Add(_link);
        root.Children.Add(linkRow);
        try
        {
            var clip = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
            if (Pinterest.IsPinterest(clip)) _link.Text = clip;
        }
        catch (Exception) { }

        _status.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(_status);

        // after reading: the album, where it goes, and the download button
        var info = new StackPanel();
        info.Children.Add(_boardName);
        _counts.Margin = new Thickness(0, 6, 0, 0);
        info.Children.Add(_counts);
        var card = Ui.Card(info);
        card.Margin = new Thickness(0, 12, 0, 0);
        _result.Children.Add(card);
        _result.Children.Add(Ui.Label(L.T("يُحفظ في المجلد")));
        var folderRow = new DockPanel();
        var browse = Ui.Button(L.T("تغيير…"), "", Theme.Button, () =>
        {
            var d = new Microsoft.Win32.OpenFolderDialog { Title = L.T("اختر مجلد الحفظ") };
            try { var parent = Path.GetDirectoryName(_folder.Text); if (Directory.Exists(parent)) d.InitialDirectory = parent; } catch (Exception) { }
            if (d.ShowDialog(this) == true) _folder.Text = Path.Combine(d.FolderName, Path.GetFileName(_folder.Text));
        });
        browse.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(browse, Dock.Right);
        folderRow.Children.Add(browse);
        _folder.FlowDirection = FlowDirection.LeftToRight;
        folderRow.Children.Add(_folder);
        _result.Children.Add(folderRow);
        var queueBox = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };
        queueBox.Children.Add(Ui.Label(L.T("قائمة الانتظار")));
        _queue.ItemsSource = Settings.Current.Queues.Select(q => q.Name).ToList();
        _queue.SelectedIndex = 0;
        _queue.HorizontalAlignment = HorizontalAlignment.Left;
        queueBox.Children.Add(_queue);
        _result.Children.Add(queueBox);
        _result.Children.Add(_startNow);
        root.Children.Add(_result);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        _download = Ui.Button(L.T("تحميل الألبوم كاملاً"), "", Theme.PrimaryButton, AddAll);
        _download.IsEnabled = false;
        var close = Ui.Button(L.T("إغلاق"), style: Theme.Button, click: Close);
        close.IsCancel = true;
        close.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(_download);
        row.Children.Add(close);
        root.Children.Add(row);
        Content = root;
        Closed += (_, _) => _cts?.Cancel();
    }

    private async Task ReadAsync()
    {
        var url = _link.Text.Trim();
        if (url.Length == 0) { _status.Text = L.T("الصق رابط الألبوم أولاً."); return; }
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _read.IsEnabled = false;
        _download.IsEnabled = false;
        _result.Visibility = Visibility.Collapsed;
        _status.Foreground = Theme.Muted;
        _status.Text = L.T("جاري قراءة الألبوم…");
        try
        {
            _board = await Pinterest.GetBoardAsync(Http.Client, url, ct);
            var title = _board.SectionName is null ? _board.Name : $"{_board.Name} — {_board.SectionName}";
            _status.Text = L.F("جاري جمع الصور والفيديوهات من «{0}»…", title);
            _media = await Pinterest.GetMediaAsync(Http.Client, _board, n => Dispatcher.BeginInvoke(() => _status.Text = L.F("جاري جمع الصور والفيديوهات من «{0}»… ({1})", title, n)), ct);
            if (_media.Count == 0) { _status.Foreground = Theme.Red; _status.Text = L.T("لم يتم العثور على صور أو فيديوهات في هذا الألبوم (ربما فارغ أو خاص)."); return; }
            _boardName.Text = title;
            _boardName.FlowDirection = Ui.DirectionOf(title);
            var pics = _media.Count(m => !m.IsVideo);
            var vids = _media.Count - pics;
            var sections = _media.Select(m => m.Section).Where(s => s is not null).Distinct().Count();
            _counts.Text = L.F("{0} صورة و {1} فيديو", pics, vids) + (sections > 0 ? " — " + L.F("{0} أقسام (كل قسم بمجلد داخل الألبوم)", sections) : "")
                + "\n" + L.F("صاحب الألبوم: {0}", _board.Owner);
            var s = Settings.Current;
            var cat = s.CategoryNamed("صور") ?? s.CategoryFor("a.jpg");
            _folder.Text = Path.Combine(s.FolderFor(cat), FileNames.Clean(_board.SectionName ?? _board.Name, "Pinterest"));
            _status.Text = "";
            _result.Visibility = Visibility.Visible;
            _download.IsEnabled = true;
            _download.IsDefault = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Log.Write($"pinterest {url}: {e.Message}");
            _status.Foreground = Theme.Red;
            _status.Text = e is InvalidOperationException ? e.Message : L.F("تعذرت قراءة الألبوم: {0}", e.Message);
        }
        finally { _read.IsEnabled = true; }
    }

    private void AddAll()
    {
        if (_board is null || _media.Count == 0) return;
        var folder = _folder.Text.Trim();
        if (folder.Length == 0) return;
        try { Directory.CreateDirectory(folder); }
        catch (Exception e) { Ui.Message(this, L.T("تعذر إنشاء المجلد"), e.Message); return; }
        var s = Settings.Current;
        var queue = _queue.SelectedItem as string ?? Settings.DefaultQueueName;
        var start = _startNow.IsChecked == true;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inList = _manager.Items.Select(i => Path.Combine(i.Folder, i.FileName)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var m in _media)
        {
            var dir = m.Section is null ? folder : Path.Combine(folder, FileNames.Clean(m.Section, "Section"));
            var path = FileNames.Unique(Path.Combine(dir, FileNames.Clean(m.Name, "Pinterest") + m.Extension),
                p => File.Exists(p) || File.Exists(p + ".ibdmpart") || inList.Contains(p) || used.Contains(p));
            used.Add(path);
            var name = Path.GetFileName(path);
            var cat = s.CategoryFor(name);
            var request = new RequestInfo { Referrer = _board.Root + "/", UserAgent = Http.DefaultUserAgent };
            var item = m.Hls
                ? new DownloadItem
                {
                    Kind = DownloadKind.Media, Url = m.Url, PageUrl = m.PinUrl, FileName = name, Folder = dir, Category = cat.Name, QueueName = queue,
                    FormatSpec = YtDlp.SpecFor("best"), TotalBytes = -1, Quality = L.T("أفضل جودة"), Request = request,
                }
                : new DownloadItem
                {
                    Kind = DownloadKind.File, Url = m.Url, PageUrl = m.PinUrl, FileName = name, Folder = dir, Category = cat.Name, QueueName = queue,
                    TotalBytes = -1, Resumable = false, Request = request,
                };
            _manager.Add(item, start);
            added++;
        }
        Log.Write($"pinterest: {added} files from {_board.Name} → {folder}");
        IbdmApp.Instance.Tray?.Notify(L.T("ألبوم Pinterest"), L.F("أُضيف {0} ملف إلى التحميلات", added), folder);
        Close();
    }
}
