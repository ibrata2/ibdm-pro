import Foundation

/// Instagram posts and reels: the page itself has no video for a visitor who is not signed in, so ask Instagram's
/// own web data for the post (the way its website does) — that gives the full video file, with its sound.
@MainActor
enum Instagram {
    struct Media { let video: URL; let title: String? }

    /// The post's code from a link like instagram.com/reel/ABC123/ or instagram.com/user/p/ABC123/.
    static func shortcode(_ s: String) -> String? {
        guard let u = URL(string: s), let host = u.host?.lowercased(), host == "instagram.com" || host.hasSuffix(".instagram.com") else { return nil }
        let parts = u.pathComponents.filter { $0 != "/" }
        for (i, p) in parts.enumerated() where ["p", "reel", "reels", "tv"].contains(p) && i + 1 < parts.count { return parts[i + 1] }
        return nil
    }

    static func fetch(_ link: String) async -> Media? {
        guard let code = shortcode(link) else { return nil }
        let page = "https://www.instagram.com/p/\(code)/"
        // A first visit gives the site's cookies (csrftoken), which the data request needs.
        var visit = URLRequest(url: URL(string: page)!)
        visit.setValue(Downloader.userAgent, forHTTPHeaderField: "User-Agent")
        _ = try? await URLSession.shared.data(for: visit)
        let csrf = HTTPCookieStorage.shared.cookies(for: URL(string: "https://www.instagram.com/")!)?.first(where: { $0.name == "csrftoken" })?.value ?? ""

        let variables = "{\"shortcode\":\"\(code)\",\"child_comment_count\":3,\"fetch_comment_count\":40,\"parent_comment_count\":24,\"has_threaded_comments\":true}"
        var c = URLComponents(string: "https://www.instagram.com/graphql/query/")!
        c.queryItems = [URLQueryItem(name: "doc_id", value: "8845758582119845"), URLQueryItem(name: "variables", value: variables)]
        guard let api = c.url else { return nil }
        var req = URLRequest(url: api)
        let headers = ["User-Agent": Downloader.userAgent, "X-IG-App-ID": "936619743392459", "X-ASBD-ID": "198387", "X-IG-WWW-Claim": "0",
                       "X-CSRFToken": csrf, "X-Requested-With": "XMLHttpRequest", "Referer": page, "Origin": "https://www.instagram.com", "Accept": "*/*"]
        for (k, v) in headers { req.setValue(v, forHTTPHeaderField: k) }
        guard let (data, _) = try? await URLSession.shared.data(for: req),
              let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let media = (root["data"] as? [String: Any])?["xdt_shortcode_media"] as? [String: Any] else { return nil }

        // A post with several pictures/videos: the first video in it.
        var node = media
        if node["video_url"] as? String == nil,
           let edges = (media["edge_sidecar_to_children"] as? [String: Any])?["edges"] as? [[String: Any]],
           let video = edges.compactMap({ $0["node"] as? [String: Any] }).first(where: { $0["video_url"] is String }) {
            node = video
        }
        guard let s = node["video_url"] as? String, let url = URL(string: s) else { return nil }
        let user = (media["owner"] as? [String: Any])?["username"] as? String
        let captionEdges = (media["edge_media_to_caption"] as? [String: Any])?["edges"] as? [[String: Any]]
        let caption = (captionEdges?.first?["node"] as? [String: Any])?["text"] as? String
        let firstLine = caption?.split(separator: "\n").first.map { String($0.prefix(60)) }
        let title = [user, firstLine].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " — ")
        return Media(video: url, title: title.isEmpty ? nil : title)
    }
}
