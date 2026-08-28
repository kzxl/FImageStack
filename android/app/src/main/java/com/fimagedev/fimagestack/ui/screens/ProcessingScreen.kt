package com.fimagedev.fimagestack.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fimagedev.fimagestack.ui.theme.*

@Composable
fun ProcessingScreen(
    currentStage: String,
    stageDescription: String,
    progressPercentage: Float,
    activeFramesCount: Int,
    culledFramesCount: Int,
    modifier: Modifier = Modifier
) {
    Box(
        modifier = modifier
            .fillMaxSize()
            .background(BgDark),
        contentAlignment = Alignment.Center
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(32.dp)
                .clip(RoundedCornerShape(24.dp))
                .background(BgPanel)
                .border(1.dp, BorderDefault, RoundedCornerShape(24.dp))
                .padding(24.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            CircularProgressIndicator(
                progress = { progressPercentage / 100f },
                color = PrimaryNeonGreen,
                trackColor = BgCard,
                modifier = Modifier.size(72.dp),
                strokeWidth = 6.dp
            )

            Spacer(modifier = Modifier.height(24.dp))

            Text(
                text = currentStage.uppercase(),
                fontSize = 16.sp,
                fontWeight = FontWeight.Bold,
                color = PrimaryCyan
            )

            Spacer(modifier = Modifier.height(8.dp))

            Text(
                text = stageDescription,
                fontSize = 12.sp,
                color = TextSecondary
            )

            Spacer(modifier = Modifier.height(20.dp))

            LinearProgressIndicator(
                progress = { progressPercentage / 100f },
                color = PrimaryNeonGreen,
                trackColor = BgCard,
                modifier = Modifier
                    .fillMaxWidth()
                    .height(6.dp)
                    .clip(RoundedCornerShape(3.dp))
            )

            Spacer(modifier = Modifier.height(16.dp))

            // Diagnostic Quality Culling Card
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(8.dp))
                    .background(BgCard)
                    .padding(12.dp),
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                Text(
                    text = "Active Slices: $activeFramesCount",
                    fontSize = 11.sp,
                    fontWeight = FontWeight.Bold,
                    color = PrimaryNeonGreen
                )
                Text(
                    text = "Culled Blurry: $culledFramesCount",
                    fontSize = 11.sp,
                    fontWeight = FontWeight.Bold,
                    color = AccentRed
                )
            }
        }
    }
}
