package com.fimagedev.fimagestack.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.CenterFocusStrong
import androidx.compose.material.icons.filled.Remove
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fimagedev.fimagestack.camera.FocusMode
import com.fimagedev.fimagestack.ui.theme.*

@Composable
fun FocusCalibrationDeck(
    focusMode: FocusMode,
    liveDiopters: Float,
    startDiopters: Float,
    endDiopters: Float,
    steps: Int,
    onFocusModeChanged: (FocusMode) -> Unit,
    onLiveDioptersChanged: (Float) -> Unit,
    onSetNearPoint: () -> Unit,
    onSetFarPoint: () -> Unit,
    onStepsChanged: (Int) -> Unit,
    modifier: Modifier = Modifier
) {
    val distanceCm = if (liveDiopters > 0.05f) 100f / liveDiopters else 999f
    val distanceDisplay = if (distanceCm >= 999f) "∞" else String.format("%.1f cm", distanceCm)

    Column(
        modifier = modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(16.dp))
            .background(BgPanel.copy(alpha = 0.90f))
            .border(1.dp, BorderDefault, RoundedCornerShape(16.dp))
            .padding(horizontal = 12.dp, vertical = 8.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        // Row 1: Focus Mode Selector + Distance Readout
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            // Focus Mode Pills
            Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                listOf(
                    FocusMode.MANUAL to "MF",
                    FocusMode.CONTINUOUS_AF to "AF-C",
                    FocusMode.AF_LOCKED to "AF-L"
                ).forEach { (mode, label) ->
                    val isSelected = focusMode == mode
                    Box(
                        modifier = Modifier
                            .clip(RoundedCornerShape(8.dp))
                            .background(if (isSelected) PrimaryNeonGreen else BgCard)
                            .clickable { onFocusModeChanged(mode) }
                            .padding(horizontal = 10.dp, vertical = 4.dp),
                        contentAlignment = Alignment.Center
                    ) {
                        Text(
                            text = label,
                            fontSize = 11.sp,
                            fontWeight = FontWeight.Bold,
                            color = if (isSelected) BgDark else TextSecondary
                        )
                    }
                }
            }

            // Live Diopter & Physical Distance Readout
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Default.CenterFocusStrong, contentDescription = null, tint = PrimaryNeonGreen, modifier = Modifier.size(14.dp))
                Spacer(modifier = Modifier.width(4.dp))
                Text(
                    text = String.format("%.1f D (%s)", liveDiopters, distanceDisplay),
                    fontSize = 12.sp,
                    fontWeight = FontWeight.Bold,
                    color = PrimaryNeonGreen
                )
            }
        }

        Spacer(modifier = Modifier.height(6.dp))

        // Row 2: Live Focus Slider (Active when MF is selected)
        Slider(
            value = liveDiopters,
            onValueChange = { onLiveDioptersChanged(it) },
            valueRange = 0.5f..10.0f,
            enabled = focusMode == FocusMode.MANUAL,
            colors = SliderDefaults.colors(
                thumbColor = PrimaryNeonGreen,
                activeTrackColor = PrimaryNeonGreen,
                inactiveTrackColor = BgCard
            ),
            modifier = Modifier.fillMaxWidth().height(24.dp)
        )

        Spacer(modifier = Modifier.height(6.dp))

        // Row 3: 1-Tap Calibration Actions (Set Near / Set Far / Slices)
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            // Set Near
            Box(
                modifier = Modifier
                    .clip(RoundedCornerShape(8.dp))
                    .background(BgCard)
                    .border(1.dp, BorderDefault, RoundedCornerShape(8.dp))
                    .clickable { onSetNearPoint() }
                    .padding(horizontal = 8.dp, vertical = 4.dp)
            ) {
                Text(
                    text = String.format("NEAR: %.1fD", startDiopters),
                    fontSize = 10.sp,
                    fontWeight = FontWeight.Bold,
                    color = PrimaryCyan
                )
            }

            // Set Far
            Box(
                modifier = Modifier
                    .clip(RoundedCornerShape(8.dp))
                    .background(BgCard)
                    .border(1.dp, BorderDefault, RoundedCornerShape(8.dp))
                    .clickable { onSetFarPoint() }
                    .padding(horizontal = 8.dp, vertical = 4.dp)
            ) {
                Text(
                    text = String.format("FAR: %.1fD", endDiopters),
                    fontSize = 10.sp,
                    fontWeight = FontWeight.Bold,
                    color = AccentYellow
                )
            }

            // Steps Counter
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(4.dp)
            ) {
                Box(
                    modifier = Modifier
                        .size(24.dp)
                        .clip(CircleShape)
                        .background(BgCard)
                        .clickable { if (steps > 3) onStepsChanged(steps - 1) },
                    contentAlignment = Alignment.Center
                ) {
                    Icon(Icons.Default.Remove, contentDescription = "Decrease", tint = TextPrimary, modifier = Modifier.size(12.dp))
                }

                Text(
                    text = "$steps Slices",
                    fontSize = 11.sp,
                    fontWeight = FontWeight.Bold,
                    color = TextPrimary
                )

                Box(
                    modifier = Modifier
                        .size(24.dp)
                        .clip(CircleShape)
                        .background(BgCard)
                        .clickable { if (steps < 25) onStepsChanged(steps + 1) },
                    contentAlignment = Alignment.Center
                ) {
                    Icon(Icons.Default.Add, contentDescription = "Increase", tint = TextPrimary, modifier = Modifier.size(12.dp))
                }
            }
        }
    }
}
