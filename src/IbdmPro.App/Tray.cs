using IbdmPro.Core;
using WF = System.Windows.Forms;

namespace IbdmPro.App;

/// <summary>The icon next to the clock: IBDM Pro keeps running there so the browser can always hand downloads to it.</summary>
public sealed class Tray : IDisposable
{
    private readonly WF.NotifyIcon _icon;
    private string? _lastFile;

    public Tray(IbdmApp app)
    {
        var menu = new WF.ContextMenuStrip { RightToLeft = WF.RightToLeft.Yes };
        menu.Items.Add("فتح IBDM Pro", null, (_, _) => app.ShowMain());
        menu.Items.Add("إضافة رابط…", null, (_, _) => { app.ShowMain(); app.Main.AddUrl(); });
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("استئناف الكل", null, (_, _) => app.Manager.ResumeAll());
        menu.Items.Add("إيقاف الكل", null, (_, _) => app.Manager.PauseAll());
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("فتح مجلد التحميلات", null, (_, _) => { Directory.CreateDirectory(Settings.Current.DownloadRoot); Ui.Open(Settings.Current.DownloadRoot); });
        menu.Items.Add("الإعدادات", null, (_, _) => { app.ShowMain(); app.Main.OpenSettings(); });
        menu.Items.Add("البحث عن تحديث…", null, (_, _) => { app.ShowMain(); Updater.ShowWindow(app.Main); });
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("خروج", null, (_, _) => app.Quit());

        System.Drawing.Icon? icon = null;
        try { if (Environment.ProcessPath is { } exe) icon = System.Drawing.Icon.ExtractAssociatedIcon(exe); } catch (Exception) { }
        _icon = new WF.NotifyIcon
        {
            Icon = icon ?? System.Drawing.SystemIcons.Application,
            Text = "IBDM Pro",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == WF.MouseButtons.Left) app.ShowMain(); };
        _icon.BalloonTipClicked += (_, _) =>
        {
            if (_lastFile is not null && File.Exists(_lastFile)) Ui.ShowInFolder(_lastFile);
            else app.ShowMain();
        };
    }

    public void Notify(string title, string text, string? file)
    {
        _lastFile = file;
        _icon.ShowBalloonTip(4000, title, text.Length > 200 ? text[..200] : text, WF.ToolTipIcon.None);
    }

    public void SetTooltip(string text) => _icon.Text = text.Length > 63 ? text[..63] : text;

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
