package com.cursor.client.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

private val CursorDark = darkColorScheme(
    primary = Color(0xFFEDECEC),
    onPrimary = Color(0xFF0E0E10),
    secondary = Color(0xFF7C7C7C),
    background = Color(0xFF0E0E10),
    surface = Color(0xFF17171A),
    onBackground = Color(0xFFEDECEC),
    onSurface = Color(0xFFEDECEC),
    surfaceVariant = Color(0xFF232328),
    outline = Color(0xFF3A3A40),
)

private val CursorLight = lightColorScheme(
    primary = Color(0xFF0E0E10),
    onPrimary = Color(0xFFEDECEC),
    secondary = Color(0xFF5C5C5C),
    background = Color(0xFFF7F7F5),
    surface = Color(0xFFFFFFFF),
    onBackground = Color(0xFF0E0E10),
    onSurface = Color(0xFF0E0E10),
    surfaceVariant = Color(0xFFECECEA),
    outline = Color(0xFFC8C8C4),
)

@Composable
fun CursorClientTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit,
) {
    MaterialTheme(
        colorScheme = if (darkTheme) CursorDark else CursorLight,
        content = content,
    )
}
