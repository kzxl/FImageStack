package com.fimagedev.fimagestack.ui.components

import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.detectTransformGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fimagedev.fimagestack.ui.theme.*

@Composable
fun ZoomableBox(
    modifier: Modifier = Modifier,
    minScale: Float = 1.0f,
    maxScale: Float = 8.0f,
    content: @Composable BoxScope.() -> Unit
) {
    var targetScale by remember { mutableFloatStateOf(1.0f) }
    var offsetX by remember { mutableFloatStateOf(0.0f) }
    var offsetY by remember { mutableFloatStateOf(0.0f) }

    val animatedScale by animateFloatAsState(
        targetValue = targetScale,
        animationSpec = spring(
            dampingRatio = Spring.DampingRatioMediumBouncy,
            stiffness = Spring.StiffnessLow
        ),
        label = "ZoomScale"
    )

    BoxWithConstraints(
        modifier = modifier
            .fillMaxSize()
            .pointerInput(Unit) {
                detectTapGestures(
                    onDoubleTap = { tapOffset ->
                        if (targetScale > 1.2f) {
                            targetScale = 1.0f
                            offsetX = 0.0f
                            offsetY = 0.0f
                        } else {
                            targetScale = 2.5f
                            // Zoom toward double-tapped point
                            offsetX = (size.width / 2f - tapOffset.x) * 1.5f
                            offsetY = (size.height / 2f - tapOffset.y) * 1.5f
                        }
                    }
                )
            }
            .pointerInput(Unit) {
                detectTransformGestures { _, pan, zoom, _ ->
                    val newScale = (targetScale * zoom).coerceIn(minScale, maxScale)
                    targetScale = newScale

                    if (newScale > 1.0f) {
                        val maxOffsetX = (size.width * (newScale - 1.0f)) / 2f
                        val maxOffsetY = (size.height * (newScale - 1.0f)) / 2f

                        offsetX = (offsetX + pan.x * newScale).coerceIn(-maxOffsetX, maxOffsetX)
                        offsetY = (offsetY + pan.y * newScale).coerceIn(-maxOffsetY, maxOffsetY)
                    } else {
                        offsetX = 0.0f
                        offsetY = 0.0f
                    }
                }
            }
    ) {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .graphicsLayer {
                    scaleX = animatedScale
                    scaleY = animatedScale
                    translationX = offsetX
                    translationY = offsetY
                },
            content = content
        )

        // Floating Zoom Level Indicator & Reset Button
        if (animatedScale > 1.05f) {
            Box(
                modifier = Modifier
                    .align(Alignment.BottomEnd)
                    .padding(bottom = 72.dp, end = 16.dp)
                    .clip(RoundedCornerShape(20.dp))
                    .background(BgPanel.copy(alpha = 0.90f))
                    .clickable {
                        targetScale = 1.0f
                        offsetX = 0.0f
                        offsetY = 0.0f
                    }
                    .padding(horizontal = 12.dp, vertical = 6.dp)
            ) {
                Text(
                    text = String.format("🔍 %.1fx (Tap to Reset)", animatedScale),
                    fontSize = 11.sp,
                    fontWeight = FontWeight.Bold,
                    color = PrimaryNeonGreen
                )
            }
        }
    }
}
