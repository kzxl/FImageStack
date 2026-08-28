package com.fimagedev.fimagestack.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.FlashOn
import androidx.compose.material.icons.filled.Visibility
import androidx.compose.material.icons.filled.VisibilityOff
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fimagedev.fimagestack.ui.theme.*

@Composable
fun PeakingHudControls(
    isPeakingEnabled: Boolean,
    selectedColor: Int,
    isMonochromeMode: Boolean,
    onTogglePeaking: () -> Unit,
    onColorSelected: (Int) -> Unit,
    onToggleMono: () -> Unit,
    modifier: Modifier = Modifier
) {
    val colors = listOf(
        Pair(0, PrimaryNeonGreen),
        Pair(1, AccentRed),
        Pair(2, AccentYellow),
        Pair(3, PrimaryCyan),
        Pair(4, AccentMagenta)
    )

    Box(
        modifier = modifier
            .clip(RoundedCornerShape(24.dp))
            .background(BgPanel.copy(alpha = 0.85f))
            .border(1.dp, BorderDefault, RoundedCornerShape(24.dp))
            .padding(horizontal = 12.dp, vertical = 6.dp)
    ) {
        Row(
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            // Toggle Peaking On/Off
            Row(
                modifier = Modifier
                    .clip(RoundedCornerShape(12.dp))
                    .background(if (isPeakingEnabled) PrimaryNeonGreen.copy(alpha = 0.2f) else BgCard)
                    .clickable { onTogglePeaking() }
                    .padding(horizontal = 8.dp, vertical = 4.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Icon(
                    imageVector = Icons.Default.FlashOn,
                    contentDescription = "Focus Peaking",
                    tint = if (isPeakingEnabled) PrimaryNeonGreen else TextMuted,
                    modifier = Modifier.size(16.dp)
                )
                Spacer(modifier = Modifier.width(4.dp))
                Text(
                    text = "PEAKING",
                    fontSize = 10.sp,
                    fontWeight = FontWeight.Bold,
                    color = if (isPeakingEnabled) PrimaryNeonGreen else TextMuted
                )
            }

            // B&W Mono Background Toggle
            Box(
                modifier = Modifier
                    .clip(RoundedCornerShape(8.dp))
                    .background(if (isMonochromeMode) PrimaryCyan.copy(alpha = 0.2f) else BgCard)
                    .clickable { onToggleMono() }
                    .padding(horizontal = 6.dp, vertical = 4.dp)
            ) {
                Text(
                    text = if (isMonochromeMode) "B&W" else "COLOR",
                    fontSize = 9.sp,
                    fontWeight = FontWeight.Bold,
                    color = if (isMonochromeMode) PrimaryCyan else TextSecondary
                )
            }

            // Color selection circles
            Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                colors.forEach { (id, color) ->
                    Box(
                        modifier = Modifier
                            .size(16.dp)
                            .clip(CircleShape)
                            .background(color)
                            .border(
                                width = if (selectedColor == id) 2.dp else 0.dp,
                                color = if (selectedColor == id) Color.White else Color.Transparent,
                                shape = CircleShape
                            )
                            .clickable { onColorSelected(id) }
                    )
                }
            }
        }
    }
}
