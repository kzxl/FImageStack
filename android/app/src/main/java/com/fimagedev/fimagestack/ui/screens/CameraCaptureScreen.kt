package com.fimagedev.fimagestack.ui.screens

import android.graphics.Bitmap
import android.view.SurfaceView
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Camera
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import com.fimagedev.fimagestack.camera.MacroBurstConfig
import com.fimagedev.fimagestack.ui.components.FocusRangeSlider
import com.fimagedev.fimagestack.ui.components.PeakingHudControls
import com.fimagedev.fimagestack.ui.theme.*

@Composable
fun CameraCaptureScreen(
    peakingBitmap: Bitmap?,
    isPeakingEnabled: Boolean,
    peakingColor: Int,
    isMonochromeMode: Boolean,
    burstConfig: MacroBurstConfig,
    isCapturing: Boolean,
    capturedCount: Int,
    onTogglePeaking: () -> Unit,
    onColorSelected: (Int) -> Unit,
    onToggleMono: () -> Unit,
    onBurstConfigChanged: (MacroBurstConfig) -> Unit,
    onStartBurstCapture: () -> Unit,
    onSurfaceCreated: (android.view.Surface) -> Unit,
    modifier: Modifier = Modifier
) {
    Box(
        modifier = modifier
            .fillMaxSize()
            .background(BgDark)
    ) {
        // 1. Camera Live Surface & Peaking Overlay
        AndroidView(
            factory = { context ->
                SurfaceView(context).apply {
                    holder.addCallback(object : android.view.SurfaceHolder.Callback {
                        override fun surfaceCreated(holder: android.view.SurfaceHolder) {
                            onSurfaceCreated(holder.surface)
                        }
                        override fun surfaceChanged(holder: android.view.SurfaceHolder, format: Int, width: Int, height: Int) {}
                        override fun surfaceDestroyed(holder: android.view.SurfaceHolder) {}
                    })
                }
            },
            modifier = Modifier.fillMaxSize()
        )

        // Peaking Neon Overlay Stream
        if (isPeakingEnabled && peakingBitmap != null) {
            Image(
                bitmap = peakingBitmap.asImageBitmap(),
                contentDescription = "Focus Peaking Live Stream",
                modifier = Modifier.fillMaxSize()
            )
        }

        // 2. Top Header HUD
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .statusBarsPadding()
                .padding(16.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            // App Title & Mode
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(
                    modifier = Modifier
                        .size(28.dp)
                        .clip(RoundedCornerShape(6.dp))
                        .background(PrimaryNeonGreen),
                    contentAlignment = Alignment.Center
                ) {
                    Text("⚡", fontSize = 14.sp)
                }
                Spacer(modifier = Modifier.width(8.dp))
                Column {
                    Text("FImageStack", fontWeight = FontWeight.Bold, fontSize = 15.sp, color = TextPrimary)
                    Text("PRO MACRO 1:1", fontSize = 10.sp, fontWeight = FontWeight.Bold, color = PrimaryCyan)
                }
            }

            // Top Floating Peaking HUD
            PeakingHudControls(
                isPeakingEnabled = isPeakingEnabled,
                selectedColor = peakingColor,
                isMonochromeMode = isMonochromeMode,
                onTogglePeaking = onTogglePeaking,
                onColorSelected = onColorSelected,
                onToggleMono = onToggleMono
            )
        }

        // 3. Bottom Controls HUD
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .align(Alignment.BottomCenter)
                .navigationBarsPadding()
                .padding(16.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            // Focus Bracketing Sliders
            FocusRangeSlider(
                startDiopters = burstConfig.startDistanceDiopters,
                endDiopters = burstConfig.endDistanceDiopters,
                steps = burstConfig.steps,
                onStartChanged = { onBurstConfigChanged(burstConfig.copy(startDistanceDiopters = it)) },
                onEndChanged = { onBurstConfigChanged(burstConfig.copy(endDistanceDiopters = it)) },
                onStepsChanged = { onBurstConfigChanged(burstConfig.copy(steps = it)) }
            )

            Spacer(modifier = Modifier.height(20.dp))

            // Shutter Button Row
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceEvenly,
                verticalAlignment = Alignment.CenterVertically
            ) {
                // Secondary Presets button
                IconButton(
                    onClick = { /* Open Macro Settings */ },
                    modifier = Modifier
                        .size(48.dp)
                        .clip(CircleShape)
                        .background(BgPanel)
                        .border(1.dp, BorderDefault, CircleShape)
                ) {
                    Icon(Icons.Default.Settings, contentDescription = "Settings", tint = TextSecondary)
                }

                // Main 1-Tap Shutter Burst Trigger
                Box(
                    modifier = Modifier
                        .size(80.dp)
                        .clip(CircleShape)
                        .background(if (isCapturing) AccentRed else PrimaryNeonGreen)
                        .clickable(enabled = !isCapturing) { onStartBurstCapture() }
                        .padding(6.dp),
                    contentAlignment = Alignment.Center
                ) {
                    Box(
                        modifier = Modifier
                            .fillMaxSize()
                            .clip(CircleShape)
                            .border(2.dp, BgDark, CircleShape)
                            .background(if (isCapturing) AccentRed.copy(alpha = 0.8f) else Color.White.copy(alpha = 0.9f)),
                        contentAlignment = Alignment.Center
                    ) {
                        if (isCapturing) {
                            Text(
                                text = "$capturedCount/${burstConfig.steps}",
                                fontWeight = FontWeight.Bold,
                                fontSize = 14.sp,
                                color = Color.White
                            )
                        } else {
                            Icon(
                                imageVector = Icons.Default.Camera,
                                contentDescription = "Capture Burst",
                                tint = BgDark,
                                modifier = Modifier.size(32.dp)
                            )
                        }
                    }
                }

                // Placeholder Balance Spacer
                Spacer(modifier = Modifier.size(48.dp))
            }
        }
    }
}
