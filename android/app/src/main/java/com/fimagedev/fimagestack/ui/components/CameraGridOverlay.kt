package com.fimagedev.fimagestack.ui.components

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp

@Composable
fun CameraGridOverlay(
    modifier: Modifier = Modifier,
    gridColor: Color = Color.White.copy(alpha = 0.35f)
) {
    Canvas(modifier = modifier.fillMaxSize()) {
        val width = size.width
        val height = size.height

        // 2 Vertical Lines (Rule of thirds)
        val oneThirdW = width / 3f
        val twoThirdW = width * 2f / 3f
        drawLine(
            color = gridColor,
            start = Offset(oneThirdW, 0f),
            end = Offset(oneThirdW, height),
            strokeWidth = 1.dp.toPx()
        )
        drawLine(
            color = gridColor,
            start = Offset(twoThirdW, 0f),
            end = Offset(twoThirdW, height),
            strokeWidth = 1.dp.toPx()
        )

        // 2 Horizontal Lines (Rule of thirds)
        val oneThirdH = height / 3f
        val twoThirdH = height * 2f / 3f
        drawLine(
            color = gridColor,
            start = Offset(0f, oneThirdH),
            end = Offset(width, oneThirdH),
            strokeWidth = 1.dp.toPx()
        )
        drawLine(
            color = gridColor,
            start = Offset(0f, twoThirdH),
            end = Offset(width, twoThirdH),
            strokeWidth = 1.dp.toPx()
        )

        // Center Crosshair
        val centerX = width / 2f
        val centerY = height / 2f
        val crossSize = 12.dp.toPx()
        drawLine(
            color = gridColor.copy(alpha = 0.6f),
            start = Offset(centerX - crossSize, centerY),
            end = Offset(centerX + crossSize, centerY),
            strokeWidth = 1.5.dp.toPx()
        )
        drawLine(
            color = gridColor.copy(alpha = 0.6f),
            start = Offset(centerX, centerY - crossSize),
            end = Offset(centerX, centerY + crossSize),
            strokeWidth = 1.5.dp.toPx()
        )
    }
}
