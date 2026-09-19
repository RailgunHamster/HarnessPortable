import SwiftUI

/// Design tokens copied from the DeepSeek Harness client theme
/// (`dsh-client-ui-theme`).
///
/// Every colour is resolved from an explicit `ColorScheme`, so the views need
/// no AppKit dynamic colours, follow the system appearance automatically, and
/// cannot fail at runtime.
enum Dsh {

    // MARK: - Shapes

    /// Corner radius for cards, sheets and other surfaces.
    static let radiusCard: CGFloat = 12
    /// Corner radius for controls (buttons, fields, chips).
    static let radiusControl: CGFloat = 8
    /// Hairline border width used instead of shadows.
    static let borderWidth: CGFloat = 1

    // MARK: - Typography

    /// Monospaced system font for technical values (host, port, URL, version).
    static func mono(_ size: CGFloat, weight: Font.Weight = .regular) -> Font {
        .system(size: size, weight: weight, design: .monospaced)
    }

    // MARK: - Backgrounds

    static func bgBase(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF151517) : hex(0xFFFFFFFF)
    }

    static func bgLayer1(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF232324) : hex(0xFFFFFFFF)
    }

    static func bgLayer2(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF2C2C2E) : hex(0xFFF5F6F7)
    }

    static func bgLayer3(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF353638) : hex(0xFFEBEEF2)
    }

    // MARK: - Borders

    static func borderL1(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0x0FFFFFFF) : hex(0x0A000000)
    }

    static func borderL2(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0x1FFFFFFF) : hex(0x1A000000)
    }

    static func borderL3(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0x29FFFFFF) : hex(0x1F000000)
    }

    // MARK: - Text

    static func textPrimary(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFFF9FAFB) : hex(0xFF0F1115)
    }

    static func textSecondary(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFFCFD3D6) : hex(0xFF61666B)
    }

    static func textTertiary(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFFADB2B8) : hex(0xFF81858C)
    }

    // MARK: - Brand

    static func brandFill(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFFF9FAFB) : hex(0xFF0F1115)
    }

    static func brandFillHover(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFFE1E5EE) : hex(0xFF2C2C2E)
    }

    static func brandFillPressed(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFFCFD3D6) : hex(0xFF43454A)
    }

    static func brandText(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF0F1115) : hex(0xFFFFFFFF)
    }

    // MARK: - Accents

    static func accent(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF5686FE) : hex(0xFF4176E6)
    }

    static func success(_ scheme: ColorScheme) -> Color {
        hex(0xFF22C55E)
    }

    static func danger(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFFF25A5A) : hex(0xFFEC1313)
    }

    static func warning(_ scheme: ColorScheme) -> Color {
        hex(0xFFF7AD31)
    }

    // MARK: - Inputs

    static func inputBackground(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xFF232324) : hex(0xFFFFFFFF)
    }

    static func inputBorder(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0x1FFFFFFF) : hex(0x1A000000)
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

/// Layer-1 surface with a 1pt border and a 12pt radius (no shadow).
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

/// Input background + 1pt input border + 8pt radius.
struct DshInputModifier: ViewModifier {
    @Environment(\.colorScheme) var colorScheme

    func body(content: Content) -> some View {
        content
            .textFieldStyle(.plain)
            .padding(.horizontal, 8)
            .padding(.vertical, 6)
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
            .padding(.vertical, 6)
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

// MARK: - View helpers

extension View {
    /// Layer-1 card surface: 1pt l1 border, 12pt radius, no shadow.
    func dshCard(radius: CGFloat = Dsh.radiusCard) -> some View {
        modifier(DshCardModifier(radius: radius))
    }

    /// Token-styled text field surface.
    func dshInput() -> some View {
        modifier(DshInputModifier())
    }
}
