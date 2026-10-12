package com.harness.portable

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.Shapes
import androidx.compose.ui.draw.clip
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.ProvidableCompositionLocal
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/** Shared native shell palette, resolved once for each system appearance. */
internal data class DshColors(
    // Layered backgrounds. `base` is the page, `layer1` the card, `layer2` a
    // nested surface / input, `layer3` the deepest step.
    val base: Color,
    val layer1: Color,
    val layer2: Color,
    val layer3: Color,

    // Borders, in the design system's own ink-over-surface form: the colour is
    // translucent and the matching `*On` colour is what it was authored
    // against, so the *composited* border can be recovered for any surface.
    val borderL1: Color,
    val borderL1On: Color,
    val borderL2: Color,
    val borderL2On: Color,
    val borderL3: Color,
    val borderL3On: Color,

    // Text ranks.
    val textPrimary: Color,
    val textSecondary: Color,
    val textTertiary: Color,

    // Brand fill: light background with dark ink, and the inverse in dark mode.
    val brandFill: Color,
    val brandHover: Color,
    val brandPressed: Color,
    val brandText: Color,

    // Semantic.
    val accent: Color,
    val accentSoft: Color,
    val success: Color,
    val danger: Color,
    val warning: Color,

    // Fields.
    val inputBg: Color,
    val inputBorder: Color,

    /** Full-screen wash behind modal surfaces. */
    val scrim: Color,
    val isDark: Boolean
) {

    /** Opaque border ink as it appears when laid over [over]. */
    fun borderOver(border: Color, over: Color): Color =
        border.over(over)

    /** Border that has first been allowed to fall on a page background. */
    fun borderOnBase(border: Color, on: Color): Color =
        borderOver(border, borderOver(on, base))
}

/** Translucent [ink] composited over opaque [base], preserving its alpha. */
private fun Color.over(base: Color, alphaScale: Float = alpha): Color {
    val a = alphaScale.coerceIn(0f, 1f)
    if (a <= 0f) return base
    return Color(
        red = red * a + base.red * (1f - a),
        green = green * a + base.green * (1f - a),
        blue = blue * a + base.blue * (1f - a),
        alpha = 1f
    )
}

private val LightTokens = DshColors(
    base = Color(0xFFF3F5F9),
    layer1 = Color(0xFFFFFFFF),
    layer2 = Color(0xFFEDF1F7),
    layer3 = Color(0xFFE3E9F2),
    borderL1 = Color(0xFFDFE5EF),
    borderL1On = Color(0xFFFFFFFF),
    borderL2 = Color(0xFFCFD8E6),
    borderL2On = Color(0xFFFFFFFF),
    borderL3 = Color(0xFFB8C6DA),
    borderL3On = Color(0xFFFFFFFF),
    textPrimary = Color(0xFF172239),
    textSecondary = Color(0xFF526078),
    textTertiary = Color(0xFF65718A),
    brandFill = Color(0xFF365EDC),
    brandHover = Color(0xFF2B50C7),
    brandPressed = Color(0xFF2343AA),
    brandText = Color(0xFFFFFFFF),
    accentSoft = Color(0xFFE9EEFF),
    accent = Color(0xFF365EDC),
    success = Color(0xFF15805F),
    danger = Color(0xFFC9364B),
    warning = Color(0xFF97680A),
    inputBg = Color(0xFFFFFFFF),
    inputBorder = Color(0xFFCFD8E6),
    scrim = Color(0x99000000),
    isDark = false
)

private val DarkTokens = DshColors(
    base = Color(0xFF101722),
    layer1 = Color(0xFF182231),
    layer2 = Color(0xFF202D40),
    layer3 = Color(0xFF2B3A50),
    borderL1 = Color(0xFF2B3A50),
    borderL1On = Color(0xFF000000),
    borderL2 = Color(0xFF35455D),
    borderL2On = Color(0xFF000000),
    borderL3 = Color(0xFF536783),
    borderL3On = Color(0xFF000000),
    textPrimary = Color(0xFFEDF2FB),
    textSecondary = Color(0xFFA7B5CC),
    textTertiary = Color(0xFF92A1BA),
    brandFill = Color(0xFF8EAFFF),
    brandHover = Color(0xFFABC3FF),
    brandPressed = Color(0xFF759AEE),
    brandText = Color(0xFF101722),
    accentSoft = Color(0xFF263858),
    accent = Color(0xFF8EAFFF),
    success = Color(0xFF4BD2A0),
    danger = Color(0xFFFF8C9E),
    warning = Color(0xFFF2C66D),
    inputBg = Color(0xFF182231),
    inputBorder = Color(0xFF35455D),
    scrim = Color(0xCC000000),
    isDark = true
)

/** Material roles use the same surfaces and accent as the desktop shells. */
private fun DshColors.toColorScheme() = if (isDark) {
    darkColorScheme(
        primary = brandFill,
        onPrimary = brandText,
        primaryContainer = accentSoft,
        onPrimaryContainer = accent,
        inversePrimary = accent,
        secondary = textSecondary,
        onSecondary = base,
        secondaryContainer = layer3,
        onSecondaryContainer = textPrimary,
        tertiary = accent,
        onTertiary = brandText,
        tertiaryContainer = layer3,
        onTertiaryContainer = textPrimary,
        background = base,
        onBackground = textPrimary,
        surface = layer1,
        onSurface = textPrimary,
        surfaceVariant = layer2,
        onSurfaceVariant = textSecondary,
        surfaceTint = Color.Transparent,
        surfaceBright = layer3,
        surfaceDim = base,
        surfaceContainerLowest = base,
        surfaceContainerLow = layer1,
        surfaceContainer = layer1,
        surfaceContainerHigh = layer2,
        surfaceContainerHighest = layer3,
        inverseSurface = textPrimary,
        inverseOnSurface = base,
        error = danger,
        onError = Color(0xFFFFFFFF),
        errorContainer = layer3,
        onErrorContainer = danger,
        outline = inputBorder,
        outlineVariant = borderL1,
        scrim = Color(0xFF000000)
    )
} else {
    lightColorScheme(
        primary = brandFill,
        onPrimary = brandText,
        primaryContainer = accentSoft,
        onPrimaryContainer = accent,
        inversePrimary = accent,
        secondary = textSecondary,
        onSecondary = base,
        secondaryContainer = layer3,
        onSecondaryContainer = textPrimary,
        tertiary = accent,
        onTertiary = brandText,
        tertiaryContainer = layer3,
        onTertiaryContainer = textPrimary,
        background = base,
        onBackground = textPrimary,
        surface = layer1,
        onSurface = textPrimary,
        surfaceVariant = layer2,
        onSurfaceVariant = textSecondary,
        surfaceTint = Color.Transparent,
        surfaceBright = base,
        surfaceDim = layer3,
        surfaceContainerLowest = base,
        surfaceContainerLow = layer1,
        surfaceContainer = layer1,
        surfaceContainerHigh = layer2,
        surfaceContainerHighest = layer3,
        inverseSurface = textPrimary,
        inverseOnSurface = base,
        error = danger,
        onError = Color(0xFFFFFFFF),
        errorContainer = layer3,
        onErrorContainer = danger,
        outline = inputBorder,
        outlineVariant = borderL1,
        scrim = Color(0xFF000000)
    )
}

private val LightScheme = LightTokens.toColorScheme()
private val DarkScheme = DarkTokens.toColorScheme()
private val ShellTypography = Typography(
    headlineSmall = TextStyle(fontSize = 24.sp, lineHeight = 32.sp, fontWeight = androidx.compose.ui.text.font.FontWeight.SemiBold),
    titleLarge = TextStyle(fontSize = 22.sp, lineHeight = 28.sp, fontWeight = androidx.compose.ui.text.font.FontWeight.SemiBold),
    titleMedium = TextStyle(fontSize = 16.sp, lineHeight = 24.sp, fontWeight = androidx.compose.ui.text.font.FontWeight.SemiBold),
    bodyLarge = TextStyle(fontSize = 16.sp, lineHeight = 24.sp),
    bodyMedium = TextStyle(fontSize = 14.sp, lineHeight = 21.sp),
    bodySmall = TextStyle(fontSize = 12.sp, lineHeight = 18.sp),
    labelLarge = TextStyle(fontSize = 14.sp, lineHeight = 20.sp, fontWeight = androidx.compose.ui.text.font.FontWeight.Medium)
)
private val ShellShapes = Shapes(
    small = RoundedCornerShape(10.dp), medium = RoundedCornerShape(16.dp), large = RoundedCornerShape(20.dp)
)

/**
 * Read the DSH tokens from any composable: `val dsh = LocalDsh.current`.
 */
internal val LocalDsh: ProvidableCompositionLocal<DshColors> =
    staticCompositionLocalOf { LightTokens }

/** Technical values (host, port, URL, version, token) are always monospace. */
internal val DshMonoFontFamily: FontFamily = FontFamily.Monospace

/**
 * Same name and shape [MainActivity] already used, now driven by the DSH
 * token palette instead of Android's dynamic colour.
 */
@Composable
internal fun AppTheme(content: @Composable () -> Unit) {
    val dark = isSystemInDarkTheme()
    val dsh = if (dark) DarkTokens else LightTokens
    CompositionLocalProvider(LocalDsh provides dsh) {
        MaterialTheme(
            colorScheme = if (dark) DarkScheme else LightScheme,
            typography = ShellTypography,
            shapes = ShellShapes,
            content = content
        )
    }
}

/** Shared corner radii: sheets/cards 16dp, controls 10dp. */
internal val DshCardShape = RoundedCornerShape(16.dp)
internal val DshControlShape = RoundedCornerShape(10.dp)

/** Field colours taken from the token set instead of Material's surface roles. */
@Composable
internal fun dshFieldColors() = androidx.compose.material3.OutlinedTextFieldDefaults.colors(
    focusedTextColor = LocalDsh.current.textPrimary,
    unfocusedTextColor = LocalDsh.current.textPrimary,
    focusedContainerColor = LocalDsh.current.inputBg,
    unfocusedContainerColor = LocalDsh.current.inputBg,
    focusedBorderColor = LocalDsh.current.accent,
    unfocusedBorderColor = LocalDsh.current.borderOver(
        LocalDsh.current.inputBorder,
        LocalDsh.current.inputBg
    ),
    focusedLabelColor = LocalDsh.current.accent,
    unfocusedLabelColor = LocalDsh.current.textTertiary,
    focusedPlaceholderColor = LocalDsh.current.textTertiary,
    unfocusedPlaceholderColor = LocalDsh.current.textTertiary,
    cursorColor = LocalDsh.current.accent
)

/** Monospace body style for host / port / URL / version / token values. */
internal fun DshColors.monoStyle(size: Int = 14): TextStyle = TextStyle(
    fontFamily = DshMonoFontFamily,
    fontSize = size.sp,
    color = textPrimary
)

/** Card/sheet body: layer-1 fill, 1dp rank-1 border, 16dp radius, no elevation. */
@Composable
internal fun Modifier.dshCard(
    color: Color = LocalDsh.current.layer1,
    borderColor: Color? = null,
    shape: Shape = DshCardShape
): Modifier {
    val dsh = LocalDsh.current
    val stroke = borderColor ?: dsh.borderOver(dsh.borderL1, color)
    return this
        .clip(shape)
        .background(color, shape)
        .border(1.dp, stroke, shape)
}

/** Nested surface: layer-2 fill, 1dp rank-2 border. */
@Composable
internal fun Modifier.dshPanel(shape: Shape = DshCardShape): Modifier {
    val dsh = LocalDsh.current
    return this
        .background(dsh.layer2, shape)
        .border(1.dp, dsh.borderOver(dsh.borderL2, dsh.layer2), shape)
}

/** Standard page wash: the token base colour, edge to edge. */
@Composable
internal fun Modifier.dshPage(): Modifier = this.background(LocalDsh.current.base)
