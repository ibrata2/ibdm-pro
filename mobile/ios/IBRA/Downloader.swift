import Foundation
import Photos
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
}

/// Downloads one link at a time into the app's folder (visible in the Files app). On the iPhone the download tools
/// of the Windows and Android apps can't run, so: any direct file link, and YouTube (videos with sound up to 720p,
/// or the audio).
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
        Task { await run(next) }
    }

    private func run(_ job: Job) async {
        do {
            var source = URL(string: job.url)!
            var name: String?
            if Downloader.isYouTube(job.url) {
                let yt = YouTube(url: source, methods: [.local, .remote])
                let streams = try await yt.streams
                let pick: YouTubeKit.Stream?
                if job.mode == "audio" {
                    pick = streams.filterAudioOnly().filter { $0.fileExtension == .m4a }.highestAudioBitrateStream()
                        ?? streams.filterAudioOnly().highestAudioBitrateStream()
                } else {
                    pick = streams.filterVideoAndAudio().filter { $0.isNativelyPlayable }.highestResolutionStream()
                        ?? streams.filterVideoAndAudio().highestResolutionStream()
                }
                guard let stream = pick else { throw Failure(t("لم يتم العثور على جودة يمكن تحميلها", "No downloadable quality was found")) }
                source = stream.url
                let title = (try? await yt.metadata)?.title ?? yt.videoID
                name = Downloader.clean(title) + "." + stream.fileExtension.rawValue
                update(job.id) { $0.title = title }
            }
            let (temp, response) = try await download(source)
            if jobs.first(where: { $0.id == job.id })?.status == .cancelled { return finished() }
            if let http = response as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
                throw Failure(t("الخادم رفض الطلب", "The server refused") + " (\(http.statusCode))")
            }
            var fileName = name ?? Downloader.clean(response?.suggestedFilename ?? source.lastPathComponent)
            if (fileName as NSString).pathExtension.isEmpty, let ext = Downloader.sniff(temp) { fileName += "." + ext }
            let target = Downloader.unique(Downloader.folder.appendingPathComponent(fileName))
            try FileManager.default.moveItem(at: temp, to: target)
            let size = (try? FileManager.default.attributesOfItem(atPath: target.path)[.size] as? Int64) ?? 0
            update(job.id) {
                $0.status = .done; $0.progress = 1; $0.file = target.lastPathComponent
                if $0.title.isEmpty { $0.title = (target.lastPathComponent as NSString).deletingPathExtension }
                $0.detail = target.lastPathComponent + " — " + ByteCountFormatter.string(fromByteCount: size, countStyle: .file)
            }
        } catch {
            if jobs.first(where: { $0.id == job.id })?.status != .cancelled {
                let message = (error as? Failure)?.message ?? (error as? URLError)?.localizedDescription ?? error.localizedDescription
                update(job.id) { $0.status = .failed; $0.error = message; $0.detail = "" }
            }
        }
        finished()
    }

    private func finished() {
        running = nil
        task = nil
        pump()
    }

    /// One file with progress: a download task, moved out of the system's temporary place before it is deleted.
    private func download(_ url: URL) async throws -> (URL, URLResponse?) {
        try await withCheckedThrowingContinuation { cont in
            var req = URLRequest(url: url)
            req.setValue("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1", forHTTPHeaderField: "User-Agent")
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

    /// What a file really is, from its first bytes.
    static func sniff(_ url: URL) -> String? {
        guard let h = try? FileHandle(forReadingFrom: url), let d = try? h.read(upToCount: 12) else { return nil }
        let b = [UInt8](d)
        func at(_ i: Int, _ sig: [UInt8]) -> Bool { b.count >= i + sig.count && Array(b[i..<i + sig.count]) == sig }
        if at(4, Array("ftyp".utf8)) { return "mp4" }
        if at(0, [0x50, 0x4B, 3, 4]) { return "zip" }
        if at(0, Array("%PDF".utf8)) { return "pdf" }
        if at(0, Array("ID3".utf8)) || at(0, [0xFF, 0xFB]) { return "mp3" }
        if at(0, [0x89, 0x50, 0x4E, 0x47]) { return "png" }
        if at(0, [0xFF, 0xD8, 0xFF]) { return "jpg" }
        if at(0, [0x4D, 0x5A]) { return "exe" }
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
