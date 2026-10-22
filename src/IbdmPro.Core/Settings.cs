using System.Text.Json;

namespace IbdmPro.Core;

/// <summary>One kind of file and the folder it is saved in (inside the IBDM Pro folder).</summary>
public sealed class Category
{
    public string Name { get; set; } = "";
    /// <summary>Folder name inside <see cref="Settings.DownloadRoot"/>, or a full path if the person chose one elsewhere.</summary>
    public string Folder { get; set; } = "";
    /// <summary>Extensions without the dot, e.g. "mp4".</summary>
    public List<string> Extensions { get; set; } = [];
    /// <summary>Glyph in Segoe Fluent Icons and the colour drawn for this kind.</summary>
    public string Icon { get; set; } = "";
    public string Color { get; set; } = "#9AA0A6";

    public bool Matches(string ext) => Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
}

public sealed class Settings
{
    public const int Port = 39401;
    public const string OtherCategory = "أخرى";

    /// <summary>Everything IBDM Pro downloads goes inside this folder, sorted into one folder per kind.</summary>
    public string DownloadRoot { get; set; } = Path.Combine(Paths.UserDownloads, "IBDM Pro");
    public List<Category> Categories { get; set; } = DefaultCategories();

    // Capture from the browser
    public bool CaptureEnabled { get; set; } = true;
    public List<string> CaptureExtensions { get; set; } = DefaultCaptureExtensions();
    public int MinCaptureSizeKB { get; set; } = 0;
    public List<string> ExcludedSites { get; set; } = ["docs.google.com", "mail.google.com", "web.whatsapp.com"];

    // Downloading
    public bool ShowInfoBeforeDownload { get; set; } = true;
    public int Connections { get; set; } = 8;
    public int MaxParallel { get; set; } = 3;
    public int SpeedLimitKBps { get; set; } = 0;
    public bool ResumeOnStart { get; set; } = true;

    // App
    /// <summary>"auto" = light or dark like Windows, "dark", or "light".</summary>
    public string Theme { get; set; } = "auto";
    public bool CheckUpdatesOnStart { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool NotifyOnComplete { get; set; } = true;
    public bool HasSeenTrayHint { get; set; }
    public bool HasSeenWelcome { get; set; }

    public static List<Category> DefaultCategories() =>
    [
        new() { Name = "فيديو", Folder = "فيديو", Icon = "", Color = "#E5533D",
            Extensions = ["mp4", "mkv", "avi", "mov", "wmv", "flv", "webm", "m4v", "3gp", "ts", "mpg", "mpeg", "vob"] },
        new() { Name = "صوتيات", Folder = "صوتيات", Icon = "", Color = "#A86BF0",
            Extensions = ["mp3", "m4a", "aac", "wav", "flac", "ogg", "opus", "wma", "amr", "weba"] },
        new() { Name = "مضغوطات", Folder = "مضغوطات", Icon = "", Color = "#E8B23A",
            Extensions = ["zip", "rar", "7z", "tar", "gz", "bz2", "xz", "tgz", "cab"] },
        new() { Name = "برامج", Folder = "برامج", Icon = "", Color = "#3B9BF0",
            Extensions = ["exe", "msi", "msix", "appx", "apk", "xapk", "bat", "dmg", "iso", "img"] },
        new() { Name = "مستندات", Folder = "مستندات", Icon = "", Color = "#2DB7A3",
            Extensions = ["pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "csv", "epub", "rtf", "odt"] },
        new() { Name = "صور", Folder = "صور", Icon = "", Color = "#4CC26B",
            Extensions = ["jpg", "jpeg", "png", "gif", "webp", "bmp", "svg", "psd", "tif", "tiff", "heic", "ico"] },
        new() { Name = "تورنت", Folder = "تورنت", Icon = "", Color = "#8BC34A", Extensions = ["torrent"] },
        new() { Name = OtherCategory, Folder = OtherCategory, Icon = "", Color = "#9AA0A6", Extensions = [] },
    ];

    public static List<string> DefaultCaptureExtensions() =>
    [
        "zip", "rar", "7z", "tar", "gz", "bz2", "xz", "iso", "exe", "msi", "msix", "apk", "xapk", "dmg", "bin", "img",
        "mp4", "mkv", "avi", "mov", "wmv", "flv", "webm", "m4v", "3gp", "mpg", "mpeg",
        "mp3", "m4a", "aac", "wav", "flac", "ogg", "opus", "wma",
        "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "epub", "torrent",
    ];

    public Category CategoryFor(string fileName)
    {
        var ext = FileNames.ExtOf(fileName);
        return Categories.FirstOrDefault(c => ext.Length > 0 && c.Matches(ext))
            ?? Categories.FirstOrDefault(c => c.Name == OtherCategory)
            ?? Categories.Last();
    }

    public Category? CategoryNamed(string? name) => Categories.FirstOrDefault(c => c.Name == name);

    /// <summary>The full folder a category saves into (created when needed).</summary>
    public string FolderFor(Category c) =>
        Path.IsPathRooted(c.Folder) ? c.Folder : Path.Combine(DownloadRoot, string.IsNullOrWhiteSpace(c.Folder) ? c.Name : c.Folder);

    public bool ShouldCapture(string fileName, string? url, long size)
    {
        if (!CaptureEnabled) return false;
        if (size > 0 && MinCaptureSizeKB > 0 && size < MinCaptureSizeKB * 1024L) return false;
        if (url is not null && IsExcluded(url)) return false;
        var ext = FileNames.ExtOf(fileName);
        return ext.Length > 0 && CaptureExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsExcluded(string url)
    {
        try
        {
            var host = new Uri(url).Host;
            return ExcludedSites.Any(s => s.Length > 0 && (host.Equals(s, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + s, StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception) { return false; }
    }

    // ---------- loading and saving ----------

    private static Settings? _current;
    public static Settings Current => _current ??= Load();
    public static event Action? Changed;

    private static string FilePath => Path.Combine(Paths.AppData, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json.Options);
                if (s is not null)
                {
                    if (s.Categories.Count == 0) s.Categories = DefaultCategories();
                    if (s.Categories.All(c => c.Name != OtherCategory)) s.Categories.Add(DefaultCategories().Last());
                    s.Connections = Math.Clamp(s.Connections, 1, 32);
                    s.MaxParallel = Math.Clamp(s.MaxParallel, 1, 10);
                    return s;
                }
            }
        }
        catch (Exception e) { Log.Write($"settings: {e.Message}"); }
        return new Settings();
    }

    public void Save()
    {
        try { Json.SaveAtomic(FilePath, this); }
        catch (Exception e) { Log.Write($"settings save: {e.Message}"); }
        _current = this;
        Changed?.Invoke();
    }

    /// <summary>For tests.</summary>
    public static void Reset(Settings? s = null) => _current = s;
}
