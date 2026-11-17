import SwiftUI

@main
struct IBRAApp: App {
    @StateObject private var downloader = Downloader.shared
    @State private var incoming: String?

    var body: some Scene {
        WindowGroup {
            ContentView(incoming: $incoming)
                .environmentObject(downloader)
                .environment(\.layoutDirection, t("ar", "en") == "ar" ? .rightToLeft : .leftToRight)
                .preferredColorScheme(.dark)
                // ibradm://download?url=… (from a Shortcut or another app)
                // Started with "-download <link> -mode video|audio" (used by the build's simulator check)
                .onAppear {
                    if let link = UserDefaults.standard.string(forKey: "download"), !link.isEmpty {
                        downloader.add(link, mode: UserDefaults.standard.string(forKey: "mode") ?? "video")
                    }
                }
                .onOpenURL { url in
                    if let q = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems?.first(where: { $0.name == "url" })?.value {
                        incoming = q
                    }
                }
        }
    }
}
