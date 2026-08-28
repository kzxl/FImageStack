package com.fimagedev.fimagestack.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fimagedev.fimagestack.camera.CameraLensInfo
import com.fimagedev.fimagestack.ui.theme.*

@Composable
fun LensSelectorRow(
    lenses: List<CameraLensInfo>,
    selectedLensId: String,
    onLensSelected: (CameraLensInfo) -> Unit,
    modifier: Modifier = Modifier
) {
    if (lenses.size <= 1) return

    Row(
        modifier = modifier
            .clip(RoundedCornerShape(20.dp))
            .background(BgPanel.copy(alpha = 0.85f))
            .border(1.dp, BorderDefault, RoundedCornerShape(20.dp))
            .padding(4.dp),
        horizontalArrangement = Arrangement.spacedBy(4.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        lenses.forEach { lens ->
            val isSelected = lens.id == selectedLensId
            Box(
                modifier = Modifier
                    .size(36.dp)
                    .clip(CircleShape)
                    .background(if (isSelected) PrimaryNeonGreen else Color.Transparent)
                    .clickable { onLensSelected(lens) },
                contentAlignment = Alignment.Center
            ) {
                Text(
                    text = lens.label,
                    fontSize = 11.sp,
                    fontWeight = FontWeight.Bold,
                    color = if (isSelected) BgDark else TextPrimary
                )
            }
        }
    }
}
