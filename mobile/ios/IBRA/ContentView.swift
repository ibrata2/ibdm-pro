import SwiftUI
import UIKit

private let red = Color(red: 0.84, green: 0.08, blue: 0.12)
private let card = Color(white: 0.10)
private let line = Color(white: 0.18)

struct ContentView: View {
    @EnvironmentObject var dl: Downloader
    @Binding var incoming: String?
    @State private var link = ""
    @State private var mode = "video"
    @State private var showAbout = false
    @State private var share: URL?
    @State private var toast: String?

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                header
                box
                HStack {
                    Text(t("التحميلات", "Downloads")).font(.headline)
                    Spacer()
                    if dl.jobs.contains(where: { $0.status != .waiting && $0.status != .running }) {
                        Button(t("حذف المكتملة", "Clear finished")) { dl.clearFinished() }.font(.footnote).tint(.gray)
                    }
                }.padding(.top, 6)
                if dl.jobs.isEmpty {
                    Text(t("لا توجد تحميلات بعد.", "No downloads yet.")).foregroundStyle(.secondary).font(.subheadline)
                }
                ForEach(dl.jobs) { j in row(j) }
            }
            .padding(16)
        }
        .background(Color(white: 0.055).ignoresSafeArea())
        .onAppear { pasteIfLink(always: false) }
        .onChange(of: incoming) { _, v in if let v { link = v; incoming = nil } }
        .sheet(isPresented: $showAbout) { about }
        .sheet(item: $share) { url in ShareSheet(items: [url]) }
        .overlay(alignment: .bottom) {
            if let toast { Text(toast).padding(10).background(.black.opacity(0.85)).clipShape(Capsule()).padding(.bottom, 30) }
        }
    }

    private var header: some View {
        HStack(spacing: 10) {
            Image("Logo").resizable().frame(width: 42, height: 42).clipShape(RoundedRectangle(cornerRadius: 10))
            VStack(alignment: .leading, spacing: 2) {
                Text("IBRA Download Manager").font(.title3.bold())
                Text(t("برمجة و تطوير ابراهيم الخزرجي", "Programmed and developed by Ibrahim Al-Khazraji")).font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            Button { showAbout = true } label: { Image(systemName: "info.circle").font(.title3) }.tint(.white)
        }
    }

    private var box: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(t("الصق الرابط", "Paste the link")).font(.subheadline).foregroundStyle(.secondary)
            HStack {
                TextField("https://…", text: $link)
                    .textInputAutocapitalization(.never).autocorrectionDisabled().keyboardType(.URL)
                    .environment(\.layoutDirection, .leftToRight)
                    .padding(10).background(Color(white: 0.055)).clipShape(RoundedRectangle(cornerRadius: 10))
                    .overlay(RoundedRectangle(cornerRadius: 10).stroke(line))
                    .onSubmit(download)
                Button(t("لصق", "Paste")) { pasteIfLink(always: true) }.buttonStyle(.bordered).tint(.white)
            }
            Picker("", selection: $mode) {
                Text(t("فيديو", "Video")).tag("video")
                Text(t("صوت فقط", "Audio only")).tag("audio")
            }.pickerStyle(.segmented)
            Button(action: download) {
                Label(t("تحميل", "Download"), systemImage: "arrow.down.circle.fill").font(.headline).frame(maxWidth: .infinity).padding(.vertical, 6)
            }.buttonStyle(.borderedProminent).tint(red)
            Text(t("على الآيفون: يوتيوب (فيديو مع الصوت حتى 720p أو الصوت فقط) وأي رابط ملف مباشر. الملفات في تطبيق «الملفات» ← على iPhone ← IBRA.",
                   "On iPhone: YouTube (video with sound up to 720p, or audio only) and any direct file link. Files are in the Files app → On My iPhone → IBRA."))
                .font(.caption).foregroundStyle(.secondary)
        }
        .padding(14).background(card).clipShape(RoundedRectangle(cornerRadius: 16))
        .overlay(RoundedRectangle(cornerRadius: 16).stroke(line))
    }

    @ViewBuilder private func row(_ j: Job) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(j.title.isEmpty ? j.url : j.title).font(.subheadline.bold()).lineLimit(2)
            Group {
                switch j.status {
                case .waiting: Text(t("بالانتظار", "Waiting")).foregroundStyle(.secondary)
                case .running: Text(j.detail.isEmpty ? t("جاري التحميل", "Downloading") : j.detail).foregroundStyle(red)
                case .done: Text(t("مكتمل", "Complete") + " — " + j.detail).foregroundStyle(.green)
                case .cancelled: Text(t("أُلغي", "Cancelled")).foregroundStyle(.secondary)
                case .failed: Text(t("فشل: ", "Failed: ") + (j.error ?? "")).foregroundStyle(red)
                }
            }.font(.caption)
            if j.status == .running { ProgressView(value: j.progress).tint(red) }
            HStack {
                switch j.status {
                case .done:
                    if let url = dl.path(j) {
                        Button(t("مشاركة / حفظ", "Share / Save")) { share = url }
                        if j.isVideo { Button(t("حفظ بالصور", "Save to Photos")) { dl.saveToPhotos(j) { ok in flash(ok ? t("تم الحفظ بالصور", "Saved to Photos") : t("لم يتم السماح بالحفظ", "Not allowed")) } } }
                    }
                    Button(t("إزالة", "Remove")) { dl.remove(j.id) }
                case .waiting, .running:
                    Button(t("إلغاء", "Cancel")) { dl.cancel(j.id) }
                default:
                    Button(t("إعادة المحاولة", "Retry")) { dl.retry(j.id) }
                    Button(t("إزالة", "Remove")) { dl.remove(j.id) }
                }
            }.buttonStyle(.bordered).tint(.white).font(.caption)
        }
        .padding(12).frame(maxWidth: .infinity, alignment: .leading)
        .background(card).clipShape(RoundedRectangle(cornerRadius: 14))
        .overlay(RoundedRectangle(cornerRadius: 14).stroke(line))
    }

    private var about: some View {
        let version = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? ""
        return VStack(spacing: 14) {
            Image("Logo").resizable().frame(width: 84, height: 84).clipShape(RoundedRectangle(cornerRadius: 18))
            Text("IBRA Download Manager \(version)").font(.title3.bold())
            Text(t("برمجة و تطوير ابراهيم الخزرجي", "Programmed and developed by Ibrahim Al-Khazraji"))
            Link(t("تيليجرام: t.me/ibraprogram", "Telegram: t.me/ibraprogram"), destination: URL(string: "https://t.me/ibraprogram")!)
            Link(t("تيك توك: ibraprogram", "TikTok: ibraprogram"), destination: URL(string: "https://www.tiktok.com/@ibraprogram")!)
            Text(t("الملفات تُحفظ في تطبيق «الملفات» ← على iPhone ← IBRA", "Files are saved in the Files app → On My iPhone → IBRA")).font(.caption).foregroundStyle(.secondary)
        }
        .padding(24).presentationDetents([.medium])
    }

    private func pasteIfLink(always: Bool) {
        let text = UIPasteboard.general.string ?? ""
        if let r = text.range(of: "https?://\\S+", options: .regularExpression) { link = String(text[r]) }
        else if always { flash(t("لا يوجد رابط بالحافظة", "No link on the clipboard")) }
    }

    private func download() {
        guard let r = link.range(of: "https?://\\S+", options: .regularExpression) else { flash(t("الصق رابطاً صحيحاً", "Paste a valid link")); return }
        dl.add(String(link[r]), mode: mode)
        link = ""
        flash(t("بدأ التحميل", "Download started"))
    }

    private func flash(_ s: String) {
        toast = s
        DispatchQueue.main.asyncAfter(deadline: .now() + 2) { if toast == s { toast = nil } }
    }
}

extension URL: @retroactive Identifiable { public var id: String { absoluteString } }

struct ShareSheet: UIViewControllerRepresentable {
    let items: [Any]
    func makeUIViewController(context: Context) -> UIActivityViewController { UIActivityViewController(activityItems: items, applicationActivities: nil) }
    func updateUIViewController(_ vc: UIActivityViewController, context: Context) {}
}
