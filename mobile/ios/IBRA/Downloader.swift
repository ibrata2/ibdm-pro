import AVFoundation
import Foundation
import Photos
import UIKit
import YouTubeKit

/// Texts: Arabic, or English when the phone is not in Arabic.
func t(_ ar: String, _ en: String) -> String {
    (Locale.preferredLanguages.first ?? "en").hasPrefix("ar") ? ar : en
}

struct Job: Identifiable, Codable, Equatable {
    enum Status: String, Codable { case waiting, running, done, failed, cancelled }
    var id = UUID().uuidString
    var url: String
    var mode: String            // "video" or "audio"
    var title = ""
    var status: Status = .waiting
    var progress: Double = 0
    var detail = ""
    var file: String?           // the name of the file in the app's folder (Files → On My iPhone → IBRA)
    var error: String?
    var isVideo: Bool { ["mp4", "mov", "m4v"].contains((file as NSString?)?.pathExtension.lowercased() ?? "") }
    /// Plays in the app's player (picture and/or sound).
    var isPlayable: Bool { ["mp4", "mov", "m4v", "m4a", "mp3", "aac", "wav", "3gp"].contains((file as NSString?)?.pathExtension.lowercased() ?? "") }
}

/// Downloads one link at a time into the app's folder (visible in the Files app). On the iPhone the download tools
/// of the Windows and Android apps can't run, so: any direct file link, a web page with a video in it (found in the
/// page, or by playing the page in a hidden browser), and YouTube (up to 1080p, or the audio).
@MainActor
final class Downloader: NSObject, ObservableObject {
    static let shared = Downloader()
    @Published var jobs: [Job] = []
    private var running: Job.ID?
    private var task: URLSessionDownloadTask?
    private var session: URLSession!
    private var finish: ((URL?, URLResponse?, Error?) -> Void)?

    static var folder: URL { FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0] }
    private var listFile: URL { Downloader.folder.appendingPathComponent(".downloads.json") }

    override init() {
        super.init()
        session = URLSession(configuration: .default, delegate: self, delegateQueue: .main)
        if let data = try? Data(contentsOf: listFile), let saved = try? JSONDecoder().decode([Job].self, from: data) {
            jobs = saved.map { var j = $0; if j.status == .running { j.status = .waiting }; return j }
        }
        pump()
    }

    func add(_ url: String, mode: String) {
        jobs.insert(Job(url: url, mode: mode), at: 0)
        save()
        pump()
    }

    func cancel(_ id: Job.ID) {
        if running == id { task?.cancel() }
        update(id) { $0.status = .cancelled; $0.detail = "" }
    }

    func retry(_ id: Job.ID) { update(id) { $0.status = .waiting; $0.error = nil }; pump() }
    func remove(_ id: Job.ID) { jobs.removeAll { $0.id == id }; save() }
    func clearFinished() { jobs.removeAll { $0.status != .waiting && $0.status != .running }; save() }
    func path(_ j: Job) -> URL? { j.file.map { Downloader.folder.appendingPathComponent($0) } }

    func saveToPhotos(_ j: Job, done: @escaping (Bool) -> Void) {
        guard let url = path(j) else { return done(false) }
        PHPhotoLibrary.requestAuthorization(for: .addOnly) { status in
            guard status == .authorized || status == .limited else { DispatchQueue.main.async { done(false) }; return }
            PHPhotoLibrary.shared().performChanges({
                PHAssetChangeRequest.creationRequestForAssetFromVideo(atFileURL: url)
            }) { ok, _ in DispatchQueue.main.async { done(ok) } }
        }
    }

    private func save() {
        if let data = try? JSONEncoder().encode(Array(jobs.prefix(200))) { try? data.write(to: listFile) }
    }

    private func update(_ id: Job.ID, _ change: (inout Job) -> Void) {
        guard let i = jobs.firstIndex(where: { $0.id == id }) else { return }
        change(&jobs[i])
        save()
    }

    private func pump() {
        guard running == nil, let next = jobs.last(where: { $0.status == .waiting }) else { return }
        running = next.id
        update(next.id) { $0.status = .running; $0.progress = 0; $0.detail = t("جاري البحث عن الملف…", "Looking for the file…") }
        // Leaving the app doesn't stop the download at once (iOS gives it time; while something plays in the app's
        // player the app stays awake, so downloads go on to the end).
        var bg = UIBackgroundTaskIdentifier.invalid
        bg = UIApplication.shared.beginBackgroundTask(withName: "IBRA download") {
            UIApplication.shared.endBackgroundTask(bg)
            bg = .invalid
        }
        Task {
            await run(next)
            if bg != .invalid { UIApplication.shared.endBackgroundTask(bg) }
        }
    }

    private func run(_ job: Job) async {
        do {
            var source = URL(string: job.url)!
            var name: String?
            if Downloader.isYouTube(job.url) {
                let yt = YouTube(url: source, methods: [.local, .remote])
                let streams = try await yt.streams
                if streams.isEmpty {
                    throw Failure(t("يوتيوب ما أعطى أي جودة لهذا الفيديو — جرّب بعد قليل أو من شبكة ثانية", "YouTube gave no qualities for this video — try again later or on another network"))
                }
                let title = (try? await yt.metadata)?.title ?? yt.videoID
                update(job.id) { $0.title = title }
                if job.mode != "audio" {
                    // Best picture: YouTube keeps sound and picture apart above 360p, so take the best phone-playable
                    // MP4 picture (up to 1080p) and the M4A sound, and put them together on the phone.
                    let combined = streams.filterVideoAndAudio().filter { $0.isNativelyPlayable }.highestResolutionStream()
                    let picture = streams.filterVideoOnly().filter { $0.isNativelyPlayable && $0.fileExtension == .mp4 && ($0.videoResolution ?? 0) <= 1080 }.highestResolutionStream()
                    let sound = streams.filterAudioOnly().filter { $0.fileExtension == .m4a }.highestAudioBitrateStream()
                    if let picture, let sound, (picture.videoResolution ?? 0) > (combined?.videoResolution ?? 0) {
                        update(job.id) { $0.detail = t("تحميل الصورة…", "Downloading the picture…") }
                        let (v, _) = try await download(picture.url)
                        if jobs.first(where: { $0.id == job.id })?.status == .cancelled { return finished() }
                        update(job.id) { $0.detail = t("تحميل الصوت…", "Downloading the sound…") }
                        let (a, _) = try await download(sound.url)
                        if jobs.first(where: { $0.id == job.id })?.status == .cancelled { return finished() }
                        update(job.id) { $0.detail = t("جاري دمج الصورة والصوت…", "Putting picture and sound together…") }
                        let target = Downloader.unique(Downloader.folder.appendingPathComponent(Downloader.clean(title) + ".mp4"))
                        try await Downloader.merge(video: v, audio: a, to: target)
                        try? FileManager.default.removeItem(at: v); try? FileManager.default.removeItem(at: a)
                        complete(job, target)
                        return finished()
                    }
                    guard let combined else { throw Failure(t("لم يتم العثور على جودة يمكن تحميلها", "No downloadable quality was found")) }
                    source = combined.url
                    name = Downloader.clean(title) + "." + combined.fileExtension.rawValue
                } else {
                    guard let sound = streams.filterAudioOnly().filter({ $0.fileExtension == .m4a }).highestAudioBitrateStream()
                        ?? streams.filterAudioOnly().highestAudioBitrateStream() else {
                        throw Failure(t("لم يتم العثور على صوت يمكن تحميله", "No downloadable sound was found"))
                    }
                    source = sound.url
                    name = Downloader.clean(title) + "." + sound.fileExtension.rawValue
                }
            }
            var referer: URL?
            var candidates: [URL] = []
            var tried: [URL] = []
            var lookedInPage = false
            if !Downloader.isYouTube(job.url), Instagram.shortcode(job.url) != nil {
                update(job.id) { $0.detail = t("جاري قراءة منشور انستغرام…", "Reading the Instagram post…") }
                if let m = await Instagram.fetch(job.url) {
                    source = m.video
                    referer = URL(string: "https://www.instagram.com/")
                    lookedInPage = true // if this file fails, the page is searched the other ways
                    if let title = m.title { name = Downloader.clean(title); update(job.id) { $0.title = title } }
                }
            }
            var usedBrowser = false
            /// The next video link of the page; when the page's own links all failed, play the page in the hidden
            /// browser once (it gets the site's cookies) and try the links its player uses.
            func next() async -> Bool {
                if candidates.isEmpty, lookedInPage, !usedBrowser {
                    usedBrowser = true
                    update(job.id) { $0.detail = t("جاري فتح الصفحة وتشغيل الفيديو…", "Opening the page and playing the video…") }
                    let found = await PageVideo.fromBrowser(URL(string: job.url) ?? source)
                    candidates = found.videos.filter { !tried.contains($0) } + found.videos.filter { tried.contains($0) }.prefix(2)
                    if name == nil, let title = found.title { name = Downloader.clean(title); update(job.id) { $0.title = title } }
                }
                guard !candidates.isEmpty else { return false }
                source = candidates.removeFirst()
                tried.append(source)
                return true
            }
            while true {
                let got: (URL, URLResponse?)
                do { got = try await download(source, referer: referer) }
                catch {
                    // one of the page's video links didn't open: try the next one
                    if lookedInPage, !(error is CancellationError), await next() { continue }
                    throw error
                }
                let (temp, response) = got
                if jobs.first(where: { $0.id == job.id })?.status == .cancelled { try? FileManager.default.removeItem(at: temp); return finished() }
                if let http = response as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
                    try? FileManager.default.removeItem(at: temp)
                    if lookedInPage, await next() { continue }
                    throw Failure(t("الخادم رفض الطلب", "The server refused") + " (\(http.statusCode))")
                }
                let size = (try? FileManager.default.attributesOfItem(atPath: temp.path)[.size] as? Int64) ?? 0
                if size == 0 {
                    // the site sent nothing (it keeps the file for its own player): never save an empty file
                    try? FileManager.default.removeItem(at: temp)
                    if lookedInPage, await next() { continue }
                    throw Failure(lookedInPage
                        ? t("الموقع رفض يعطي الفيديو لغير مشغّله — جرّب نسخة الحاسبة أو الأندرويد", "The site only gives the video to its own player — try the Windows or Android app")
                        : t("الخادم أرسل ملف فارغ", "The server sent an empty file"))
                }
                let kind = Downloader.sniff(temp)
                let isPage = kind == "html" || (response?.mimeType ?? "").lowercased().contains("html")
                if isPage {
                    try? FileManager.default.removeItem(at: temp)
                    if lookedInPage {
                        if await next() { continue }
                        throw Failure(t("الرابط فتح صفحة مو فيديو", "The link opened a page, not a video"))
                    }
                    // The link is a web page (Instagram, TikTok, Facebook, a site…): find the video inside it.
                    lookedInPage = true
                    let page = response?.url ?? source
                    update(job.id) { $0.detail = t("جاري البحث عن الفيديو بالصفحة…", "Looking for the video in the page…") }
                    let html = String(decoding: (try? Data(contentsOf: temp)) ?? Data(), as: UTF8.self)
                    let found = PageVideo.fromHTML(html, base: page)
                    if let title = found.title {
                        update(job.id) { $0.title = title }
                        name = Downloader.clean(title)
                    }
                    candidates = found.videos
                    referer = page
                    let any = await next()
                    if jobs.first(where: { $0.id == job.id })?.status == .cancelled { return finished() }
                    guard any else {
                        throw Failure(t("هذا الرابط صفحة، وما لكيت بيها فيديو يتحمل (يمكن الفيديو يحتاج تسجيل دخول أو الموقع يحميه). جرّب رابط الفيديو نفسه أو نسخة الحاسبة.",
                                        "This link is a web page and no downloadable video was found in it (it may need a login or the site protects it). Try the video's own link, or the Windows app."))
                    }
                    continue
                }
                if lookedInPage, !(kind.map { Downloader.mediaKinds.contains($0) } ?? false) {
                    // From a page only a whole picture-and-sound file counts (not a preview, a piece of a stream, or a page)
                    try? FileManager.default.removeItem(at: temp)
                    if await next() { continue }
                    throw Failure(t("ما لكيت فيديو كامل يتحمل بهذا الرابط — الموقع يعطي الفيديو بقطع لمشغّله بس. جرّب نسخة الحاسبة أو الأندرويد.",
                                    "No whole video file was found at this link — the site only gives it in pieces to its own player. Try the Windows or Android app."))
                }
                var fileName = name ?? Downloader.clean(response?.suggestedFilename ?? source.lastPathComponent)
                let ext = (fileName as NSString).pathExtension.lowercased()
                if let kind, ext != kind, !(kind == "mp4" && ["m4v", "mov", "m4a", "3gp"].contains(ext)), !(kind == "jpg" && ext == "jpeg") {
                    // the name has no type, or a wrong one ("video", "file.php", "Unknown"): use what the file really is
                    if !ext.isEmpty, !Downloader.knownExt.contains(ext) || PageVideo.mediaExt.contains(kind) { fileName = (fileName as NSString).deletingPathExtension }
                    if (fileName as NSString).pathExtension.isEmpty { fileName += "." + kind }
                } else if ext.isEmpty, let fromLink = Downloader.extFrom(response?.mimeType) ?? (source.pathExtension.isEmpty ? nil : source.pathExtension.lowercased()) {
                    fileName += "." + fromLink
                } else if ext.isEmpty, lookedInPage {
                    fileName += ".mp4"
                }
                let target = Downloader.unique(Downloader.folder.appendingPathComponent(fileName))
                try FileManager.default.moveItem(at: temp, to: target)
                complete(job, target)
                break
            }
        } catch {
            if jobs.first(where: { $0.id == job.id })?.status != .cancelled {
                let message = (error as? Failure)?.message ?? (error as? URLError)?.localizedDescription ?? error.localizedDescription
                update(job.id) { $0.status = .failed; $0.error = message; $0.detail = "" }
            }
        }
        finished()
    }

    private func complete(_ job: Job, _ target: URL) {
        let size = (try? FileManager.default.attributesOfItem(atPath: target.path)[.size] as? Int64) ?? 0
        update(job.id) {
            $0.status = .done; $0.progress = 1; $0.file = target.lastPathComponent
            if $0.title.isEmpty { $0.title = (target.lastPathComponent as NSString).deletingPathExtension }
            $0.detail = target.lastPathComponent + " — " + ByteCountFormatter.string(fromByteCount: size, countStyle: .file)
        }
    }

    /// Picture + sound into one MP4, without re-encoding (fast, same quality).
    static func merge(video: URL, audio: URL, to out: URL) async throws {
        let v = video.deletingPathExtension().appendingPathExtension("mp4"), a = audio.deletingPathExtension().appendingPathExtension("m4a")
        try? FileManager.default.moveItem(at: video, to: v); try? FileManager.default.moveItem(at: audio, to: a)
        defer { try? FileManager.default.removeItem(at: v); try? FileManager.default.removeItem(at: a) }
        let comp = AVMutableComposition()
        let va = AVURLAsset(url: v), aa = AVURLAsset(url: a)
        guard let vt = try await va.loadTracks(withMediaType: .video).first,
              let at = try await aa.loadTracks(withMediaType: .audio).first,
              let cv = comp.addMutableTrack(withMediaType: .video, preferredTrackID: kCMPersistentTrackID_Invalid),
              let ca = comp.addMutableTrack(withMediaType: .audio, preferredTrackID: kCMPersistentTrackID_Invalid) else {
            throw Failure(t("تعذر قراءة الصورة أو الصوت", "Couldn't read the picture or the sound"))
        }
        let vd = try await va.load(.duration), ad = try await aa.load(.duration)
        try cv.insertTimeRange(CMTimeRange(start: .zero, duration: vd), of: vt, at: .zero)
        try ca.insertTimeRange(CMTimeRange(start: .zero, duration: CMTimeMinimum(vd, ad)), of: at, at: .zero)
        cv.preferredTransform = try await vt.load(.preferredTransform)
        guard let ex = AVAssetExportSession(asset: comp, presetName: AVAssetExportPresetPassthrough) else {
            throw Failure(t("تعذر دمج الصورة والصوت", "Couldn't put picture and sound together"))
        }
        ex.outputURL = out
        ex.outputFileType = .mp4
        await ex.export()
        if ex.status != .completed { throw Failure(ex.error?.localizedDescription ?? t("تعذر دمج الصورة والصوت", "Couldn't put picture and sound together")) }
    }

    private func finished() {
        running = nil
        task = nil
        pump()
    }

    /// One file with progress: a download task, moved out of the system's temporary place before it is deleted.
    static let userAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1"

    private func download(_ url: URL, referer: URL? = nil) async throws -> (URL, URLResponse?) {
        try await withCheckedThrowingContinuation { cont in
            var req = URLRequest(url: url)
            req.setValue(Downloader.userAgent, forHTTPHeaderField: "User-Agent")
            req.setValue("ar,en;q=0.8", forHTTPHeaderField: "Accept-Language")
            if let referer { req.setValue(referer.absoluteString, forHTTPHeaderField: "Referer") }
            finish = { location, response, error in
                if let location {
                    let keep = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
                    do { try FileManager.default.moveItem(at: location, to: keep); cont.resume(returning: (keep, response)) }
                    catch { cont.resume(throwing: error) }
                } else {
                    cont.resume(throwing: error ?? Failure(t("فشل التحميل", "Download failed")))
                }
            }
            task = session.downloadTask(with: req)
            task?.resume()
        }
    }

    // ---------- helpers ----------

    struct Failure: Error { let message: String; init(_ m: String) { message = m } }

    static func isYouTube(_ s: String) -> Bool {
        guard let host = URL(string: s)?.host?.lowercased() else { return false }
        return host == "youtu.be" || host.hasSuffix("youtube.com")
    }

    static func clean(_ s: String) -> String {
        let bad = CharacterSet(charactersIn: "/\\:*?\"<>|").union(.controlCharacters)
        let c = s.components(separatedBy: bad).joined(separator: " ").trimmingCharacters(in: .whitespaces)
        return c.isEmpty ? "download" : String(c.prefix(120))
    }

    static func unique(_ url: URL) -> URL {
        var u = url
        var n = 2
        let base = url.deletingPathExtension().lastPathComponent, ext = url.pathExtension
        while FileManager.default.fileExists(atPath: u.path) {
            u = url.deletingLastPathComponent().appendingPathComponent("\(base) (\(n))" + (ext.isEmpty ? "" : ".\(ext)"))
            n += 1
        }
        return u
    }

    /// Files with picture and/or sound (what a video link from a page must give).
    static let mediaKinds: Set<String> = ["mp4", "mov", "m4a", "mp3", "webm", "mkv", "3gp", "ogg", "wav"]

    static let knownExt: Set<String> = ["mp4", "m4v", "mov", "webm", "mkv", "m4a", "mp3", "aac", "wav", "ogg", "opus", "flac", "jpg", "jpeg", "png",
        "gif", "webp", "heic", "pdf", "zip", "rar", "7z", "exe", "msi", "apk", "ipa", "dmg", "txt", "json", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "3gp"]

    static func extFrom(_ mime: String?) -> String? {
        switch mime?.lowercased() ?? "" {
        case "video/mp4": return "mp4"
        case "video/quicktime": return "mov"
        case "video/webm": return "webm"
        case "audio/mp4", "audio/x-m4a": return "m4a"
        case "audio/mpeg": return "mp3"
        case "image/jpeg": return "jpg"
        case "image/png": return "png"
        case "image/gif": return "gif"
        case "image/webp": return "webp"
        case "application/pdf": return "pdf"
        case "application/zip": return "zip"
        case "application/vnd.android.package-archive": return "apk"
        default: return nil
        }
    }

    /// What a file really is, from its first bytes ("html" = a web page, not a file).
    static func sniff(_ url: URL) -> String? {
        guard let h = try? FileHandle(forReadingFrom: url), let d = try? h.read(upToCount: 512) else { return nil }
        let b = [UInt8](d)
        func at(_ i: Int, _ sig: [UInt8]) -> Bool { b.count >= i + sig.count && Array(b[i..<i + sig.count]) == sig }
        // a piece of a streamed video (not a file that plays on its own)
        if at(4, Array("styp".utf8)) || at(4, Array("moof".utf8)) || at(4, Array("sidx".utf8)) { return "part" }
        if at(4, Array("ftyp".utf8)) {
            let brand = b.count >= 12 ? String(decoding: b[8..<12], as: UTF8.self) : ""
            if brand.hasPrefix("M4A") || brand.hasPrefix("M4B") { return "m4a" }
            if brand.hasPrefix("qt") { return "mov" }
            if brand.hasPrefix("3gp") { return "3gp" }
            return "mp4"
        }
        if at(0, [0x1A, 0x45, 0xDF, 0xA3]) { return String(decoding: b, as: UTF8.self).contains("webm") ? "webm" : "mkv" }
        if at(0, [0x50, 0x4B, 3, 4]) { return "zip" }
        if at(0, Array("%PDF".utf8)) { return "pdf" }
        if at(0, Array("ID3".utf8)) || at(0, [0xFF, 0xFB]) || at(0, [0xFF, 0xF3]) || at(0, [0xFF, 0xF2]) { return "mp3" }
        if at(0, [0x89, 0x50, 0x4E, 0x47]) { return "png" }
        if at(0, [0xFF, 0xD8, 0xFF]) { return "jpg" }
        if at(0, Array("GIF8".utf8)) { return "gif" }
        if at(0, Array("RIFF".utf8)) && at(8, Array("WEBP".utf8)) { return "webp" }
        if at(0, Array("RIFF".utf8)) && at(8, Array("WAVE".utf8)) { return "wav" }
        if at(0, Array("OggS".utf8)) { return "ogg" }
        if at(0, Array("Rar!".utf8)) { return "rar" }
        if at(0, [0x4D, 0x5A]) { return "exe" }
        let text = String(decoding: b, as: UTF8.self).lowercased().trimmingCharacters(in: .whitespacesAndNewlines)
        if text.hasPrefix("<!doctype html") || text.hasPrefix("<html") || text.contains("<head") || (text.hasPrefix("<") && text.contains("<script")) { return "html" }
        return nil
    }
}

extension Downloader: URLSessionDownloadDelegate {
    nonisolated func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didWriteData bytesWritten: Int64, totalBytesWritten: Int64, totalBytesExpectedToWrite: Int64) {
        MainActor.assumeIsolated {
            guard let id = running else { return }
            let total = totalBytesExpectedToWrite
            if let i = jobs.firstIndex(where: { $0.id == id }) {
                jobs[i].progress = total > 0 ? Double(totalBytesWritten) / Double(total) : 0
                jobs[i].detail = ByteCountFormatter.string(fromByteCount: totalBytesWritten, countStyle: .file)
                    + (total > 0 ? " / " + ByteCountFormatter.string(fromByteCount: total, countStyle: .file) : "")
            }
        }
    }

    nonisolated func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {
        MainActor.assumeIsolated {
            finish?(location, downloadTask.response, nil)
            finish = nil
        }
    }

    nonisolated func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        MainActor.assumeIsolated {
            if let finish { finish(nil, task.response, error); self.finish = nil }
        }
    }
}

extension Downloader {
    /// For "play without downloading": the address the player opens, its title and the headers it needs.
    func playable(_ link: String, audio: Bool) async throws -> (URL, String, [String: String]) {
        guard let url = URL(string: link) else { throw Failure(t("الصق رابطاً صحيحاً", "Paste a valid link")) }
        let ua = ["User-Agent": Downloader.userAgent]
        if Downloader.isYouTube(link) {
            let yt = YouTube(url: url, methods: [.local, .remote])
            let streams = try await yt.streams
            let title = (try? await yt.metadata)?.title ?? yt.videoID
            let pick = audio
                ? streams.filterAudioOnly().filter { $0.fileExtension == .m4a }.highestAudioBitrateStream()
                : streams.filterVideoAndAudio().filter { $0.isNativelyPlayable }.highestResolutionStream()
            guard let s = pick ?? streams.filterVideoAndAudio().highestResolutionStream() else {
                throw Failure(t("يوتيوب ما أعطى أي جودة لهذا الفيديو — جرّب بعد قليل أو من شبكة ثانية", "YouTube gave no qualities for this video — try again later or on another network"))
            }
            return (s.url, title, [:])
        }
        if Instagram.shortcode(link) != nil, let m = await Instagram.fetch(link) {
            return (m.video, m.title ?? "Instagram", ua.merging(["Referer": "https://www.instagram.com/"]) { a, _ in a })
        }
        // A file plays as it is; a page: find the video in it (its text, or the hidden browser).
        var req = URLRequest(url: url)
        req.setValue(Downloader.userAgent, forHTTPHeaderField: "User-Agent")
        let (bytes, resp) = try await URLSession.shared.bytes(for: req)
        let mime = resp.mimeType?.lowercased() ?? ""
        if mime.contains("html") || mime.isEmpty {
            var data = Data()
            for try await b in bytes { data.append(b); if data.count > 3_000_000 { break } }
            let page = resp.url ?? url
            var found = PageVideo.fromHTML(String(decoding: data, as: UTF8.self), base: page)
            if found.videos.isEmpty { found = await PageVideo.fromBrowser(url) }
            guard let v = found.videos.first else {
                throw Failure(t("ما لكيت فيديو يشتغل بهذا الرابط", "No playable video was found at this link"))
            }
            return (v, found.title ?? page.host ?? "", ua.merging(["Referer": page.absoluteString]) { a, _ in a })
        }
        bytes.task.cancel()
        return (resp.url ?? url, url.deletingPathExtension().lastPathComponent, ua)
    }
}
