using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IbdmPro.Core;

/// <summary>Pinterest boards (albums): every pin of a public board — pictures in their original size, videos in their
/// best quality — read from Pinterest's own web API (the one its site uses; no login needed for public boards).</summary>
public static class Pinterest
{
    public sealed record Board(string Id, string Name, string Owner, int PinCount, int SectionCount, string Root, string? SectionId, string? SectionName);

    /// <summary>One file of the board. <see cref="Hls"/>: a video stream (downloaded with yt-dlp), else a plain file.
    /// <see cref="Section"/>: the board section it is in (its own sub-folder), or null.</summary>
    public sealed record Media(string Url, string Name, string Extension, bool IsVideo, bool Hls, string? Section, string PinUrl);

    private static readonly Regex BoardLink = new(
        @"^https?://(?:[\w-]+\.)?pinterest\.[\w.]+/(?!pin/|search/|ideas/|today/|explore/|business/|settings/|_)([^/?#]+)/(?!_)([^/?#]+)/?(?:(?!_)([^/?#]+)/?)?(?:[?#].*)?$",
        RegexOptions.IgnoreCase);
    private static readonly Regex ShortLink = new(@"^https?://(?:www\.)?pin\.it/\w+", RegexOptions.IgnoreCase);

    public static bool IsBoardLink(string? url) => url is not null && BoardLink.IsMatch(url.Trim());
    public static bool IsShortLink(string? url) => url is not null && ShortLink.IsMatch(url.Trim());
    public static bool IsPinterest(string? url) => IsBoardLink(url) || IsShortLink(url);

    /// <summary>(site root, user, board, section) from a board link, or null.</summary>
    public static (string Root, string User, string Board, string? Section)? Parse(string url)
    {
        var m = BoardLink.Match(url.Trim());
        if (!m.Success) return null;
        var u = new Uri(url.Trim());
        return ($"{u.Scheme}://{u.Host}", Uri.UnescapeDataString(m.Groups[1].Value), Uri.UnescapeDataString(m.Groups[2].Value),
            m.Groups[3].Success ? Uri.UnescapeDataString(m.Groups[3].Value) : null);
    }

    /// <summary>A pin.it short link opens the board (or pin) it was shared from: follow it.</summary>
    public static async Task<string> ResolveAsync(HttpClient http, string url, CancellationToken ct)
    {
        url = url.Trim();
        if (!IsShortLink(url)) return url;
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", Http.DefaultUserAgent);
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var final = resp.RequestMessage?.RequestUri?.ToString() ?? url;
        // pinterest.com/url_shortener/…/redirect/?… : the real address is in the page it redirects to.
        return final.Split('?')[0].Contains("/pin/") || IsBoardLink(final) ? final.Split('?')[0] : final;
    }

    private static async Task<JsonElement> CallAsync(HttpClient http, string root, string resource, object options, CancellationToken ct)
    {
        var data = JsonSerializer.Serialize(new { options });
        var url = $"{root}/resource/{resource}Resource/get/?data={Uri.EscapeDataString(data)}&source_url=";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*, q=0.01");
        req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        req.Headers.TryAddWithoutValidation("X-APP-VERSION", "a89153f");
        req.Headers.TryAddWithoutValidation("X-Pinterest-AppState", "active");
        req.Headers.TryAddWithoutValidation("X-Pinterest-PWS-Handler", "www/[username].js");
        req.Headers.TryAddWithoutValidation("User-Agent", Http.DefaultUserAgent);
        req.Headers.TryAddWithoutValidation("Cookie", "csrftoken=" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());
        using var resp = await http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if ((int)resp.StatusCode == 404) throw new InvalidOperationException(L.T("الألبوم غير موجود على Pinterest (ربما الرابط غلط أو الألبوم خاص)."));
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException(L.F("رفض Pinterest الطلب ({0})", (int)resp.StatusCode));
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    public static async Task<Board> GetBoardAsync(HttpClient http, string url, CancellationToken ct)
    {
        url = await ResolveAsync(http, url, ct);
        var p = Parse(url) ?? throw new InvalidOperationException(L.T("هذا ليس رابط ألبوم (لوحة) على Pinterest. افتح الألبوم وانسخ الرابط من شريط العنوان."));
        var b = (await CallAsync(http, p.Root, "Board", new { slug = p.Board, username = p.User, field_set_key = "detailed" }, ct))
            .GetProperty("resource_response").GetProperty("data");
        if (b.ValueKind != JsonValueKind.Object) throw new InvalidOperationException(L.T("الألبوم غير موجود على Pinterest (ربما الرابط غلط أو الألبوم خاص)."));
        string? sectionId = null, sectionName = null;
        if (p.Section is not null)
        {
            var s = (await CallAsync(http, p.Root, "BoardSection", new { board_slug = p.Board, section_slug = p.Section, username = p.User }, ct))
                .GetProperty("resource_response").GetProperty("data");
            if (s.ValueKind == JsonValueKind.Object) { sectionId = Str(s, "id"); sectionName = Str(s, "title"); }
        }
        var owner = b.TryGetProperty("owner", out var o) && o.ValueKind == JsonValueKind.Object ? Str(o, "username") ?? p.User : p.User;
        return new Board(Str(b, "id") ?? "", Str(b, "name") ?? p.Board, owner, Int(b, "pin_count"), Int(b, "section_count"), p.Root, sectionId, sectionName);
    }

    /// <summary>Every file of the board (the board's own pins, then each section's), in the board's order.</summary>
    public static async Task<List<Media>> GetMediaAsync(HttpClient http, Board board, Action<int>? progress = null, CancellationToken ct = default)
    {
        var all = new List<Media>();
        var seen = new HashSet<string>();
        async Task Feed(string resource, Dictionary<string, object?> options, string? section)
        {
            for (var page = 0; page < 400; page++)
            {
                var res = await CallAsync(http, board.Root, resource, options, ct);
                var data = res.GetProperty("resource_response").GetProperty("data");
                if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("results", out var r)) data = r;
                if (data.ValueKind != JsonValueKind.Array) break;
                foreach (var pin in data.EnumerateArray())
                {
                    var id = Str(pin, "id");
                    if (id is null || !seen.Add(id)) continue;
                    all.AddRange(FromPin(pin, board.Root, section));
                }
                progress?.Invoke(all.Count);
                var next = NextBookmark(res);
                if (next is null) break;
                options["bookmarks"] = new[] { next };
            }
        }
        if (board.SectionId is not null)
        {
            await Feed("BoardSectionPins", new() { ["section_id"] = board.SectionId, ["page_size"] = 50 }, null);
            return all;
        }
        await Feed("BoardFeed", new() { ["board_id"] = board.Id, ["field_set_key"] = "react_grid_pin", ["prepend"] = false, ["page_size"] = 50, ["bookmarks"] = null }, null);
        if (board.SectionCount > 0)
        {
            var sections = new List<(string Id, string Title)>();
            var options = new Dictionary<string, object?> { ["board_id"] = board.Id };
            for (var page = 0; page < 50; page++)
            {
                var res = await CallAsync(http, board.Root, "BoardSections", options, ct);
                var data = res.GetProperty("resource_response").GetProperty("data");
                if (data.ValueKind != JsonValueKind.Array) break;
                foreach (var s in data.EnumerateArray())
                    if (Str(s, "id") is { } sid) sections.Add((sid, Str(s, "title") ?? sid));
                var next = NextBookmark(res);
                if (next is null) break;
                options["bookmarks"] = new[] { next };
            }
            foreach (var (sid, title) in sections)
                await Feed("BoardSectionPins", new() { ["section_id"] = sid, ["page_size"] = 50 }, title);
        }
        return all;
    }

    private static string? NextBookmark(JsonElement res)
    {
        if (!res.TryGetProperty("resource", out var r) || !r.TryGetProperty("options", out var o) || !o.TryGetProperty("bookmarks", out var b)
            || b.ValueKind != JsonValueKind.Array || b.GetArrayLength() == 0) return null;
        var first = b[0].GetString();
        return string.IsNullOrEmpty(first) || first == "-end-" || first.StartsWith("Y2JOb25lO") ? null : first;
    }

    /// <summary>The files of one pin: its video (best quality), the pages of a story / carousel, or its picture in the
    /// original size.</summary>
    public static List<Media> FromPin(JsonElement pin, string root, string? section)
    {
        var list = new List<Media>();
        if (Str(pin, "type") is { } type && type != "pin") return list;
        var id = Str(pin, "id") ?? "";
        var pinUrl = $"{root}/pin/{id}/";
        var title = new[] { Str(pin, "title"), Str(pin, "grid_title"), Str(pin, "closeup_unified_title") }
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))?.Trim();
        var name = string.IsNullOrWhiteSpace(title) ? "Pinterest " + id : title!;
        if (name.Length > 80) name = name[..80].Trim();

        void Add(string url, bool video, bool hls)
        {
            var ext = hls ? ".mp4" : Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
            if (ext.Length is < 2 or > 5) ext = video ? ".mp4" : ".jpg";
            list.Add(new Media(url, name, ext, video, hls, section, pinUrl));
        }

        if (pin.TryGetProperty("story_pin_data", out var story) && story.ValueKind == JsonValueKind.Object
            && story.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Array)
        {
            foreach (var page in pages.EnumerateArray())
            {
                if (!page.TryGetProperty("blocks", out var blocks) || blocks.ValueKind != JsonValueKind.Array) continue;
                foreach (var block in blocks.EnumerateArray())
                {
                    if (block.TryGetProperty("video", out var v) && v.ValueKind == JsonValueKind.Object && BestVideo(v) is { } bv) { Add(bv.Url, true, bv.Hls); continue; }
                    var t = Str(block, "type") ?? "";
                    if (!t.Contains("image")) continue;
                    if (block.TryGetProperty("image", out var img) && img.ValueKind == JsonValueKind.Object && img.TryGetProperty("images", out var imgs)
                        && imgs.TryGetProperty("originals", out var orig) && Str(orig, "url") is { } ou) Add(ou, false, false);
                    else if ((Str(block, "image_signature") ?? Str(page, "image_signature")) is { Length: > 6 } sig)
                        Add($"https://i.pinimg.com/originals/{sig[..2]}/{sig[2..4]}/{sig[4..6]}/{sig}.jpg", false, false);
                }
            }
            if (list.Count > 0) return list;
        }
        if (pin.TryGetProperty("carousel_data", out var car) && car.ValueKind == JsonValueKind.Object
            && car.TryGetProperty("carousel_slots", out var slots) && slots.ValueKind == JsonValueKind.Array)
        {
            foreach (var slot in slots.EnumerateArray())
            {
                if (!slot.TryGetProperty("images", out var imgs) || imgs.ValueKind != JsonValueKind.Object) continue;
                var first = imgs.EnumerateObject().FirstOrDefault();
                if (first.Value.ValueKind == JsonValueKind.Object && Str(first.Value, "url") is { } u)
                    Add(u.Replace("/" + first.Name + "/", "/originals/"), false, false);
            }
            if (list.Count > 0) return list;
        }
        if (pin.TryGetProperty("videos", out var videos) && videos.ValueKind == JsonValueKind.Object && BestVideo(videos) is { } best)
        {
            Add(best.Url, true, best.Hls);
            return list;
        }
        if (pin.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Object)
        {
            if (images.TryGetProperty("orig", out var o) && Str(o, "url") is { } url) Add(url, false, false);
            else
            {
                // No "orig": the widest size there is.
                var widest = images.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object && Str(p.Value, "url") is not null)
                    .OrderByDescending(p => Int(p.Value, "width")).FirstOrDefault();
                if (widest.Value.ValueKind == JsonValueKind.Object) Add(Str(widest.Value, "url")!, false, false);
            }
        }
        return list;
    }

    /// <summary>The best video: the widest MP4 file; a stream (m3u8) only when it is wider or there is no MP4.</summary>
    private static (string Url, bool Hls)? BestVideo(JsonElement video)
    {
        if (!video.TryGetProperty("video_list", out var formats) || formats.ValueKind != JsonValueKind.Object) return null;
        (string Url, int Width)? mp4 = null, hls = null;
        foreach (var f in formats.EnumerateObject())
        {
            if (f.Value.ValueKind != JsonValueKind.Object || Str(f.Value, "url") is not { } u) continue;
            var w = Int(f.Value, "width");
            var path = u.Split('?')[0];
            if (path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)) { if (hls is null || w > hls.Value.Width) hls = (u, w); }
            else if (path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) { if (mp4 is null || w > mp4.Value.Width) mp4 = (u, w); }
        }
        if (mp4 is not null && (hls is null || mp4.Value.Width >= hls.Value.Width)) return (mp4.Value.Url, false);
        return hls is not null ? (hls.Value.Url, true) : null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        } : null;

    private static int Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;
}
