import AVKit
import MediaPlayer
import SwiftUI
import UIKit

struct PlayItem: Identifiable { let id = UUID(); let title: String }

/// The app's player: downloaded files and links played without downloading. It keeps playing when you leave the
/// app or lock the phone (the sound goes on; a video can also float in a small window — picture in picture), with
/// play/pause on the lock screen.
@MainActor
final class Player: ObservableObject {
    static let shared = Player()
    @Published var item: PlayItem?
    let player = AVPlayer()
    weak var controller: AVPlayerViewController?
    var inPiP = false
    private var wasPlaying = false
    private var timeObserver: Any?
    private var title = ""

    private init() {
        let nc = NotificationCenter.default
        nc.addObserver(forName: UIApplication.willResignActiveNotification, object: nil, queue: .main) { _ in
            Task { @MainActor in Player.shared.wasPlaying = Player.shared.player.rate > 0 }
        }
        // Leaving the app: a video on screen would be paused by iOS, so the picture is let go and the sound goes on
        // (unless it is floating in its small window).
        nc.addObserver(forName: UIApplication.didEnterBackgroundNotification, object: nil, queue: .main) { _ in
            Task { @MainActor in
                try? await Task.sleep(nanoseconds: 400_000_000)
                let p = Player.shared
                guard !p.inPiP, p.player.currentItem != nil else { return }
                p.controller?.player = nil
                if p.wasPlaying { p.player.play() }
            }
        }
        nc.addObserver(forName: UIApplication.willEnterForegroundNotification, object: nil, queue: .main) { _ in
            Task { @MainActor in Player.shared.controller?.player = Player.shared.player }
        }
        let rc = MPRemoteCommandCenter.shared()
        rc.playCommand.addTarget { _ in Task { @MainActor in Player.shared.player.play(); Player.shared.updateInfo() }; return .success }
        rc.pauseCommand.addTarget { _ in Task { @MainActor in Player.shared.player.pause(); Player.shared.updateInfo() }; return .success }
        rc.togglePlayPauseCommand.addTarget { _ in
            Task { @MainActor in
                let p = Player.shared.player
                if p.rate > 0 { p.pause() } else { p.play() }
                Player.shared.updateInfo()
            }
            return .success
        }
        rc.changePlaybackPositionCommand.addTarget { e in
            guard let e = e as? MPChangePlaybackPositionCommandEvent else { return .commandFailed }
            let to = e.positionTime
            Task { @MainActor in _ = await Player.shared.player.seek(to: CMTime(seconds: to, preferredTimescale: 600)); Player.shared.updateInfo() }
            return .success
        }
    }

    func play(_ url: URL, title: String, headers: [String: String] = [:]) {
        let session = AVAudioSession.sharedInstance()
        try? session.setCategory(.playback, mode: .moviePlayback)
        try? session.setActive(true)
        let asset = headers.isEmpty ? AVURLAsset(url: url) : AVURLAsset(url: url, options: ["AVURLAssetHTTPHeaderFieldsKey": headers])
        player.replaceCurrentItem(with: AVPlayerItem(asset: asset))
        player.play()
        self.title = title
        if timeObserver == nil {
            timeObserver = player.addPeriodicTimeObserver(forInterval: CMTime(seconds: 2, preferredTimescale: 1), queue: .main) { _ in
                Task { @MainActor in Player.shared.updateInfo() }
            }
        }
        updateInfo()
        item = PlayItem(title: title)
    }

    func stop() {
        player.pause()
        player.replaceCurrentItem(with: nil)
        MPNowPlayingInfoCenter.default().nowPlayingInfo = nil
        item = nil
    }

    /// What the lock screen and control center show.
    func updateInfo() {
        guard let current = player.currentItem else { return }
        var info: [String: Any] = [
            MPMediaItemPropertyTitle: title,
            MPMediaItemPropertyArtist: "IBRA DM",
            MPNowPlayingInfoPropertyElapsedPlaybackTime: current.currentTime().seconds.isFinite ? current.currentTime().seconds : 0,
            MPNowPlayingInfoPropertyPlaybackRate: player.rate,
        ]
        if current.duration.seconds.isFinite { info[MPMediaItemPropertyPlaybackDuration] = current.duration.seconds }
        MPNowPlayingInfoCenter.default().nowPlayingInfo = info
    }
}

/// The system player view (play/pause, seek, full screen, AirPlay, picture in picture).
struct PlayerView: UIViewControllerRepresentable {
    func makeCoordinator() -> Coordinator { Coordinator() }

    func makeUIViewController(context: Context) -> AVPlayerViewController {
        let vc = AVPlayerViewController()
        vc.player = Player.shared.player
        vc.allowsPictureInPicturePlayback = true
        vc.canStartPictureInPictureAutomaticallyFromInline = true
        vc.updatesNowPlayingInfoCenter = false
        vc.delegate = context.coordinator
        Player.shared.controller = vc
        return vc
    }

    func updateUIViewController(_ vc: AVPlayerViewController, context: Context) {}

    final class Coordinator: NSObject, AVPlayerViewControllerDelegate {
        func playerViewControllerWillStartPictureInPicture(_ c: AVPlayerViewController) { Task { @MainActor in Player.shared.inPiP = true } }
        func playerViewControllerDidStopPictureInPicture(_ c: AVPlayerViewController) { Task { @MainActor in Player.shared.inPiP = false } }
        func playerViewControllerShouldAutomaticallyDismissAtPictureInPictureStart(_ c: AVPlayerViewController) -> Bool { false }
        func playerViewController(_ c: AVPlayerViewController, restoreUserInterfaceForPictureInPictureStopWithCompletionHandler done: @escaping (Bool) -> Void) { done(true) }
    }
}

/// The full-screen player page.
struct PlayerScreen: View {
    @ObservedObject var player = Player.shared
    var body: some View {
        ZStack(alignment: .top) {
            Color.black.ignoresSafeArea()
            PlayerView().ignoresSafeArea(edges: .bottom).padding(.top, 54)
            HStack {
                Text(player.item?.title ?? "").font(.subheadline.bold()).lineLimit(1)
                Spacer()
                Button { player.stop() } label: { Image(systemName: "xmark.circle.fill").font(.title2) }.tint(.white)
            }
            .padding(.horizontal, 16).padding(.top, 10)
        }
    }
}
