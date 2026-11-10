using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using IbdmPro.Core;

namespace IbdmPro.App;

/// <summary>Messages from the developer: checked now and then, a new one pops up next to the clock
/// (click = open its link, or the messages window), and all of them are listed in «الرسائل».</summary>
public static class MessageCenter
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    public static List<AppMessage> Latest { get; private set; } = [];
    public static void SetForPreview(List<AppMessage> list) => Latest = list;

    public static async Task RunInBackground()
    {
        Latest = MessageFeed.Current(MessageFeed.Cached(), L.Lang, DateTime.UtcNow);
        await Task.Delay(TimeSpan.FromSeconds(15));
        while (true)
        {
            try { await CheckOnce(); }
            catch (Exception e) { Log.Write($"messages: {e.Message}"); }
            await Task.Delay(TimeSpan.FromMinutes(30));
        }
    }

    private static async Task CheckOnce()
    {
        var all = await MessageFeed.FetchAsync(Http);
        var current = MessageFeed.Current(all, L.Lang, DateTime.UtcNow);
        Latest = current;
        IbdmApp.Instance.Dispatcher.Invoke(() => IbdmApp.Instance.Main.RefreshAd());
        if (MessageFeed.FirstRun)
        {
            // A new install: only messages from the last week are shown, older ones count as read.
            MessageFeed.MarkSeen(current.Where(m => m.When < DateTime.UtcNow.AddDays(-7)).Select(m => m.Id).Append("_first_run"));
        }
        foreach (var m in MessageFeed.Unseen(current))
        {
            MessageFeed.MarkSeen([m.Id]);
            IbdmApp.Instance.Dispatcher.Invoke(() => Show(m));
            await Task.Delay(TimeSpan.FromSeconds(9)); // one notice at a time
        }
    }

    private static void Show(AppMessage m)
    {
        var app = IbdmApp.Instance;
        var title = m.Kind == "app" ? (m.Title.Length > 0 ? L.F("برنامج جديد: {0}", m.Title) : L.T("برنامج جديد على قناة البرامج"))
            : m.Title.Length > 0 ? m.Title : L.F("رسالة من {0}", Program.ProductName);
        var text = m.Text.Length > 0 ? m.Text : title;
        if (m.HasLink) text += "\n" + L.T("اضغط هنا لفتح الرابط");
        app.Tray?.NotifyAction(title, text, () =>
        {
            if (m.HasLink) Ui.Open(m.Url!);
            else { app.ShowMain(); MessagesWindow.Show(app.Main); }
        });
    }
}

public sealed class MessagesWindow : Window
{
    public static void Show(Window? owner) => new MessagesWindow { Owner = owner is { IsVisible: true } ? owner : null }.ShowDialog();

    public MessagesWindow() : this(MessageCenter.Latest.Count > 0 ? MessageCenter.Latest : MessageFeed.Current(MessageFeed.Cached(), L.Lang, DateTime.UtcNow)) { }

    public MessagesWindow(List<AppMessage> list)
    {
        Ui.Setup(this, L.T("الرسائل"), 520, 560);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        var root = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
        var head = Ui.Text(L.T("رسائل وإعلانات من المطوّر"), 16, Theme.Ink, bold: true);
        head.Margin = new Thickness(0, 0, 0, 12);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        var close = Ui.Button(L.T("إغلاق"), style: Theme.Button, click: Close);
        close.IsCancel = true;
        close.HorizontalAlignment = HorizontalAlignment.Left;
        close.Margin = new Thickness(0, 12, 0, 0);
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);

        var items = new StackPanel();
        if (list.Count == 0)
            items.Children.Add(Ui.Text(L.T("لا توجد رسائل حالياً."), 13, Theme.Muted));
        foreach (var m in list)
        {
            var card = new StackPanel();
            var top = new DockPanel();
            if (m.When != DateTime.MinValue)
            {
                var date = Ui.Text(m.When.ToLocalTime().ToString("yyyy/MM/dd"), 11.5, Theme.Muted);
                date.FlowDirection = FlowDirection.LeftToRight;
                DockPanel.SetDock(date, Dock.Right);
                top.Children.Add(date);
            }
            top.Children.Add(Ui.Text(m.Title.Length > 0 ? m.Title : L.T("رسالة"), 14, Theme.Ink, bold: true, wrap: TextWrapping.Wrap));
            card.Children.Add(top);
            if (m.Text.Length > 0)
            {
                var body = Ui.Text(m.Text, 13, Theme.Ink, wrap: TextWrapping.Wrap);
                body.Margin = new Thickness(0, 6, 0, 0);
                card.Children.Add(body);
            }
            if (m.HasLink)
            {
                var url = m.Url!;
                var open = Ui.Button(L.T("فتح الرابط"), "", Theme.PrimaryButton, () => Ui.Open(url));
                open.HorizontalAlignment = HorizontalAlignment.Left;
                open.Margin = new Thickness(0, 10, 0, 0);
                card.Children.Add(open);
            }
            var box = Ui.Card(card);
            box.Margin = new Thickness(0, 0, 0, 10);
            items.Children.Add(box);
        }
        root.Children.Add(new ScrollViewer { Content = items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }
}
