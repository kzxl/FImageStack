package com.fimagedev.fimagestack.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.BrightnessLow
import androidx.compose.material.icons.filled.WbSunny
import androidx.compose.material3.Icon
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
fun ExposureSlider(
    currentEv: Int,
    evStepRational: Float,
    minEv: Int,
    maxEv: Int,
    onEvChanged: (Int) -> Unit,
    modifier: Modifier = Modifier
) {
    val evDisplay = String.format("%+.1f EV", currentEv * evStepRational)

    Box(
        modifier = modifier
            .clip(RoundedCornerShape(16.dp))
            .background(BgPanel.copy(alpha = 0.85f))
            .border(1.dp, BorderDefault, RoundedCornerShape(16.dp))
            .padding(horizontal = 12.dp, vertical = 6.dp)
    ) {
        Row(
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            Icon(
                imageVector = Icons.Default.WbSunny,
                contentDescription = "EV Meter",
                tint = PrimaryNeonGreen,
                modifier = Modifier.size(16.dp)
            )

            Slider(
                value = currentEv.toFloat(),
                onValueChange = { onEvChanged(it.toInt()) },
                valueRange = minEv.toFloat()..maxEv.toFloat(),
                steps = maxEv - minEv - 1,
                colors = SliderDefaults.colors(
                    thumbColor = PrimaryNeonGreen,
                    activeTrackColor = PrimaryNeonGreen,
                    inactiveTrackColor = BgCard
                ),
                modifier = Modifier.width(120.dp).height(28.dp)
            )

            Text(
                text = evDisplay,
                fontSize = 11.sp,
                fontWeight = FontWeight.Bold,
                color = PrimaryNeonGreen
            )
        }
    }
}
