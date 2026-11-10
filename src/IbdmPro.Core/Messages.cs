using System.Net.Http;
using System.Text.Json;

namespace IbdmPro.Core;

/// <summary>A message from the developer to everyone who uses the app (news, an announcement, a link).
/// Messages are written in messages.json in the app's repository (the "Send a message" action adds them there);
/// every copy of the app reads that file now and then and shows new ones next to the clock.</summary>
public sealed class AppMessage
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>Optional link opened when the message is clicked.</summary>
    public string? Url { get; set; }
    /// <summary>When it was sent (yyyy-MM-dd HH:mm, UTC).</summary>
    public string? Date { get; set; }
    /// <summary>Not shown after this day (yyyy-MM-dd); empty = never expires.</summary>
    public string? Expires { get; set; }
    /// <summary>"all", "ar" or "en": which language's users see it.</summary>
    public string? Lang { get; set; } = "all";
    /// <summary>"message" (a notice), "ad" (an ad for a program — also kept in the app's side bar) or
    /// "app" (a new program is out — also kept in the side bar).</summary>
    public string? Kind { get; set; } = "message";

    public bool IsAd => Kind is "ad" or "app";

    public bool HasLink => Url is { Length: > 0 } u && (u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

    public DateTime When => DateTime.TryParse(Date, System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var d) ? d : DateTime.MinValue;

    public bool IsExpired(DateTime nowUtc) => DateTime.TryParse(Expires, System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var e) && nowUtc.Date > e.Date;

    public bool IsFor(string lang) => string.IsNullOrEmpty(Lang) || Lang == "all" || string.Equals(Lang, lang, StringComparison.OrdinalIgnoreCase);
}

public static class MessageFeed
{
    public const string FeedUrl = "https://raw.githubusercontent.com/ibrata2/ibdm-pro/main/messages.json";

    private static string SeenFile => Path.Combine(Paths.AppData, "messages-seen.json");
    private static string CacheFile => Path.Combine(Paths.AppData, "messages.json");

    /// <summary>Reads the list (newest first). Keeps a copy so the messages window works offline.</summary>
    public static async Task<List<AppMessage>> FetchAsync(HttpClient http, string? url = null, CancellationToken ct = default)
    {
        // A query string so a cached copy is not served for hours.
        var u = (url ?? FeedUrl) + "?t=" + DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute;
        var json = await http.GetStringAsync(u, ct);
        var list = Parse(json);
        try { File.WriteAllText(CacheFile, json); } catch (Exception) { }
        return list;
    }

    public static List<AppMessage> Parse(string json)
    {
        var list = JsonSerializer.Deserialize<List<AppMessage>>(json, Json.Web) ?? [];
        return list.Where(m => !string.IsNullOrWhiteSpace(m.Id) && (m.Text.Length > 0 || m.Title.Length > 0))
            .OrderByDescending(m => m.When).ToList();
    }

    /// <summary>The last list read (for the messages window when offline).</summary>
    public static List<AppMessage> Cached()
    {
        try { return File.Exists(CacheFile) ? Parse(File.ReadAllText(CacheFile)) : []; }
        catch (Exception) { return []; }
    }

    /// <summary>Messages meant for this user (language, not expired).</summary>
    public static List<AppMessage> Current(IEnumerable<AppMessage> all, string lang, DateTime nowUtc) =>
        all.Where(m => m.IsFor(lang) && !m.IsExpired(nowUtc)).ToList();

    public static HashSet<string> Seen()
    {
        try { return File.Exists(SeenFile) ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(SeenFile)) ?? [] : []; }
        catch (Exception) { return []; }
    }

    public static void MarkSeen(IEnumerable<string> ids)
    {
        var seen = Seen();
        foreach (var id in ids) seen.Add(id);
        try { Json.SaveAtomic(SeenFile, seen); } catch (Exception) { }
    }

    /// <summary>New messages (not shown before), oldest first so they are shown in order.</summary>
    public static List<AppMessage> Unseen(IEnumerable<AppMessage> current) =>
        current.Where(m => !Seen().Contains(m.Id)).OrderBy(m => m.When).ToList();

    private static string DismissedFile => Path.Combine(Paths.AppData, "ads-closed.json");

    /// <summary>The ad shown in the side bar: the newest ad / new-program message the user has not closed.</summary>
    public static AppMessage? CurrentAd(IEnumerable<AppMessage> current)
    {
        HashSet<string> closed;
        try { closed = File.Exists(DismissedFile) ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(DismissedFile)) ?? [] : []; }
        catch (Exception) { closed = []; }
        return current.Where(m => m.IsAd && !closed.Contains(m.Id)).OrderByDescending(m => m.When).FirstOrDefault();
    }

    public static void CloseAd(string id)
    {
        HashSet<string> closed;
        try { closed = File.Exists(DismissedFile) ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(DismissedFile)) ?? [] : []; }
        catch (Exception) { closed = []; }
        closed.Add(id);
        try { Json.SaveAtomic(DismissedFile, closed); } catch (Exception) { }
    }

    /// <summary>On a first run nothing old pops up: everything already published counts as seen.</summary>
    public static bool FirstRun => !File.Exists(SeenFile);
}
