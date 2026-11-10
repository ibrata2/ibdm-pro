using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using IbdmPro.Core;

namespace IbdmPro.App;

/// <summary>Many downloads at once: paste many links (files, video pages, YouTube playlists), or pick videos from one
/// playlist; one quality for all the videos, one queue for everything.</summary>
public sealed class BatchWindow : Window
{
    private readonly DownloadManager _manager;
    private readonly TextBox _links = Ui.Input(multiline: true);
    private readonly ComboBox _quality = new();
    private readonly ComboBox _queue = new() { MinWidth = 160 };
    private readonly CheckBox _startNow;
    private readonly TextBlock _status = Ui.Text("", 12.5, Theme.Muted, wrap: TextWrapping.Wrap);
    private readonly StackPanel _entries = new();
    private readonly Button _add;
    private readonly string? _playlistUrl;
    private CancellationTokenSource? _cts;

    private static readonly (string Key, string Text)[] Qualities =
    [
        ("best", "أفضل جودة متوفرة"), ("1080", "1080p"), ("720", "720p"), ("480", "480p"), ("audio", "صوت فقط (MP3)"),
    ];

    /// <summary>Paste many links.</summary>
    public static void ShowLinks(Window owner, DownloadManager manager, string? text = null) =>
        new BatchWindow(manager, null) { Owner = owner is { IsVisible: true } ? owner : null }.WithText(text).ShowDialog();

    /// <summary>Choose videos from a YouTube playlist.</summary>
    public static void ShowPlaylist(Window? owner, DownloadManager manager, string url) =>
        new BatchWindow(manager, url) { Owner = owner is { IsVisible: true } ? owner : null }.ShowDialog();

    private BatchWindow WithText(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text)) _links.Text = text;
        return this;
    }

    private BatchWindow(DownloadManager manager, string? playlistUrl)
    {
        _manager = manager;
        _playlistUrl = playlistUrl;
        Ui.Setup(this, playlistUrl is null ? L.T("روابط متعددة") : L.T("قائمة تشغيل"), 640, 640);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        MinHeight = 420;

        var root = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
        var head = Ui.Text(playlistUrl is null ? L.T("تحميل روابط متعددة") : L.T("تحميل قائمة تشغيل"), 16, Theme.Ink, bold: true);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        var hint = Ui.Text(playlistUrl is null
            ? L.T("الصق الروابط، كل رابط في سطر (ملفات، صفحات فيديو، أو قوائم تشغيل يوتيوب). يمكنك لصق نص كامل وسيستخرج البرنامج الروابط منه.")
            : L.T("اختر الفيديوهات التي تريدها من القائمة."), 12, Theme.Muted, wrap: TextWrapping.Wrap);
        hint.Margin = new Thickness(0, 4, 0, 10);
        DockPanel.SetDock(hint, Dock.Top);
        root.Children.Add(hint);

        // bottom: options and buttons
        var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var options = new WrapPanel();
        var qBox = new StackPanel { Margin = new Thickness(0, 0, 14, 6) };
        qBox.Children.Add(Ui.Label(L.T("جودة الفيديوهات")));
        _quality.ItemsSource = Qualities.Select(q => L.T(q.Text)).ToList();
        _quality.SelectedIndex = 0;
        _quality.MinWidth = 170;
        qBox.Children.Add(_quality);
        options.Children.Add(qBox);
        var queueBox = new StackPanel { Margin = new Thickness(0, 0, 14, 6) };
        queueBox.Children.Add(Ui.Label(L.T("قائمة الانتظار")));
        _queue.ItemsSource = Settings.Current.Queues.Select(q => q.Name).ToList();
        _queue.SelectedIndex = 0;
        queueBox.Children.Add(_queue);
        options.Children.Add(queueBox);
        bottom.Children.Add(options);
        _startNow = Ui.Check(L.T("ابدأ التحميل الآن (وإلا تُضاف إلى القائمة متوقفة)"), true);
        bottom.Children.Add(_startNow);
        _status.Margin = new Thickness(0, 8, 0, 0);
        bottom.Children.Add(_status);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        _add = Ui.Button(L.T("إضافة"), "", Theme.PrimaryButton, async () => await AddAllAsync());
        _add.IsDefault = playlistUrl is not null;
        var close = Ui.Button(L.T("إغلاق"), style: Theme.Button, click: Close);
        close.IsCancel = true;
        close.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(_add);
        row.Children.Add(close);
        bottom.Children.Add(row);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        if (playlistUrl is null)
        {
            _links.FlowDirection = FlowDirection.LeftToRight;
            _links.AcceptsReturn = true;
            _links.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _links.TextWrapping = TextWrapping.NoWrap;
            _links.VerticalContentAlignment = VerticalAlignment.Top;
            try
            {
                var clip = Clipboard.ContainsText() ? Clipboard.GetText() : "";
                if (Links(clip).Count > 0) _links.Text = string.Join(Environment.NewLine, Links(clip));
            }
            catch (Exception) { }
            root.Children.Add(_links);
        }
        else
        {
            var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            var all = Ui.Button(L.T("تحديد الكل"), style: Theme.Button, click: () => SetAll(true));
            var none = Ui.Button(L.T("إلغاء التحديد"), style: Theme.Button, click: () => SetAll(false));
            none.Margin = new Thickness(8, 0, 0, 0);
            tools.Children.Add(all);
            tools.Children.Add(none);
            DockPanel.SetDock(tools, Dock.Top);
            root.Children.Add(tools);
            root.Children.Add(new Border
            {
                BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Background = Theme.Card,
                Child = new ScrollViewer { Content = _entries, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8) },
            });
            Loaded += async (_, _) => await LoadPlaylistAsync();
        }
        Content = root;
        Closed += (_, _) => _cts?.Cancel();
    }

    /// <summary>Every http(s) link in a piece of text (one per line, or inside other text).</summary>
    public static List<string> Links(string text) =>
        Regex.Matches(text ?? "", @"https?://[^\s""'<>]+", RegexOptions.IgnoreCase).Select(m => m.Value.TrimEnd('.', ',', ')', ']', '؛', '،')).Distinct().ToList();

    private void SetAll(bool on)
    {
        foreach (var c in _entries.Children.OfType<CheckBox>()) c.IsChecked = on;
    }

    private async Task LoadPlaylistAsync()
    {
        _add.IsEnabled = false;
        _status.Text = L.T("جاري قراءة قائمة التشغيل…");
        _cts = new CancellationTokenSource();
        try
        {
            var pl = await YtDlp.GetPlaylistAsync(_playlistUrl!, new RequestInfo(), _cts.Token);
            Title = pl.Title;
            var i = 0;
            foreach (var e in pl.Entries)
            {
                i++;
                var text = $"{i}. {e.Title}" + (e.Duration > 0 ? $"  ({TimeSpan.FromSeconds(e.Duration):h\\:mm\\:ss})".Replace("(0:", "(") : "");
                _entries.Children.Add(new CheckBox { Content = text, Tag = e, IsChecked = true, Style = Theme.CheckBox, Margin = new Thickness(0, 2, 0, 2) });
            }
            _status.Text = L.F("{0} — {1} فيديو", pl.Title, pl.Entries.Count);
            _add.IsEnabled = pl.Entries.Count > 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { _status.Text = e.Message; }
    }

    private async Task AddAllAsync()
    {
        _add.IsEnabled = false;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var quality = Qualities[Math.Max(0, _quality.SelectedIndex)].Key;
        var queue = _queue.SelectedItem as string ?? Settings.DefaultQueueName;
        var start = _startNow.IsChecked == true;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int added = 0, failed = 0;
        var problems = new List<string>();
        try
        {
            if (_playlistUrl is not null)
            {
                foreach (var e in _entries.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (YtDlp.PlaylistEntry)c.Tag))
                {
                    _manager.Add(MediaItem(e.Url, e.Title, quality, queue, used), start);
                    added++;
                }
            }
            else
            {
                var links = Links(_links.Text);
                for (var n = 0; n < links.Count; n++)
                {
                    ct.ThrowIfCancellationRequested();
                    var url = links[n];
                    _status.Text = L.F("جاري فحص الرابط {0} من {1}…", n + 1, links.Count);
                    try
                    {
                        if (YtDlp.IsPlaylistUrl(url) && !url.Contains("v=", StringComparison.Ordinal) || url.Contains("/playlist?", StringComparison.OrdinalIgnoreCase))
                        {
                            var pl = await YtDlp.GetPlaylistAsync(url, new RequestInfo(), ct);
                            foreach (var e in pl.Entries) { _manager.Add(MediaItem(e.Url, e.Title, quality, queue, used), start); added++; }
                            continue;
                        }
                        var probe = await Http.ProbeAsync(url, new RequestInfo(), ct);
                        if (probe.Error is null && !probe.LooksLikeWebPage)
                        {
                            _manager.Add(FileItem(probe, url, queue, used), start);
                            added++;
                            continue;
                        }
                        var media = await YtDlp.GetInfoAsync(url, new RequestInfo(), ct);
                        _manager.Add(MediaItem(media.Url, media.Title, quality, queue, used), start);
                        added++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e)
                    {
                        failed++;
                        problems.Add($"{url} — {e.Message}");
                    }
                }
            }
            IbdmApp.Instance.Main.Sync();
            _status.Text = L.F("تمت إضافة {0} تحميل.", added) + (failed > 0 ? " " + L.F("تعذر {0}:", failed) + Environment.NewLine + string.Join(Environment.NewLine, problems.Take(8)) : "");
            if (failed == 0 && added > 0) { await Task.Delay(900); Close(); }
        }
        catch (OperationCanceledException) { }
        finally { _add.IsEnabled = true; }
    }

    private static DownloadItem MediaItem(string url, string title, string quality, string queue, HashSet<string> used)
    {
        var s = Settings.Current;
        var audio = quality == "audio";
        var cat = s.CategoryNamed(audio ? "صوتيات" : "فيديو") ?? s.CategoryFor(audio ? "a.mp3" : "a.mp4");
        var folder = s.FolderFor(cat);
        var name = UniqueName(folder, FileNames.Clean(title, "video") + (audio ? ".mp3" : ".mp4"), used);
        return new DownloadItem
        {
            Kind = DownloadKind.Media, Url = url, PageUrl = url, FileName = name, Folder = folder, Category = cat.Name, QueueName = queue,
            FormatSpec = YtDlp.SpecFor(quality), AudioOnly = audio, AudioFormat = audio ? "mp3" : null, TotalBytes = -1,
            Quality = audio ? "MP3" : quality == "best" ? L.T("أفضل جودة") : quality + "p",
        };
    }

    private static DownloadItem FileItem(ProbeResult probe, string url, string queue, HashSet<string> used)
    {
        var s = Settings.Current;
        var name = FileNames.Clean(probe.FileName);
        var cat = s.CategoryFor(name);
        var folder = s.FolderFor(cat);
        return new DownloadItem
        {
            Kind = DownloadKind.File, Url = probe.FinalUrl, PageUrl = url, FileName = UniqueName(folder, name, used), Folder = folder, Category = cat.Name,
            QueueName = queue, TotalBytes = probe.Size, Resumable = probe.Resumable,
        };
    }

    /// <summary>A name not used on disk, in the list, or earlier in this batch.</summary>
    private static string UniqueName(string folder, string name, HashSet<string> used)
    {
        var inList = IbdmApp.Instance.Manager.Items.Select(i => Path.Combine(i.Folder, i.FileName)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var path = FileNames.Unique(Path.Combine(folder, name), p => File.Exists(p) || File.Exists(p + ".ibdmpart") || inList.Contains(p) || used.Contains(p));
        used.Add(path);
        return Path.GetFileName(path);
    }
}
