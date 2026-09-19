package com.harness.portable

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
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

/**
 * DeepSeek Harness design tokens.
 *
 * These values are copied verbatim from DSH's own design system
 * (`dsh-client-ui-theme`) so the Android shell and the web console it hosts
 * share one visual language. Anything Material3 has no slot for (layered
 * surfaces, tertiary text, the three border ranks, the mono family) travels
 * alongside the scheme through [LocalDsh].
 */
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
        border.over(over, border.alpha * border.alpha)

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
    base = Color(0xFFFFFFFF),
    layer1 = Color(0xFFFFFFFF),
    layer2 = Color(0xFFF5F6F7),
    layer3 = Color(0xFFEBEEF2),
    borderL1 = Color(0x0A000000),
    borderL1On = Color(0xFFFFFFFF),
    borderL2 = Color(0x1A000000),
    borderL2On = Color(0xFFFFFFFF),
    borderL3 = Color(0x1F000000),
    borderL3On = Color(0xFFFFFFFF),
    textPrimary = Color(0xFF0F1115),
    textSecondary = Color(0xFF61666B),
    textTertiary = Color(0xFF81858C),
    brandFill = Color(0xFF0F1115),
    brandHover = Color(0xFF2C2C2E),
    brandPressed = Color(0xFF43454A),
    brandText = Color(0xFFFFFFFF),
    accent = Color(0xFF4176E6),
    success = Color(0xFF22C55E),
    danger = Color(0xFFEC1313),
    warning = Color(0xFFF7AD31),
    inputBg = Color(0xFFFFFFFF),
    inputBorder = Color(0x1A000000),
    scrim = Color(0x99000000),
    isDark = false
)

private val DarkTokens = DshColors(
    base = Color(0xFF151517),
    layer1 = Color(0xFF232324),
    layer2 = Color(0xFF2C2C2E),
    layer3 = Color(0xFF353638),
    borderL1 = Color(0x0FFFFFFF),
    borderL1On = Color(0xFF000000),
    borderL2 = Color(0x1FFFFFFF),
    borderL2On = Color(0xFF000000),
    borderL3 = Color(0x29FFFFFF),
    borderL3On = Color(0xFF000000),
    textPrimary = Color(0xFFF9FAFB),
    textSecondary = Color(0xFFCFD3D6),
    textTertiary = Color(0xFFADB2B8),
    brandFill = Color(0xFFF9FAFB),
    brandHover = Color(0xFFE1E5EE),
    brandPressed = Color(0xFFCFD3D6),
    brandText = Color(0xFF0F1115),
    accent = Color(0xFF5686FE),
    success = Color(0xFF22C55E),
    danger = Color(0xFFF25A5A),
    warning = Color(0xFFF7AD31),
    inputBg = Color(0xFF232324),
    inputBorder = Color(0x1FFFFFFF),
    scrim = Color(0xCC000000),
    isDark = true
)

/**
 * Material3 has no role for `layer1`/`layer2`/`layer3`, tertiary text, the
 * border ranks or the mono family, so the fields Material3 *does* own are
 * mapped here and the rest is exposed through [LocalDsh].
 *
 * Note the deliberate inversion: `primary` is the *brand fill* (near-black in
 * light mode, near-white in dark mode), which is exactly how DSH paints its
 * primary button. `onSurface` stays the text rank, so components that read it
 * keep readable text.
 */
private fun DshColors.toColorScheme() = if (isDark) {
    darkColorScheme(
        primary = brandFill,
        onPrimary = brandText,
        primaryContainer = brandHover,
        onPrimaryContainer = brandText,
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
        surface = base,
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
        outline = Color(0xFF4A4C50),
        outlineVariant = Color(0xFF353638),
        scrim = Color(0xFF000000)
    )
} else {
    lightColorScheme(
        primary = brandFill,
        onPrimary = brandText,
        primaryContainer = brandHover,
        onPrimaryContainer = brandText,
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
        surface = base,
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
        outline = Color(0xFF81858C),
        outlineVariant = Color(0xFFEBEEF2),
        scrim = Color(0xFF000000)
    )
}

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
    val dsh = if (isSystemInDarkTheme()) DarkTokens else LightTokens
    CompositionLocalProvider(LocalDsh provides dsh) {
        MaterialTheme(
            colorScheme = dsh.toColorScheme(),
            content = content
        )
    }
}

/** Shared corner radii: sheets/cards 12dp, controls 8dp. */
internal val DshCardShape = RoundedCornerShape(12.dp)
internal val DshControlShape = RoundedCornerShape(8.dp)

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

/** Card/sheet body: layer-1 fill, 1dp rank-1 border, 12dp radius, no elevation. */
@Composable
internal fun Modifier.dshCard(
    color: Color = LocalDsh.current.layer1,
    borderColor: Color? = null,
    shape: Shape = DshCardShape
): Modifier {
    val dsh = LocalDsh.current
    val stroke = borderColor ?: dsh.borderOver(dsh.borderL1, color)
    return this
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
