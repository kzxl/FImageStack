package com.fimagedev.fimagestack.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fimagedev.fimagestack.camera.FlashMode
import com.fimagedev.fimagestack.ui.theme.*

@Composable
fun CameraTopToolbar(
    flashMode: FlashMode,
    timerSeconds: Int,
    isGridEnabled: Boolean,
    aspectRatio: String, // "4:3", "16:9", "1:1"
    onToggleFlash: () -> Unit,
    onToggleTimer: () -> Unit,
    onToggleGrid: () -> Unit,
    onToggleAspectRatio: () -> Unit,
    modifier: Modifier = Modifier
) {
    Row(
        modifier = modifier
            .fillMaxWidth()
            .statusBarsPadding()
            .padding(horizontal = 16.dp, vertical = 8.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        // 1. Flash / Torch Mode
        IconButton(
            onClick = onToggleFlash,
            modifier = Modifier
                .size(40.dp)
                .clip(CircleShape)
                .background(if (flashMode != FlashMode.OFF) PrimaryNeonGreen else BgPanel.copy(alpha = 0.85f))
                .border(1.dp, BorderDefault, CircleShape)
        ) {
            val icon = when (flashMode) {
                FlashMode.OFF -> Icons.Default.FlashOff
                FlashMode.TORCH -> Icons.Default.FlashlightOn
                FlashMode.AUTO -> Icons.Default.FlashAuto
            }
            Icon(
                imageVector = icon,
                contentDescription = "Flash Mode",
                tint = if (flashMode != FlashMode.OFF) BgDark else TextPrimary,
                modifier = Modifier.size(20.dp)
            )
        }

        // 2. Self-Timer (Off / 2s / 5s)
        Box(
            modifier = Modifier
                .clip(RoundedCornerShape(12.dp))
                .background(if (timerSeconds > 0) PrimaryNeonGreen else BgPanel.copy(alpha = 0.85f))
                .border(1.dp, BorderDefault, RoundedCornerShape(12.dp))
                .clickable { onToggleTimer() }
                .padding(horizontal = 10.dp, vertical = 6.dp),
            contentAlignment = Alignment.Center
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(
                    imageVector = Icons.Default.Timer,
                    contentDescription = "Self Timer",
                    tint = if (timerSeconds > 0) BgDark else TextPrimary,
                    modifier = Modifier.size(16.dp)
                )
                if (timerSeconds > 0) {
                    Spacer(modifier = Modifier.width(4.dp))
                    Text(
                        text = "${timerSeconds}s",
                        fontSize = 11.sp,
                        fontWeight = FontWeight.Bold,
                        color = BgDark
                    )
                }
            }
        }

        // 3. Grid Overlay Toggle
        IconButton(
            onClick = onToggleGrid,
            modifier = Modifier
                .size(40.dp)
                .clip(CircleShape)
                .background(if (isGridEnabled) PrimaryNeonGreen else BgPanel.copy(alpha = 0.85f))
                .border(1.dp, BorderDefault, CircleShape)
        ) {
            Icon(
                imageVector = Icons.Default.GridOn,
                contentDescription = "Grid Overlay",
                tint = if (isGridEnabled) BgDark else TextPrimary,
                modifier = Modifier.size(20.dp)
            )
        }

        // 4. Aspect Ratio Selector
        Box(
            modifier = Modifier
                .clip(RoundedCornerShape(12.dp))
                .background(BgPanel.copy(alpha = 0.85f))
                .border(1.dp, BorderDefault, RoundedCornerShape(12.dp))
                .clickable { onToggleAspectRatio() }
                .padding(horizontal = 10.dp, vertical = 6.dp)
        ) {
            Text(
                text = aspectRatio,
                fontSize = 11.sp,
                fontWeight = FontWeight.Bold,
                color = PrimaryNeonGreen
            )
        }
    }
}
