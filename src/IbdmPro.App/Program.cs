using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using IbdmPro.Core;
using Microsoft.Win32;

namespace IbdmPro.App;

public static class Program
{
    public static string Version { get; } =
        (typeof(Program).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0");

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 1 && args[0] == "--snapshot")
        {
            // Render the windows to pictures with sample data, without touching the real settings or list.
            Paths.AppDataOverride = Path.Combine(Path.GetTempPath(), "IBDM Pro snapshot " + Guid.NewGuid().ToString("N")[..6]);
            var snap = new IbdmApp(background: true) { SnapshotDir = Path.GetFullPath(args[1]) };
            return snap.Run();
        }
        using var single = new Mutex(true, "Local\\IBDMPro.SingleInstance", out var first);
        if (!first)
        {
            // Already running (maybe hidden in the tray): ask it to show its window and leave.
            if (!args.Contains("--background")) LocalServer.TryShowRunning();
            return 0;
        }
        var app = new IbdmApp(args.Contains("--background"));
        return app.Run();
    }
}

/// <summary>The app: owns the download manager, the local server for the browser extension, the tray icon and the main window.</summary>
public sealed class IbdmApp : Application
{
    public static IbdmApp Instance { get; private set; } = null!;
    public DownloadManager Manager { get; private set; } = null!;
    public LocalServer? Server { get; private set; }
    public Tray? Tray { get; private set; }
    public MainWindow Main { get; private set; } = null!;
    private readonly bool _background;
    public bool Quitting { get; private set; }
    public string? SnapshotDir { get; init; }

    public IbdmApp(bool background)
    {
        Instance = this;
        _background = background;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_, e) =>
        {
            Log.Write($"unhandled: {e.Exception}");
            e.Handled = true;
            try { Ui.Message(null, "حدث خطأ", e.Exception.Message); } catch (Exception) { }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"fatal: {e.ExceptionObject}");
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("ar")));
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Write($"IBDM Pro {Program.Version} started");
        var settings = Settings.Current;
        if (SnapshotDir is not null)
        {
            Manager = new DownloadManager();
            Main = new MainWindow(Manager);
            Snapshots.Run(this, SnapshotDir);
            Manager.Dispose();
            Shutdown();
            return;
        }
        Startup.Apply(settings.StartWithWindows);

        Manager = new DownloadManager();
        Manager.Limiter.BytesPerSecond = settings.SpeedLimitKBps * 1024L;
        Manager.ItemCompleted += item => Dispatcher.BeginInvoke(() =>
        {
            if (Settings.Current.NotifyOnComplete) Tray?.Notify("اكتمل التحميل", item.FileName, item.FullPath);
        });
        Manager.ItemFailed += item => Dispatcher.BeginInvoke(() => Tray?.Notify("فشل التحميل", $"{item.FileName}\n{item.Error}", null));

        Main = new MainWindow(Manager);
        Tray = new Tray(this);

        try
        {
            Server = new LocalServer { Version = Program.Version };
            Server.Captured += req => Dispatcher.BeginInvoke(() => Capture.Handle(req));
            Server.ShowRequested += () => Dispatcher.BeginInvoke(ShowMain);
            Server.Start();
        }
        catch (Exception ex)
        {
            Log.Write($"server: {ex.Message}");
            Server = null;
        }

        Manager.Begin();
        if (!_background) ShowMain();

        if (!settings.HasSeenWelcome)
        {
            settings.HasSeenWelcome = true;
            settings.Save();
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => ExtensionGuide.Show(Main));
        }
        if (Server is null) Ui.Message(Main, "تنبيه", $"المنفذ {Settings.Port} مستخدم من برنامج آخر، لذلك لن تصل التحميلات من المتصفح. أغلق البرنامج الآخر ثم أعد تشغيل IBDM Pro.");
    }

    public void ShowMain() => Ui.ForceFront(Main);

    public void Quit()
    {
        Quitting = true;
        Manager.Dispose();
        Server?.Dispose();
        Tray?.Dispose();
        Shutdown();
    }
}

/// <summary>"Start with Windows": a Run entry for the signed-in user, started hidden in the tray.</summary>
public static class Startup
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Apply(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key, true) ?? Registry.CurrentUser.CreateSubKey(Key);
            if (on && Environment.ProcessPath is { } exe) k.SetValue("IBDM Pro", $"\"{exe}\" --background");
            else k.DeleteValue("IBDM Pro", false);
        }
        catch (Exception e) { Log.Write($"startup: {e.Message}"); }
    }
}
