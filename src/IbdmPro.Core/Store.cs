using System.Net.Http;
using System.Text.Json;

namespace IbdmPro.Core;

/// <summary>A program or game published on the developer's site (IBRA STORE).</summary>
public sealed class StoreApp
{
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Tagline { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Icon { get; set; }
    public string? Screenshot { get; set; }
    public DateTime Created { get; set; }

    /// <summary>The picture behind the slide: the first screenshot, or the icon.</summary>
    public string? Background => Screenshot ?? Icon;

    public string PageUrl => StoreFeed.Site + "app/" + Uri.EscapeDataString(Slug);
}

/// <summary>The latest programs and games from IBRA STORE (https://ibraprogram.netlify.app/api/apps),
/// shown as a slide show in the side bar. A copy is kept so the slides still show without internet.</summary>
public static class StoreFeed
{
    public const string Site = "https://ibraprogram.netlify.app/";
    public const string ApiUrl = Site + "api/apps";
    /// <summary>How many slides (the newest ones).</summary>
    public const int Max = 10;

    private static string CacheFile => Path.Combine(Paths.AppData, "store-apps.json");

    public static async Task<List<StoreApp>> FetchAsync(HttpClient http, string? lang = null, CancellationToken ct = default)
    {
        var json = await http.GetStringAsync(ApiUrl + "?t=" + DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute, ct);
        var list = Parse(json, lang);
        if (list.Count > 0) try { File.WriteAllText(CacheFile, json); } catch (Exception) { }
        return list;
    }

    public static List<StoreApp> Cached(string? lang = null)
    {
        try { return File.Exists(CacheFile) ? Parse(File.ReadAllText(CacheFile), lang) : []; }
        catch (Exception) { return []; }
    }

    /// <summary>Reads the list (a plain array, or an object holding it under "apps"/"items"/"data"),
    /// newest first, the first <see cref="Max"/>. Pictures given as site paths become full links.</summary>
    public static List<StoreApp> Parse(string json, string? lang = null)
    {
        var list = new List<StoreApp>();
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement;
        if (arr.ValueKind == JsonValueKind.Object)
            foreach (var key in new[] { "apps", "items", "data", "results" })
                if (arr.TryGetProperty(key, out var inner) && inner.ValueKind == JsonValueKind.Array) { arr = inner; break; }
        if (arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var e in arr.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            var a = new StoreApp
            {
                Name = Str(Get(e, "name", "title"), lang),
                Slug = Str(Get(e, "slug", "id"), lang),
                Tagline = Str(Get(e, "tagline", "summary", "description"), lang),
                Category = First(Get(e, "categories", "category"), lang),
                Icon = Link(Str(Get(e, "icon", "logo"), lang)),
                Screenshot = Link(First(Get(e, "screenshots", "images"), lang)),
                Created = Date(Get(e, "createdAt", "created_at", "date")),
            };
            if (a.Name.Length == 0 || a.Slug.Length == 0) continue;
            list.Add(a);
        }
        return list.OrderByDescending(a => a.Created).Take(Max).ToList();
    }

    private static JsonElement? Get(JsonElement o, params string[] names)
    {
        foreach (var n in names)
            if (o.TryGetProperty(n, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)) return v;
        return null;
    }

    /// <summary>A text: a plain string, a number, or {"ar": …, "en": …} (the user's language first), or {"name"/"url": …}.</summary>
    private static string Str(JsonElement? v, string? lang)
    {
        if (v is not { } e) return "";
        switch (e.ValueKind)
        {
            case JsonValueKind.String: return e.GetString()!.Trim();
            case JsonValueKind.Number: return e.GetRawText();
            case JsonValueKind.Object:
                foreach (var k in new[] { lang ?? "ar", "ar", "en", "name", "title", "url", "src" })
                    if (e.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.String) return x.GetString()!.Trim();
                return "";
            default: return "";
        }
    }

    /// <summary>The first item of a list (or the value itself when it is not a list).</summary>
    private static string First(JsonElement? v, string? lang)
    {
        if (v is { ValueKind: JsonValueKind.Array } a)
        {
            foreach (var x in a.EnumerateArray()) { var s = Str(x, lang); if (s.Length > 0) return s; }
            return "";
        }
        return Str(v, lang);
    }

    private static string? Link(string s)
    {
        if (s.Length == 0) return null;
        if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return s;
        if (s.StartsWith("//")) return "https:" + s;
        return Uri.TryCreate(new Uri(Site), s, out var u) ? u.ToString() : null;
    }

    private static DateTime Date(JsonElement? v)
    {
        if (v is not { } e) return DateTime.MinValue;
        // Firestore-style {"seconds": …} / {"_seconds": …}
        if (e.ValueKind == JsonValueKind.Object)
            foreach (var k in new[] { "seconds", "_seconds", "$date" })
                if (e.TryGetProperty(k, out var x)) return Date(x);
        if (e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var n))
            return n > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(n).UtcDateTime : DateTimeOffset.FromUnixTimeSeconds(n).UtcDateTime;
        if (e.ValueKind == JsonValueKind.String && DateTime.TryParse(e.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var d)) return d;
        return DateTime.MinValue;
    }
}
