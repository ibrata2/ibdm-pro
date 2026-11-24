import Foundation
import UIKit
import WebKit

/// A link that opens a web page (Instagram, TikTok, Facebook, any site) instead of a file: find the video in it.
/// First from the page's text (the links sites put there for players and previews), and if there is none, by
/// opening the page in a hidden browser for a few seconds and taking the file the page's player plays.
@MainActor
enum PageVideo {
    struct Found { var title: String?; var videos: [URL] }

    static let mediaExt = ["mp4", "m4v", "mov", "webm", "m4a", "mp3"]

    // ---------- 1. from the page's text ----------

    static func fromHTML(_ raw: String, base: URL) -> Found {
        let html = unescape(raw)
        var list: [URL] = []
        func add(_ s: String) {
            var s = s.trimmingCharacters(in: .whitespacesAndNewlines)
            if s.hasPrefix("//") { s = "https:" + s }
            guard !s.hasPrefix("blob:"), !s.hasPrefix("data:"), let u = URL(string: s, relativeTo: base)?.absoluteURL,
                  ["http", "https"].contains(u.scheme?.lowercased() ?? ""), !isStream(u), !list.contains(u) else { return }
            list.append(u)
        }
        // Best first: the high-quality fields of Facebook, then TikTok, Instagram, schema.org, the preview tags, the
        // page's own <video>, and at last any video file link written in the page.
        let keys = ["playable_url_quality_hd", "browser_native_hd_url", "hd_src_no_ratelimit", "hd_src", "playable_url",
                    "browser_native_sd_url", "sd_src_no_ratelimit", "sd_src", "playAddr", "downloadAddr", "video_url", "contentUrl"]
        for k in keys { for v in matches(#""\#(k)"\s*:\s*"([^"]+)""#, html) { add(v) } }
        for p in ["og:video:secure_url", "og:video:url", "og:video", "twitter:player:stream"] {
            for v in matches(#"<meta[^>]+(?:property|name)\s*=\s*["']\#(p)["'][^>]*content\s*=\s*["']([^"']+)["']"#, html) { add(v) }
            for v in matches(#"<meta[^>]+content\s*=\s*["']([^"']+)["'][^>]*(?:property|name)\s*=\s*["']\#(p)["']"#, html) { add(v) }
        }
        for v in matches(#"<(?:video|source)[^>]+src\s*=\s*["']([^"']+)["']"#, html) { add(v) }
        for v in matches(#"(https?://[^\s"'<>\\]+?\.(?:mp4|m4v|mov|webm)(?:\?[^\s"'<>\\]*)?)"#, html) { add(v) }
        return Found(title: title(html), videos: list)
    }

    static func title(_ html: String) -> String? {
        let t = matches(#"<meta[^>]+property\s*=\s*["']og:title["'][^>]*content\s*=\s*["']([^"']+)["']"#, html).first
            ?? matches(#"<title[^>]*>([^<]+)</title>"#, html).first
        guard let t else { return nil }
        let clean = htmlText(t).trimmingCharacters(in: .whitespacesAndNewlines)
        return clean.isEmpty ? nil : String(clean.prefix(100))
    }

    /// HLS / DASH lists are not files; the phone can't save them as one file.
    static func isStream(_ u: URL) -> Bool { ["m3u8", "mpd"].contains(u.pathExtension.lowercased()) }

    private static func matches(_ pattern: String, _ s: String) -> [String] {
        guard let re = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]) else { return [] }
        return re.matches(in: s, range: NSRange(s.startIndex..., in: s)).compactMap { m in
            m.numberOfRanges > 1 ? Range(m.range(at: 1), in: s).map { String(s[$0]) } : nil
        }
    }

    /// Links inside page scripts are written with escapes (\/ & / &amp;).
    private static func unescape(_ s: String) -> String {
        var r = s.replacingOccurrences(of: "\\/", with: "/")
        for (a, b) in [("\\u0026", "&"), ("\\u002F", "/"), ("\\u002f", "/"), ("\\u003D", "="), ("\\u003d", "="), ("&amp;", "&")] {
            r = r.replacingOccurrences(of: a, with: b)
        }
        return r
    }

    private static func htmlText(_ s: String) -> String {
        var r = s
        for (a, b) in [("&amp;", "&"), ("&quot;", "\""), ("&#39;", "'"), ("&#x27;", "'"), ("&lt;", "<"), ("&gt;", ">")] { r = r.replacingOccurrences(of: a, with: b) }
        return r
    }

    // ---------- 2. by playing the page in a hidden browser ----------

    private static var holder: WKWebView?

    /// Opens the page (like Safari on the phone), starts its video muted, and for up to `seconds` collects the video
    /// file links the page uses. The page's cookies are handed to the downloader so the file opens for it too.
    static func fromBrowser(_ page: URL, seconds: Int = 15) async -> Found {
        let cfg = WKWebViewConfiguration()
        cfg.allowsInlineMediaPlayback = true
        cfg.mediaTypesRequiringUserActionForPlayback = []
        let web = WKWebView(frame: CGRect(x: 0, y: 0, width: 390, height: 844), configuration: cfg)
        web.customUserAgent = Downloader.userAgent
        web.alpha = 0.01
        web.isUserInteractionEnabled = false
        // a web view plays and runs its scripts only while it is in a window
        if let window = UIApplication.shared.connectedScenes.compactMap({ ($0 as? UIWindowScene)?.keyWindow }).first {
            window.insertSubview(web, at: 0)
        }
        holder = web
        defer { web.stopLoading(); web.removeFromSuperview(); holder = nil }
        web.load(URLRequest(url: page))

        let script = """
        (() => {
          const out = [];
          document.querySelectorAll('video').forEach(v => {
            try { v.muted = true; v.play && v.play().catch(()=>{}); } catch (e) {}
            [v.currentSrc, v.src].forEach(s => s && out.push(s));
            v.querySelectorAll('source').forEach(s => s.src && out.push(s.src));
          });
          try { performance.getEntriesByType('resource').forEach(e => out.push(e.name)); } catch (e) {}
          return JSON.stringify({ t: document.title || '', u: out, h: document.documentElement ? document.documentElement.outerHTML.slice(0, 3000000) : '' });
        })()
        """
        var found = Found(title: nil, videos: [])
        for _ in 0..<seconds {
            try? await Task.sleep(nanoseconds: 1_000_000_000)
            guard let json = try? await web.evaluateJavaScript(script) as? String,
                  let obj = try? JSONSerialization.jsonObject(with: Data(json.utf8)) as? [String: Any] else { continue }
            let base = web.url ?? page
            var list: [URL] = []
            for s in (obj["u"] as? [String]) ?? [] {
                guard !s.hasPrefix("blob:"), var u = URL(string: s), ["http", "https"].contains(u.scheme ?? "") else { continue }
                // players that fetch a file in pieces add the piece's bytes to the link; without them it is the whole file
                if var c = URLComponents(url: u, resolvingAgainstBaseURL: false), let q = c.queryItems,
                   q.contains(where: { ["bytestart", "byteend", "range"].contains($0.name.lowercased()) }) {
                    c.queryItems = q.filter { !["bytestart", "byteend", "range"].contains($0.name.lowercased()) }
                    u = c.url ?? u
                }
                let ext = u.pathExtension.lowercased()
                let looksVideo = mediaExt.contains(ext) || u.absoluteString.range(of: #"(video|\.mp4|mime_type=video)"#, options: [.regularExpression, .caseInsensitive]) != nil
                if looksVideo && !isStream(u) && !list.contains(u) { list.append(u) }
            }
            let fromPage = fromHTML((obj["h"] as? String) ?? "", base: base)
            for u in fromPage.videos where !list.contains(u) { list.append(u) }
            let t = ((obj["t"] as? String).flatMap { $0.isEmpty ? nil : $0 }) ?? fromPage.title
            found = Found(title: fromPage.title ?? t, videos: list)
            if !list.isEmpty { break }
        }
        // the page's cookies → the downloader (some sites only give the file to the visitor who opened the page)
        let cookies = await web.configuration.websiteDataStore.httpCookieStore.allCookies()
        for c in cookies { HTTPCookieStorage.shared.setCookie(c) }
        return found
    }
}
