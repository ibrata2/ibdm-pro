using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IbdmPro.Core;

namespace IbdmPro.App;

/// <summary>Starts queues at their scheduled time, and when a queue that has a "when done" action finishes,
/// shuts the computer down / puts it to sleep / closes the app (after a countdown that can be cancelled).</summary>
public static class Scheduler
{
    private static readonly HashSet<string> Busy = [];
    private static DispatcherTimer? _timer;

    public static void Start()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private static void Tick()
    {
        var app = IbdmApp.Instance;
        var s = Settings.Current;
        var now = DateTime.Now;
        var changed = false;
        foreach (var q in s.Queues.ToList())
        {
            if (q.IsDue(now))
            {
                q.MarkScheduledRun(now);
                changed = true;
                app.Manager.SetQueueRunning(q.Name, true);
                Log.Write($"schedule: queue {q.Name} started");
                app.Tray?.Notify(L.T("بدأت قائمة الانتظار حسب الجدولة"), q.Name, null);
            }
            if (q.WhenDone is "none" or "" || !q.IsRunning) { Busy.Remove(q.Name); continue; }
            var working = app.Manager.Items.Any(i => i.QueueName == q.Name && (i.IsActive || i.Status == DownloadStatus.Queued));
            if (working) Busy.Add(q.Name);
            else if (Busy.Remove(q.Name)) WhenDone(q);
        }
        if (changed) s.Save();
    }

    private static void WhenDone(Settings.DownloadQueue q)
    {
        var app = IbdmApp.Instance;
        Log.Write($"schedule: queue {q.Name} finished, action {q.WhenDone}");
        app.Manager.Save();
        if (q.WhenDone == "exit") { app.Quit(); return; }
        var what = q.WhenDone == "sleep" ? L.T("وضع السكون") : L.T("إطفاء الحاسبة");
        if (!Countdown.Ask(L.F("اكتملت قائمة «{0}». {1} بعد:", q.Name, what), 60)) return;
        try
        {
            if (q.WhenDone == "shutdown") System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false });
            else if (q.WhenDone == "sleep") SetSuspendState(false, false, false);
        }
        catch (Exception e) { Log.Write($"when done: {e.Message}"); }
    }

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
}

/// <summary>"The computer shuts down in 60 seconds" with a Cancel button. True if the time ran out.</summary>
public static class Countdown
{
    public static bool Ask(string message, int seconds)
    {
        var w = new Window { ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.Height, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        Ui.Setup(w, Program.ProductName, 440, 220);
        var left = seconds;
        var go = false;
        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(Ui.Text(message, 14, Theme.Ink, bold: true, wrap: TextWrapping.Wrap));
        var count = Ui.Text(L.F("{0} ثانية", left), 28, Theme.Gold, bold: true);
        count.Margin = new Thickness(0, 10, 0, 14);
        root.Children.Add(count);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var now = Ui.Button(L.T("الآن"), style: Theme.PrimaryButton, click: () => { go = true; w.Close(); });
        var cancel = Ui.Button(L.T("إلغاء"), style: Theme.Button, click: w.Close);
        cancel.IsCancel = true;
        cancel.IsDefault = true;
        cancel.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(now);
        row.Children.Add(cancel);
        root.Children.Add(row);
        w.Content = root;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            left--;
            count.Text = L.F("{0} ثانية", left);
            if (left <= 0) { go = true; w.Close(); }
        };
        w.Loaded += (_, _) => timer.Start();
        w.Closed += (_, _) => timer.Stop();
        w.ShowDialog();
        return go;
    }
}

/// <summary>"Schedule…" for one queue: start time, once or every day, and what to do when it is done.</summary>
public sealed class ScheduleWindow : Window
{
    public static void Show(Window owner, string queueName)
    {
        var q = Settings.Current.Queues.FirstOrDefault(x => x.Name == queueName);
        if (q is not null) new ScheduleWindow(q) { Owner = owner is { IsVisible: true } ? owner : null }.ShowDialog();
    }

    public ScheduleWindow(Settings.DownloadQueue q)
    {
        Ui.Setup(this, L.F("جدولة قائمة «{0}»", q.Name), 460, 420);
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        root.Children.Add(Ui.Text(L.F("جدولة قائمة «{0}»", q.Name), 16, Theme.Ink, bold: true));

        var enable = Ui.Check(L.T("بدء القائمة تلقائياً في الساعة:"), q.ScheduleEnabled);
        enable.Margin = new Thickness(0, 14, 0, 6);
        root.Children.Add(enable);
        TimeSpan.TryParse(q.StartTime, System.Globalization.CultureInfo.InvariantCulture, out var at);
        var hour = new ComboBox { Width = 70, ItemsSource = Enumerable.Range(0, 24).Select(h => h.ToString("00")).ToList(), SelectedIndex = at.Hours };
        var minute = new ComboBox { Width = 70, ItemsSource = Enumerable.Range(0, 12).Select(m => (m * 5).ToString("00")).ToList(), SelectedIndex = Math.Clamp(at.Minutes / 5, 0, 11) };
        var time = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = L.Rtl ? HorizontalAlignment.Right : HorizontalAlignment.Left };
        time.Children.Add(hour);
        var colon = Ui.Text(":", 16, Theme.Ink, bold: true);
        colon.Margin = new Thickness(6, 0, 6, 0);
        colon.VerticalAlignment = VerticalAlignment.Center;
        time.Children.Add(colon);
        time.Children.Add(minute);
        root.Children.Add(time);

        var once = new RadioButton { Content = L.T("مرة واحدة"), GroupName = "repeat", IsChecked = !q.Daily, Foreground = Theme.Ink, Margin = new Thickness(0, 10, 0, 0) };
        var daily = new RadioButton { Content = L.T("كل يوم"), GroupName = "repeat", IsChecked = q.Daily, Foreground = Theme.Ink, Margin = new Thickness(0, 4, 0, 0) };
        root.Children.Add(once);
        root.Children.Add(daily);

        root.Children.Add(Ui.Label(L.T("عند انتهاء القائمة")));
        var actions = new (string Key, string Text)[] { ("none", L.T("بدون إجراء")), ("shutdown", L.T("إطفاء الحاسبة")), ("sleep", L.T("وضع السكون")), ("exit", L.T("إغلاق البرنامج")) };
        var whenDone = new ComboBox { ItemsSource = actions.Select(a => a.Text).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(actions, a => a.Key == q.WhenDone)) };
        root.Children.Add(whenDone);
        var note = Ui.Text(L.T("قبل إطفاء الحاسبة أو السكون يظهر عد تنازلي لمدة دقيقة يمكنك إلغاؤه."), 11.5, Theme.Muted, wrap: TextWrapping.Wrap);
        note.Margin = new Thickness(0, 6, 0, 0);
        root.Children.Add(note);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        var save = Ui.Button(L.T("حفظ"), "", Theme.PrimaryButton, () =>
        {
            q.ScheduleEnabled = enable.IsChecked == true;
            q.StartTime = $"{hour.SelectedItem ?? "02"}:{minute.SelectedItem ?? "00"}";
            q.Daily = daily.IsChecked == true;
            q.WhenDone = actions[Math.Max(0, whenDone.SelectedIndex)].Key;
            // A time already passed today means tomorrow: forget today's run only if the new time is still ahead.
            if (q.ScheduleEnabled && TimeSpan.TryParse(q.StartTime, System.Globalization.CultureInfo.InvariantCulture, out var t) && t > DateTime.Now.TimeOfDay) q.LastScheduledRun = null;
            else if (q.ScheduleEnabled) q.LastScheduledRun = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            Settings.Current.Save();
            Close();
        });
        save.IsDefault = true;
        var cancel = Ui.Button(L.T("إلغاء"), style: Theme.Button, click: Close);
        cancel.IsCancel = true;
        cancel.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(save);
        row.Children.Add(cancel);
        root.Children.Add(row);
        Content = root;
    }
}
