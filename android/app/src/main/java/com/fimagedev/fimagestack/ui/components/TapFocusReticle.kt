package com.fimagedev.fimagestack.ui.components

import androidx.compose.animation.core.*
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.unit.dp
import com.fimagedev.fimagestack.ui.theme.PrimaryNeonGreen

@Composable
fun TapFocusReticle(
    tapPoint: Offset?,
    modifier: Modifier = Modifier
) {
    if (tapPoint == null) return

    val infiniteTransition = rememberInfiniteTransition(label = "FocusPulse")
    val pulseScale by infiniteTransition.animateFloat(
        initialValue = 1.2f,
        targetValue = 0.9f,
        animationSpec = infiniteRepeatable(
            animation = tween(400, easing = FastOutSlowInEasing),
            repeatMode = RepeatMode.Reverse
        ),
        label = "FocusScale"
    )

    Canvas(modifier = modifier.fillMaxSize()) {
        val baseRadius = 36.dp.toPx()
        val currentRadius = baseRadius * pulseScale

        // Outer focus circle
        drawCircle(
            color = PrimaryNeonGreen,
            radius = currentRadius,
            center = tapPoint,
            style = Stroke(width = 2.dp.toPx())
        )

        // 4 Corner Tick marks
        val tickLength = 8.dp.toPx()
        // Top
        drawLine(
            color = PrimaryNeonGreen,
            start = Offset(tapPoint.x, tapPoint.y - currentRadius),
            end = Offset(tapPoint.x, tapPoint.y - currentRadius - tickLength),
            strokeWidth = 2.dp.toPx()
        )
        // Bottom
        drawLine(
            color = PrimaryNeonGreen,
            start = Offset(tapPoint.x, tapPoint.y + currentRadius),
            end = Offset(tapPoint.x, tapPoint.y + currentRadius + tickLength),
            strokeWidth = 2.dp.toPx()
        )
        // Left
        drawLine(
            color = PrimaryNeonGreen,
            start = Offset(tapPoint.x - currentRadius, tapPoint.y),
            end = Offset(tapPoint.x - currentRadius - tickLength, tapPoint.y),
            strokeWidth = 2.dp.toPx()
        )
        // Right
        drawLine(
            color = PrimaryNeonGreen,
            start = Offset(tapPoint.x + currentRadius, tapPoint.y),
            end = Offset(tapPoint.x + currentRadius + tickLength, tapPoint.y),
            strokeWidth = 2.dp.toPx()
        )
    }
}
