import SwiftUI

/// Shared shell tokens. System appearance uses flat native surfaces and crisp borders.
enum Dsh {

    // MARK: - Shapes

    /// Corner radius for cards, sheets and other surfaces.
    static let radiusCard: CGFloat = 16
    /// Corner radius for controls (buttons, fields, chips).
    static let radiusControl: CGFloat = 10
    /// Hairline border width used instead of shadows.
    static let borderWidth: CGFloat = 1

    // MARK: - Typography

    /// Monospaced system font for technical values (host, port, URL, version).
    static func mono(_ size: CGFloat, weight: Font.Weight = .regular) -> Font {
        .system(size: size, weight: weight, design: .monospaced)
    }

    // MARK: - Backgrounds

    static func bgBase(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF101722) : hex(0xFFF3F5F9)
    }

    static func bgLayer1(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF182231) : hex(0xFFFFFFFF)
    }

    static func bgLayer2(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF202D40) : hex(0xFFEDF1F7)
    }

    static func bgLayer3(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF2B3A50) : hex(0xFFE3E9F2)
    }

    // MARK: - Borders

    static func borderL1(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF2B3A50) : hex(0xFFDFE5EF)
    }

    static func borderL2(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF35455D) : hex(0xFFCFD8E6)
    }

    static func borderL3(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF536783) : hex(0xFFB8C6DA)
    }

    // MARK: - Text

    static func textPrimary(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFFEDF2FB) : hex(0xFF172239)
    }

    static func textSecondary(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFFA7B5CC) : hex(0xFF526078)
    }

    static func textTertiary(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF92A1BA) : hex(0xFF65718A)
    }

    // MARK: - Brand

    static func brandFill(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF8EAFFF) : hex(0xFF365EDC)
    }

    static func brandFillHover(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFFABC3FF) : hex(0xFF2B50C7)
    }

    static func brandFillPressed(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF759AEE) : hex(0xFF2343AA)
    }

    static func brandText(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF101722) : hex(0xFFFFFFFF)
    }

    // MARK: - Accents

    static func accent(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF8EAFFF) : hex(0xFF365EDC)
    }

    static func accentSoft(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF263858) : hex(0xFFE9EEFF)
    }

    static func success(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF4BD2A0) : hex(0xFF15805F)
    }

    static func danger(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFFFF8C9E) : hex(0xFFC9364B)
    }

    static func warning(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFFF2C66D) : hex(0xFF97680A)
    }

    // MARK: - Inputs

    static func inputBackground(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF182231) : hex(0xFFFFFFFF)
    }

    static func inputBorder(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF35455D) : hex(0xFFCFD8E6)
    }

    // MARK: - Convenience

    /// Resolved colour set for one appearance.
    static func palette(_ scheme: ColorScheme) -> DshPalette {
        DshPalette(scheme)
    }

    /// `#AARRGGBB` literal to `Color` (the tokens are written as ARGB hex).
    private static func hex(_ value: UInt32) -> Color {
        let alpha = Double((value >> 24) & 0xFF) / 255
        let red = Double((value >> 16) & 0xFF) / 255
        let green = Double((value >> 8) & 0xFF) / 255
        let blue = Double(value & 0xFF) / 255
        return Color(.sRGB, red: red, green: green, blue: blue, opacity: alpha)
    }
}

/// Colour tokens resolved for a concrete `ColorScheme`.
///
/// Usage in a view:
/// ```swift
/// @Environment(\.colorScheme) var colorScheme
/// private var dsh: DshPalette { Dsh.palette(colorScheme) }
/// ```
struct DshPalette {
    let scheme: ColorScheme

    init(_ scheme: ColorScheme) {
        self.scheme = scheme
    }

    var bgBase: Color { Dsh.bgBase(scheme) }
    var bgLayer1: Color { Dsh.bgLayer1(scheme) }
    var bgLayer2: Color { Dsh.bgLayer2(scheme) }
    var bgLayer3: Color { Dsh.bgLayer3(scheme) }

    var borderL1: Color { Dsh.borderL1(scheme) }
    var borderL2: Color { Dsh.borderL2(scheme) }
    var borderL3: Color { Dsh.borderL3(scheme) }

    var textPrimary: Color { Dsh.textPrimary(scheme) }
    var textSecondary: Color { Dsh.textSecondary(scheme) }
    var textTertiary: Color { Dsh.textTertiary(scheme) }

    var brandFill: Color { Dsh.brandFill(scheme) }
    var brandFillHover: Color { Dsh.brandFillHover(scheme) }
    var brandFillPressed: Color { Dsh.brandFillPressed(scheme) }
    var brandText: Color { Dsh.brandText(scheme) }

    var accent: Color { Dsh.accent(scheme) }
    var accentSoft: Color { Dsh.accentSoft(scheme) }
    var success: Color { Dsh.success(scheme) }
    var danger: Color { Dsh.danger(scheme) }
    var warning: Color { Dsh.warning(scheme) }

    var inputBackground: Color { Dsh.inputBackground(scheme) }
    var inputBorder: Color { Dsh.inputBorder(scheme) }

    var radiusCard: CGFloat { Dsh.radiusCard }
    var radiusControl: CGFloat { Dsh.radiusControl }
    var borderWidth: CGFloat { Dsh.borderWidth }
}

// MARK: - Card surface

/// Layer-1 surface with a 1pt border and a 16pt radius (no shadow).
struct DshCardModifier: ViewModifier {
    var radius: CGFloat = Dsh.radiusCard
    @Environment(\.colorScheme) var colorScheme

    func body(content: Content) -> some View {
        content
            .background(Dsh.bgLayer1(colorScheme), in: RoundedRectangle(cornerRadius: radius))
            .overlay(
                RoundedRectangle(cornerRadius: radius)
                    .stroke(Dsh.borderL1(colorScheme), lineWidth: Dsh.borderWidth)
            )
    }
}

// MARK: - Input surface

/// Input background + 1pt input border + 10pt radius.
struct DshInputModifier: ViewModifier {
    @Environment(\.colorScheme) var colorScheme

    func body(content: Content) -> some View {
        content
            .textFieldStyle(.plain)
            .padding(.horizontal, 10)
            .padding(.vertical, 8)
            .background(
                Dsh.inputBackground(colorScheme),
                in: RoundedRectangle(cornerRadius: Dsh.radiusControl)
            )
            .overlay(
                RoundedRectangle(cornerRadius: Dsh.radiusControl)
                    .stroke(Dsh.inputBorder(colorScheme), lineWidth: Dsh.borderWidth)
            )
    }
}

// MARK: - Primary button

/// Brand-fill primary button with brand text and hover/pressed states.
struct DshPrimaryButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        DshPrimaryButtonBody(isPressed: configuration.isPressed, label: configuration.label)
    }
}

private struct DshPrimaryButtonBody<Label: View>: View {
    let isPressed: Bool
    let label: Label
    @Environment(\.colorScheme) var colorScheme
    @Environment(\.isEnabled) var isEnabled
    @State var isHovering = false

    private var fill: Color {
        guard isEnabled else { return Dsh.bgLayer3(colorScheme) }
        if isPressed { return Dsh.brandFillPressed(colorScheme) }
        if isHovering { return Dsh.brandFillHover(colorScheme) }
        return Dsh.brandFill(colorScheme)
    }

    private var textColor: Color {
        isEnabled ? Dsh.brandText(colorScheme) : Dsh.textTertiary(colorScheme)
    }

    var body: some View {
        label
            .font(.system(size: 13, weight: .medium))
            .foregroundStyle(textColor)
            .padding(.horizontal, 12)
            .padding(.vertical, 8)
            .background(fill, in: RoundedRectangle(cornerRadius: Dsh.radiusControl))
            .contentShape(RoundedRectangle(cornerRadius: Dsh.radiusControl))
            .onHover { isHovering = isEnabled && $0 }
    }
}

// MARK: - Status dot

/// Small colour dot shown next to a status label.
struct DshStatusDot: View {
    let color: Color
    var size: CGFloat = 7

    var body: some View {
        Circle()
            .fill(color)
            .frame(width: size, height: size)
    }
}

struct DshSecondaryButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        DshSecondaryButtonBody(label: configuration.label, isPressed: configuration.isPressed)
    }
}

private struct DshSecondaryButtonBody<Label: View>: View {
    let label: Label
    let isPressed: Bool
    @Environment(\.colorScheme) var colorScheme
    @Environment(\.isEnabled) var isEnabled
    @State private var hovering = false

    var body: some View {
        label
            .font(.system(size: 13, weight: .medium))
            .foregroundStyle(isEnabled ? Dsh.textPrimary(colorScheme) : Dsh.textTertiary(colorScheme))
            .padding(.horizontal, 10)
            .padding(.vertical, 8)
            .background(isPressed || hovering ? Dsh.bgLayer3(colorScheme) : Dsh.bgLayer2(colorScheme),
                        in: RoundedRectangle(cornerRadius: Dsh.radiusControl))
            .contentShape(RoundedRectangle(cornerRadius: Dsh.radiusControl))
            .opacity(isEnabled ? 1 : 0.5)
            .onHover { hovering = isEnabled && $0 }
    }
}

struct DshEmptyState: View {
    let title: String
    let message: String
    let systemImage: String
    @Environment(\.colorScheme) var colorScheme

    var body: some View {
        VStack(spacing: 14) {
            Image(systemName: systemImage)
                .font(.system(size: 24, weight: .medium))
                .foregroundStyle(Dsh.accent(colorScheme))
                .frame(width: 56, height: 56)
                .background(Dsh.accentSoft(colorScheme), in: RoundedRectangle(cornerRadius: 16))
            Text(title).font(.system(size: 18, weight: .semibold)).foregroundStyle(Dsh.textPrimary(colorScheme))
            Text(message).font(.callout).foregroundStyle(Dsh.textSecondary(colorScheme))
                .multilineTextAlignment(.center)
        }
        .padding(32)
        .frame(maxWidth: .infinity)
        .dshCard()
    }
}

// MARK: - View helpers

extension View {
    /// Layer-1 card surface: 1pt l1 border, 16pt radius, no shadow.
    func dshCard(radius: CGFloat = Dsh.radiusCard) -> some View {
        modifier(DshCardModifier(radius: radius))
    }

    /// Token-styled text field surface.
    func dshInput() -> some View {
        modifier(DshInputModifier())
    }
}
