using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace IbdmPro.Core;

/// <summary>Suno (suno.com) songs. The download tool yt-dlp refuses Suno, but every song is a plain MP3 file
/// at cdn1.suno.ai/&lt;song id&gt;.mp3 — the same id as in the song's page (suno.com/song/&lt;id&gt;).</summary>
public static class Suno
{
    private static readonly Regex Id = new(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase);
    private static readonly Regex Host = new(@"^https?://(?:[\w-]+\.)*suno\.(?:com|ai)(?:[/?#:]|$)", RegexOptions.IgnoreCase);

    public static bool IsSuno(string? url) => url is not null && Host.IsMatch(url);

    /// <summary>The song's MP3 address from a song page, a share link with the id, or a Suno sound address; else null.</summary>
    public static string? FileUrl(string? url)
    {
        if (!IsSuno(url)) return null;
        var m = Id.Match(url!);
        return m.Success ? $"https://cdn1.suno.ai/{m.Value.ToLowerInvariant()}.mp3" : null;
    }

    /// <summary>The song's video (the picture with the song, made for sharing): Suno now refuses the MP3 to
    /// anyone but paying members, but the video is public — its sound is the song.</summary>
    public static string? VideoUrl(string? fileUrl) => fileUrl is null ? null : Regex.Replace(fileUrl, @"\.(mp3|m4a)$", ".mp4", RegexOptions.IgnoreCase);

    /// <summary>The song's name from its page (og:title / title, without "| Suno"), or null.</summary>
    public static string? NameFromPage(string html)
    {
        var m = Regex.Match(html, @"<meta[^>]+property=[""']og:title[""'][^>]*content=[""']([^""']+)", RegexOptions.IgnoreCase);
        if (!m.Success) m = Regex.Match(html, @"<meta[^>]+content=[""']([^""']+)[""'][^>]*property=[""']og:title", RegexOptions.IgnoreCase);
        if (!m.Success) m = Regex.Match(html, @"<title[^>]*>([^<]+)</title>", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var name = WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
        name = Regex.Replace(name, @"\s*[|\-–—]\s*Suno(?:\s*AI)?\s*$", "", RegexOptions.IgnoreCase);
        return name.Length > 0 && !name.Equals("Suno", StringComparison.OrdinalIgnoreCase) ? name : null;
    }

    public static async Task<string?> NameAsync(HttpClient http, string pageUrl, CancellationToken ct)
    {
        if (!Id.IsMatch(pageUrl) || !pageUrl.Contains("/song/", StringComparison.OrdinalIgnoreCase)) return null;
        try { return NameFromPage(await http.GetStringAsync(pageUrl, ct)); }
        catch (Exception e) when (e is not OperationCanceledException) { return null; }
    }
}
