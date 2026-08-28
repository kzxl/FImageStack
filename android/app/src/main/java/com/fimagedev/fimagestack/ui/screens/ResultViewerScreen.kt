package com.fimagedev.fimagestack.ui.screens

import android.graphics.Bitmap
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Share
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fimagedev.fimagestack.ui.components.SplitComparisonView
import com.fimagedev.fimagestack.ui.components.ZoomableBox
import com.fimagedev.fimagestack.ui.theme.*

@Composable
fun ResultViewerScreen(
    fusedBitmap: Bitmap,
    rawFirstSliceBitmap: Bitmap,
    depthMapBitmap: Bitmap?,
    dofPreservedPercentage: Float,
    executionTimeMs: Long,
    onBackToCamera: () -> Unit,
    onExportResult: () -> Unit,
    modifier: Modifier = Modifier
) {
    var selectedTab by remember { mutableIntStateOf(0) } // 0: Split A/B, 1: Full Fused, 2: 3D Depth Map

    Box(
        modifier = modifier
            .fillMaxSize()
            .background(BgDark)
    ) {
        // Main Interactive Zoomable Viewer Area
        ZoomableBox(
            modifier = Modifier.fillMaxSize(),
            minScale = 1.0f,
            maxScale = 8.0f
        ) {
            when (selectedTab) {
                0 -> SplitComparisonView(
                    fusedBitmap = fusedBitmap,
                    singleSliceBitmap = rawFirstSliceBitmap,
                    modifier = Modifier.fillMaxSize()
                )
                1 -> Image(
                    bitmap = fusedBitmap.asImageBitmap(),
                    contentDescription = "Full Master Fused",
                    contentScale = ContentScale.Fit,
                    modifier = Modifier.fillMaxSize()
                )
                2 -> if (depthMapBitmap != null) {
                    Image(
                        bitmap = depthMapBitmap.asImageBitmap(),
                        contentDescription = "3D Turbo Depth Map",
                        contentScale = ContentScale.Fit,
                        modifier = Modifier.fillMaxSize()
                    )
                }
            }
        }

        // Top Navigation Bar
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .statusBarsPadding()
                .padding(16.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            IconButton(
                onClick = onBackToCamera,
                modifier = Modifier
                    .size(42.dp)
                    .clip(CircleShape)
                    .background(BgPanel.copy(alpha = 0.85f))
                    .border(1.dp, BorderDefault, CircleShape)
            ) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back", tint = TextPrimary)
            }

            // Quality & Metric Badge
            Box(
                modifier = Modifier
                    .clip(RoundedCornerShape(12.dp))
                    .background(BgPanel.copy(alpha = 0.85f))
                    .border(1.dp, BorderDefault, RoundedCornerShape(12.dp))
                    .padding(horizontal = 12.dp, vertical = 6.dp)
            ) {
                Text(
                    text = String.format("DOF: +%.1f%% (%dms)", dofPreservedPercentage, executionTimeMs),
                    fontSize = 11.sp,
                    fontWeight = FontWeight.Bold,
                    color = PrimaryNeonGreen
                )
            }

            IconButton(
                onClick = onExportResult,
                modifier = Modifier
                    .size(42.dp)
                    .clip(CircleShape)
                    .background(PrimaryNeonGreen)
            ) {
                Icon(Icons.Default.Share, contentDescription = "Export", tint = BgDark)
            }
        }

        // Bottom View Switcher Tabs
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .align(Alignment.BottomCenter)
                .navigationBarsPadding()
                .padding(16.dp)
        ) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(16.dp))
                    .background(BgPanel.copy(alpha = 0.90f))
                    .border(1.dp, BorderDefault, RoundedCornerShape(16.dp))
                    .padding(6.dp),
                horizontalArrangement = Arrangement.SpaceEvenly
            ) {
                val tabs = listOf("SPLIT A/B", "ALL-IN-FOCUS", "3D DEPTH")
                tabs.forEachIndexed { index, label ->
                    val isSelected = selectedTab == index
                    Button(
                        onClick = { selectedTab = index },
                        colors = ButtonDefaults.buttonColors(
                            containerColor = if (isSelected) PrimaryNeonGreen else BgCard,
                            contentColor = if (isSelected) BgDark else TextSecondary
                        ),
                        shape = RoundedCornerShape(10.dp),
                        modifier = Modifier.weight(1f).padding(horizontal = 4.dp),
                        contentPadding = PaddingValues(vertical = 8.dp)
                    ) {
                        Text(
                            text = label,
                            fontSize = 11.sp,
                            fontWeight = if (isSelected) FontWeight.Bold else FontWeight.Normal
                        )
                    }
                }
            }
        }
    }
}
