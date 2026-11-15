import SwiftUI
import AppKit

@MainActor
struct MacUpdateInstallActions: View {
    @ObservedObject var updater: UpdateService
    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                if updater.canInstallAutomatically {
                    Button("安装并重新打开") { updater.installAndRelaunch() }
                        .buttonStyle(DshPrimaryButtonStyle())
                        .accessibilityIdentifier("Updates.InstallAndRelaunch")
                }
                Button("打开安装镜像") { updater.openInstaller() }
                    .accessibilityIdentifier("Updates.OpenInstaller")
            }
            if let issue = updater.installIssue {
                Text(issue).font(.callout).foregroundStyle(Dsh.danger(colorScheme))
            }
        }
    }
}

enum MacUpdateStage: String, CaseIterable {
    case preparing = "准备更新"
    case installing = "安装新版本"
    case reopening = "重新打开"
    case failed = "更新中断"

    static let steps: [Self] = [.preparing, .installing, .reopening]

    var progress: Double { Double((Self.steps.firstIndex(of: self) ?? 0) + 1) / 3 }
}

@MainActor
final class MacUpdatePresentation: ObservableObject {
    @Published var stage: MacUpdateStage = .preparing
    @Published var failure: String?
}

struct MacUpdateSplashView: View {
    @ObservedObject var presentation: MacUpdatePresentation
    let from: String
    let to: String
    let icon: NSImage
    @Environment(\.colorScheme) private var colorScheme
    private var palette: DshPalette { Dsh.palette(colorScheme) }

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(spacing: 12) {
                Image(nsImage: icon).resizable().scaledToFit().frame(width: 42, height: 42)
                    .accessibilityHidden(true)
                VStack(alignment: .leading, spacing: 4) {
                    Text("Harness Portable").font(.system(size: 18, weight: .semibold))
                    Text(presentation.stage.rawValue).font(.callout).foregroundStyle(palette.textSecondary)
                }
                Spacer(minLength: 12)
                Text("\(from) → \(to)").font(Dsh.mono(11, weight: .medium))
                    .foregroundStyle(palette.accent)
                    .padding(.horizontal, 9).padding(.vertical, 5)
                    .background(palette.accentSoft, in: Capsule())
            }
            if let failure = presentation.failure {
                Text(failure).font(.callout).foregroundStyle(palette.danger)
                    .lineLimit(3).textSelection(.disabled)
            } else {
                GeometryReader { metrics in
                    ZStack(alignment: .leading) {
                        Capsule().fill(palette.bgLayer3)
                        Capsule().fill(palette.accent)
                            .frame(width: metrics.size.width * presentation.stage.progress)
                    }
                }
                    .frame(height: 6)
                    .accessibilityElement(children: .ignore)
                    .accessibilityLabel(presentation.stage.rawValue)
                    .accessibilityValue("\(Int(presentation.stage.progress * 100))%")
                HStack(spacing: 8) {
                    ForEach(MacUpdateStage.steps, id: \.self) { step in
                        let current = step == presentation.stage
                        Text(step.rawValue).font(.system(size: 11, weight: current ? .semibold : .regular))
                            .foregroundStyle(current ? palette.accent : palette.textSecondary)
                            .frame(maxWidth: .infinity).padding(.vertical, 7)
                            .background(current ? palette.accentSoft : palette.bgLayer2, in: RoundedRectangle(cornerRadius: 7))
                    }
                }
            }
        }
        .foregroundStyle(palette.textPrimary)
        .padding(22)
        .frame(width: 470, height: 184)
        .background(palette.bgLayer1, in: RoundedRectangle(cornerRadius: Dsh.radiusCard))
        .overlay { RoundedRectangle(cornerRadius: Dsh.radiusCard).strokeBorder(palette.borderL1) }
    }
}
