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
                .onOpenURL { url in
                    if let q = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems?.first(where: { $0.name == "url" })?.value {
                        incoming = q
                    }
                }
        }
    }
}
