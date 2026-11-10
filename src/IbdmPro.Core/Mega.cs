using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IbdmPro.Core;

/// <summary>MEGA (mega.nz) public file links. The file is stored encrypted; the key is in the link after "#".
/// The app asks MEGA's API for a download address, downloads the encrypted file with its normal engine
/// (connections, pause, resume), then decrypts it (AES-128-CTR) when it is complete.</summary>
public static class Mega
{
    public sealed record FileInfo(string Name, long Size, string DownloadUrl);

    private static readonly Regex NewLink = new(@"mega\.(?:nz|co\.nz|io)/file/([A-Za-z0-9_-]{8,})#([A-Za-z0-9_-]{43,})", RegexOptions.IgnoreCase);
    private static readonly Regex OldLink = new(@"mega\.(?:nz|co\.nz)/#!([A-Za-z0-9_-]{8,})!([A-Za-z0-9_-]{43,})", RegexOptions.IgnoreCase);
    private static readonly Regex FolderLink = new(@"mega\.(?:nz|co\.nz)/(?:folder/|#F!)", RegexOptions.IgnoreCase);

    public static bool IsFileLink(string? url) => url is not null && (NewLink.IsMatch(url) || OldLink.IsMatch(url));
    public static bool IsFolderLink(string? url) => url is not null && FolderLink.IsMatch(url);

    /// <summary>(file handle, key) from a link, or null.</summary>
    public static (string Handle, string Key)? Parse(string url)
    {
        var m = NewLink.Match(url);
        if (!m.Success) m = OldLink.Match(url);
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Value) : null;
    }

    /// <summary>Name, size and a fresh download address (the address works for a few hours).</summary>
    public static async Task<FileInfo> GetFileAsync(HttpClient http, string url, CancellationToken ct = default)
    {
        var (handle, keyText) = Parse(url) ?? throw new InvalidOperationException(L.T("رابط MEGA غير صحيح"));
        var body = JsonSerializer.Serialize(new object[] { new { a = "g", g = 1, ssl = 2, p = handle } });
        using var resp = await http.PostAsync($"https://g.api.mega.co.nz/cs?id={Random.Shared.Next(1, int.MaxValue)}", new StringContent(body, Encoding.UTF8, "application/json"), ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Number) throw new InvalidOperationException(Error(root.GetInt32()));
        var first = root[0];
        if (first.ValueKind == JsonValueKind.Number) throw new InvalidOperationException(Error(first.GetInt32()));
        if (first.TryGetProperty("e", out var e) && e.ValueKind == JsonValueKind.Number) throw new InvalidOperationException(Error(e.GetInt32()));
        var size = first.GetProperty("s").GetInt64();
        var g = first.TryGetProperty("g", out var gv) ? gv.GetString() : null;
        if (string.IsNullOrEmpty(g)) throw new InvalidOperationException(L.T("MEGA لم يعطِ رابط تحميل لهذا الملف (ربما تجاوزت حد التحميل المجاني، انتظر قليلاً)."));
        var name = DecryptName(first.GetProperty("at").GetString() ?? "", keyText) ?? handle;
        return new FileInfo(FileNames.Clean(name), size, g);
    }

    private static string Error(int code) => code switch
    {
        -9 or -2 => L.T("الملف غير موجود على MEGA (ربما حُذف أو أن الرابط ناقص)."),
        -16 => L.T("الملف محظور على MEGA."),
        -17 or -18 => L.T("MEGA مشغول أو تجاوزت حد التحميل المجاني — انتظر قليلاً ثم أعد المحاولة."),
        -11 => L.T("الوصول للملف مرفوض من MEGA."),
        _ => L.F("خطأ من MEGA ({0})", code),
    };

    // ---------- the key ----------

    public static byte[] B64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/').Replace(",", "");
        s += new string('=', (4 - s.Length % 4) % 4);
        return Convert.FromBase64String(s);
    }

    /// <summary>The 128-bit AES key and the 8-byte nonce from the link's 256-bit key.</summary>
    public static (byte[] Key, byte[] Nonce) Keys(string keyText)
    {
        var k = B64(keyText);
        if (k.Length < 32) throw new InvalidOperationException(L.T("مفتاح رابط MEGA ناقص"));
        var aes = new byte[16];
        for (var i = 0; i < 16; i++) aes[i] = (byte)(k[i] ^ k[i + 16]);
        return (aes, k[16..24]);
    }

    /// <summary>The file name stored (encrypted) in the file's attributes: AES-CBC, zero IV, "MEGA{json}".</summary>
    public static string? DecryptName(string at, string keyText)
    {
        try
        {
            var (key, _) = Keys(keyText);
            var data = B64(at);
            using var aes = Aes.Create();
            aes.Key = key;
            var plain = aes.DecryptCbc(data, new byte[16], PaddingMode.None);
            var text = Encoding.UTF8.GetString(plain).TrimEnd('\0');
            if (!text.StartsWith("MEGA{")) return null;
            using var doc = JsonDocument.Parse(text[4..]);
            return doc.RootElement.TryGetProperty("n", out var n) ? n.GetString() : null;
        }
        catch (Exception) { return null; }
    }

    // ---------- the file ----------

    /// <summary>Decrypts (in place, through a temporary file) a downloaded MEGA file.</summary>
    public static Task DecryptFileAsync(string path, string keyText, Action<double>? progress = null, CancellationToken ct = default) =>
        DecryptToAsync(path, path, keyText, progress, ct);

    /// <summary>Decrypts <paramref name="source"/> into <paramref name="target"/> (written whole, then put in place).</summary>
    public static async Task DecryptToAsync(string source, string target, string keyText, Action<double>? progress = null, CancellationToken ct = default)
    {
        var (key, nonce) = Keys(keyText);
        var tmp = target + ".decrypting";
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true))
        await using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true))
        {
            var total = input.Length;
            var buffer = new byte[1 << 20];
            long offset = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                Ctr(key, nonce, offset, buffer.AsSpan(0, read));
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                offset += read;
                progress?.Invoke(total > 0 ? (double)offset / total : 1);
            }
        }
        File.Move(tmp, target, true);
    }

    /// <summary>AES-128-CTR on <paramref name="data"/>, which sits at byte <paramref name="offset"/> of the file.
    /// The counter block is the 8-byte nonce followed by the 64-bit block number (big-endian).</summary>
    public static void Ctr(byte[] key, byte[] nonce, long offset, Span<byte> data)
    {
        if (data.Length == 0) return;
        using var aes = Aes.Create();
        aes.Key = key;
        var first = offset / 16;
        var skip = (int)(offset % 16);
        var blocks = (skip + data.Length + 15) / 16;
        // All the counter blocks of this piece, encrypted in one call (fast), then XORed with the data.
        var counters = new byte[blocks * 16];
        for (var n = 0; n < blocks; n++)
        {
            var at = n * 16;
            nonce.CopyTo(counters, at);
            var block = first + n;
            for (var b = 0; b < 8; b++) counters[at + 15 - b] = (byte)(block >> (8 * b));
        }
        var stream = aes.EncryptEcb(counters, PaddingMode.None);
        for (var i = 0; i < data.Length; i++) data[i] ^= stream[skip + i];
    }
}
