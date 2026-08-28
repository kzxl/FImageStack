package com.fimagedev.fimagestack.ui.components

import android.graphics.Bitmap
import android.graphics.Paint
import android.graphics.Rect
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.drawIntoCanvas
import androidx.compose.ui.graphics.nativeCanvas
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fimagedev.fimagestack.ui.theme.*

@Composable
fun SplitComparisonView(
    fusedBitmap: Bitmap,
    singleSliceBitmap: Bitmap,
    modifier: Modifier = Modifier
) {
    var splitFraction by remember { mutableFloatStateOf(0.5f) }

    BoxWithConstraints(
        modifier = modifier
            .fillMaxSize()
            .background(BgDark)
    ) {
        val widthPx = constraints.maxWidth.toFloat()
        val heightPx = constraints.maxHeight.toFloat()

        Canvas(
            modifier = Modifier
                .fillMaxSize()
                .pointerInput(Unit) {
                    detectHorizontalDragGestures { change, dragAmount ->
                        change.consume()
                        splitFraction = (splitFraction + dragAmount / widthPx).coerceIn(0.02f, 0.98f)
                    }
                }
        ) {
            val splitX = widthPx * splitFraction
            val canvas = drawIntoCanvas { it.nativeCanvas }

            drawIntoCanvas { nativeCanvasScope ->
                val nCanvas = nativeCanvasScope.nativeCanvas
                val srcRect = Rect(0, 0, singleSliceBitmap.width, singleSliceBitmap.height)
                val dstRect = Rect(0, 0, widthPx.toInt(), heightPx.toInt())
                val paint = Paint().apply { isFilterBitmap = true }

                // 1. Draw Single Raw Slice on entire background
                nCanvas.drawBitmap(singleSliceBitmap, srcRect, dstRect, paint)

                // 2. Draw Fused All-In-Focus Master clipped to left of split line
                nCanvas.save()
                nCanvas.clipRect(0f, 0f, splitX, heightPx)
                nCanvas.drawBitmap(fusedBitmap, srcRect, dstRect, paint)
                nCanvas.restore()
            }

            // 3. Draw Neon Divider Line
            drawLine(
                color = PrimaryNeonGreen,
                start = Offset(splitX, 0f),
                end = Offset(splitX, heightPx),
                strokeWidth = 3.dp.toPx()
            )

            // 4. Draw Center Drag Handle Circle
            val centerY = heightPx / 2f
            drawCircle(
                color = PrimaryNeonGreen,
                radius = 16.dp.toPx(),
                center = Offset(splitX, centerY)
            )
            drawCircle(
                color = BgDark,
                radius = 12.dp.toPx(),
                center = Offset(splitX, centerY)
            )
            drawCircle(
                color = PrimaryNeonGreen,
                radius = 5.dp.toPx(),
                center = Offset(splitX, centerY)
            )
        }

        // Floating Indicator Badges
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp),
            horizontalArrangement = Arrangement.SpaceBetween
        ) {
            Box(
                modifier = Modifier
                    .clip(CircleShape)
                    .background(PrimaryNeonGreen.copy(alpha = 0.85f))
                    .padding(horizontal = 12.dp, vertical = 6.dp)
            ) {
                Text(
                    text = "◀ ALL-IN-FOCUS MASTER",
                    fontSize = 10.sp,
                    fontWeight = FontWeight.Bold,
                    color = BgDark
                )
            }

            Box(
                modifier = Modifier
                    .clip(CircleShape)
                    .background(BgCard.copy(alpha = 0.85f))
                    .padding(horizontal = 12.dp, vertical = 6.dp)
            ) {
                Text(
                    text = "SINGLE RAW SLICE ▶",
                    fontSize = 10.sp,
                    fontWeight = FontWeight.Bold,
                    color = TextPrimary
                )
            }
        }
    }
}
