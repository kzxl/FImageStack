package com.fimagedev.fimagestack.ui.components

import android.graphics.Bitmap
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
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
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
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

        // 1. Single Raw Frame (Right/Background)
        Image(
            bitmap = singleSliceBitmap.asImageBitmap(),
            contentDescription = "Single Focus Slice",
            contentScale = ContentScale.Fit,
            modifier = Modifier.fillMaxSize()
        )

        // 2. Fused All-In-Focus Master (Left/Clipped)
        Canvas(
            modifier = Modifier
                .fillMaxSize()
                .pointerInput(Unit) {
                    detectHorizontalDragGestures { change, dragAmount ->
                        change.consume()
                        splitFraction = (splitFraction + dragAmount / widthPx).coerceIn(0.05f, 0.95f)
                    }
                }
        ) {
            val splitX = widthPx * splitFraction

            // Draw divider line
            drawLine(
                color = PrimaryNeonGreen,
                start = Offset(splitX, 0f),
                end = Offset(splitX, heightPx),
                strokeWidth = 4.dp.toPx()
            )
        }

        // 3. Floating Indicator Badges
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
