package com.fimagedev.fimagestack.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
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
fun FocusRangeSlider(
    startDiopters: Float,
    endDiopters: Float,
    steps: Int,
    onStartChanged: (Float) -> Unit,
    onEndChanged: (Float) -> Unit,
    onStepsChanged: (Int) -> Unit,
    modifier: Modifier = Modifier
) {
    Box(
        modifier = modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(16.dp))
            .background(BgPanel.copy(alpha = 0.85f))
            .border(1.dp, BorderDefault, RoundedCornerShape(16.dp))
            .padding(16.dp)
    ) {
        Column {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    text = "🎯 MACRO FOCUS BRACKETING",
                    fontSize = 12.sp,
                    fontWeight = FontWeight.Bold,
                    color = PrimaryCyan
                )
                Text(
                    text = "$steps Slices",
                    fontSize = 12.sp,
                    fontWeight = FontWeight.Bold,
                    color = PrimaryNeonGreen
                )
            }

            Spacer(modifier = Modifier.height(12.dp))

            // Near Distance (Start Diopters)
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    text = "Near:",
                    fontSize = 11.sp,
                    color = TextSecondary,
                    modifier = Modifier.width(45.dp)
                )
                Slider(
                    value = startDiopters,
                    onValueChange = onStartChanged,
                    valueRange = 0.5f..15.0f,
                    colors = SliderDefaults.colors(
                        thumbColor = PrimaryNeonGreen,
                        activeTrackColor = PrimaryNeonGreen,
                        inactiveTrackColor = BgCard
                    ),
                    modifier = Modifier.weight(1f)
                )
                Text(
                    text = String.format("%.1f D", startDiopters),
                    fontSize = 11.sp,
                    fontWeight = FontWeight.Bold,
                    color = TextPrimary,
                    modifier = Modifier.width(45.dp)
                )
            }

            // Far Distance (End Diopters)
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    text = "Far:",
                    fontSize = 11.sp,
                    color = TextSecondary,
                    modifier = Modifier.width(45.dp)
                )
                Slider(
                    value = endDiopters,
                    onValueChange = onEndChanged,
                    valueRange = 0.1f..10.0f,
                    colors = SliderDefaults.colors(
                        thumbColor = PrimaryCyan,
                        activeTrackColor = PrimaryCyan,
                        inactiveTrackColor = BgCard
                    ),
                    modifier = Modifier.weight(1f)
                )
                Text(
                    text = String.format("%.1f D", endDiopters),
                    fontSize = 11.sp,
                    fontWeight = FontWeight.Bold,
                    color = TextPrimary,
                    modifier = Modifier.width(45.dp)
                )
            }
        }
    }
}
