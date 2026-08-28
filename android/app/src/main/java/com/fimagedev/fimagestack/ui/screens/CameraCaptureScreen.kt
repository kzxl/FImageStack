package com.fimagedev.fimagestack.ui.screens

import android.graphics.Bitmap
import android.view.SurfaceView
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Camera
import androidx.compose.material.icons.filled.GridView
import androidx.compose.material.icons.filled.Layers
import androidx.compose.material.icons.filled.Visibility
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import com.fimagedev.fimagestack.camera.CameraLensInfo
import com.fimagedev.fimagestack.camera.FlashMode
import com.fimagedev.fimagestack.camera.MacroBurstConfig
import com.fimagedev.fimagestack.ui.components.*
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
    flashMode: FlashMode,
    timerSeconds: Int,
    countdownRemaining: Int,
    isGridEnabled: Boolean,
    aspectRatio: String,
    availableLenses: List<CameraLensInfo>,
    selectedLensId: String,
    currentEv: Int,
    evStepRational: Float,
    minEv: Int,
    maxEv: Int,
    tapFocusPoint: Offset?,
    latestThumbnail: Bitmap?,
    isMosaicMode: Boolean,
    mosaicTiles: List<MosaicTile>,
    activeMosaicIndex: Int,
    onToggleFlash: () -> Unit,
    onToggleTimer: () -> Unit,
    onToggleGrid: () -> Unit,
    onToggleAspectRatio: () -> Unit,
    onEvChanged: (Int) -> Unit,
    onLensSelected: (CameraLensInfo) -> Unit,
    onTapFocus: (Offset, Float, Float) -> Unit,
    onTogglePeaking: () -> Unit,
    onColorSelected: (Int) -> Unit,
    onToggleMono: () -> Unit,
    onToggleMosaicMode: () -> Unit,
    onSelectMosaicTile: (Int) -> Unit,
    onStitchAllMosaicTiles: () -> Unit,
    onBurstConfigChanged: (MacroBurstConfig) -> Unit,
    onStartBurstCapture: () -> Unit,
    onOpenLatestResult: () -> Unit,
    onSurfaceCreated: (android.view.Surface) -> Unit,
    modifier: Modifier = Modifier
) {
    var viewWidth by remember { mutableFloatStateOf(1080f) }
    var viewHeight by remember { mutableFloatStateOf(1920f) }

    // Accurate Portrait Camera Sensor Aspect Ratios (Eliminates live view stretching!)
    val targetAspectRatio = when (aspectRatio) {
        "16:9" -> 9f / 16f
        "1:1" -> 1f
        else -> 3f / 4f // 4:3 native portrait sensor ratio (0.75)
    }

    Box(
        modifier = modifier
            .fillMaxSize()
            .background(BgDark)
    ) {
        // 1. Camera Viewfinder Frame with TRUE 1:1 Aspect Ratio (No Stretching)
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .aspectRatio(targetAspectRatio)
                .align(Alignment.Center)
                .clipToBounds()
                .onGloballyPositioned {
                    viewWidth = it.size.width.toFloat()
                    viewHeight = it.size.height.toFloat()
                }
                .pointerInput(Unit) {
                    detectTapGestures { offset ->
                        onTapFocus(offset, viewWidth, viewHeight)
                    }
                }
        ) {
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

            // Focus Peaking Neon Overlay Stream (Mapped 1:1 to sensor aspect)
            if (isPeakingEnabled && peakingBitmap != null) {
                Image(
                    bitmap = peakingBitmap.asImageBitmap(),
                    contentDescription = "Focus Peaking Live Stream",
                    contentScale = ContentScale.Fit,
                    modifier = Modifier.fillMaxSize()
                )
            }

            // 3x3 Rule-of-Thirds Grid Overlay
            if (isGridEnabled) {
                CameraGridOverlay(modifier = Modifier.fillMaxSize())
            }

            // Animated Tap-to-Focus Reticle
            TapFocusReticle(tapPoint = tapFocusPoint, modifier = Modifier.fillMaxSize())
        }

        // 2. Top Header & Toolbar HUD
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .align(Alignment.TopCenter)
        ) {
            CameraTopToolbar(
                flashMode = flashMode,
                timerSeconds = timerSeconds,
                isGridEnabled = isGridEnabled,
                aspectRatio = aspectRatio,
                onToggleFlash = onToggleFlash,
                onToggleTimer = onToggleTimer,
                onToggleGrid = onToggleGrid,
                onToggleAspectRatio = onToggleAspectRatio
            )

            // Sub-Toolbar: Lens Switcher + Mosaic Mode + Peaking HUD
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 16.dp, vertical = 4.dp),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                    // Lens selector (0.6x, 1x, 2x)
                    LensSelectorRow(
                        lenses = availableLenses,
                        selectedLensId = selectedLensId,
                        onLensSelected = onLensSelected
                    )

                    // Sub-Part Mosaic Mode Toggle Button
                    IconButton(
                        onClick = onToggleMosaicMode,
                        modifier = Modifier
                            .size(36.dp)
                            .clip(CircleShape)
                            .background(if (isMosaicMode) PrimaryCyan else BgPanel.copy(alpha = 0.85f))
                            .border(1.dp, BorderDefault, CircleShape)
                    ) {
                        Icon(
                            imageVector = Icons.Default.GridView,
                            contentDescription = "Mosaic Matrix Mode",
                            tint = if (isMosaicMode) BgDark else TextPrimary,
                            modifier = Modifier.size(18.dp)
                        )
                    }
                }

                // Neon Peaking HUD
                PeakingHudControls(
                    isPeakingEnabled = isPeakingEnabled,
                    selectedColor = peakingColor,
                    isMonochromeMode = isMonochromeMode,
                    onTogglePeaking = onTogglePeaking,
                    onColorSelected = onColorSelected,
                    onToggleMono = onToggleMono
                )
            }
        }

        // Sub-Part Mosaic Matrix Mini HUD (when Mosaic mode is active)
        if (isMosaicMode) {
            SubPartMosaicHud(
                tiles = mosaicTiles,
                activeTileIndex = activeMosaicIndex,
                onTileSelected = onSelectMosaicTile,
                onStitchAllTiles = onStitchAllMosaicTiles,
                modifier = Modifier
                    .align(Alignment.CenterStart)
                    .padding(start = 16.dp)
            )
        }

        // Exposure Compensation (EV) Slider floating on Right/Top
        ExposureSlider(
            currentEv = currentEv,
            evStepRational = evStepRational,
            minEv = minEv,
            maxEv = maxEv,
            onEvChanged = onEvChanged,
            modifier = Modifier
                .align(Alignment.TopEnd)
                .padding(top = 115.dp, end = 16.dp)
        )

        // 3. Countdown Overlay
        if (countdownRemaining > 0) {
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .background(BgDark.copy(alpha = 0.4f)),
                contentAlignment = Alignment.Center
            ) {
                Text(
                    text = "$countdownRemaining",
                    fontSize = 96.sp,
                    fontWeight = FontWeight.Black,
                    color = PrimaryNeonGreen
                )
            }
        }

        // 4. Bottom Controls HUD
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .align(Alignment.BottomCenter)
                .navigationBarsPadding()
                .padding(16.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            // Focus Bracketing Diopter Stepping Slider
            FocusRangeSlider(
                startDiopters = burstConfig.startDistanceDiopters,
                endDiopters = burstConfig.endDistanceDiopters,
                steps = burstConfig.steps,
                onStartChanged = { onBurstConfigChanged(burstConfig.copy(startDistanceDiopters = it)) },
                onEndChanged = { onBurstConfigChanged(burstConfig.copy(endDistanceDiopters = it)) },
                onStepsChanged = { onBurstConfigChanged(burstConfig.copy(steps = it)) }
            )

            Spacer(modifier = Modifier.height(16.dp))

            // Shutter Button Row
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceEvenly,
                verticalAlignment = Alignment.CenterVertically
            ) {
                // Left: Gallery Thumbnail of Latest Result
                Box(
                    modifier = Modifier
                        .size(52.dp)
                        .clip(CircleShape)
                        .background(BgPanel)
                        .border(2.dp, if (latestThumbnail != null) PrimaryNeonGreen else BorderDefault, CircleShape)
                        .clickable { onOpenLatestResult() },
                    contentAlignment = Alignment.Center
                ) {
                    if (latestThumbnail != null) {
                        Image(
                            bitmap = latestThumbnail.asImageBitmap(),
                            contentDescription = "Latest Capture",
                            contentScale = ContentScale.Crop,
                            modifier = Modifier.fillMaxSize()
                        )
                    } else {
                        Icon(Icons.Default.Camera, contentDescription = "Gallery", tint = TextSecondary, modifier = Modifier.size(24.dp))
                    }
                }

                // Center: 1-Tap Pro Macro Shutter Button
                Box(
                    modifier = Modifier
                        .size(80.dp)
                        .clip(CircleShape)
                        .background(if (isCapturing) AccentRed else if (isMosaicMode) PrimaryCyan else PrimaryNeonGreen)
                        .clickable(enabled = !isCapturing) { onStartBurstCapture() },
                    contentAlignment = Alignment.Center
                ) {
                    Box(
                        modifier = Modifier
                            .size(68.dp)
                            .clip(CircleShape)
                            .background(BgDark),
                        contentAlignment = Alignment.Center
                    ) {
                        if (isCapturing) {
                            Text(
                                text = "$capturedCount/${burstConfig.steps}",
                                color = AccentRed,
                                fontWeight = FontWeight.Black,
                                fontSize = 13.sp
                            )
                        } else {
                            Box(
                                modifier = Modifier
                                    .size(54.dp)
                                    .clip(CircleShape)
                                    .background(if (isMosaicMode) PrimaryCyan else PrimaryNeonGreen),
                                contentAlignment = Alignment.Center
                            ) {
                                if (isMosaicMode) {
                                    Text(
                                        text = "${activeMosaicIndex + 1}",
                                        color = BgDark,
                                        fontWeight = FontWeight.Black,
                                        fontSize = 16.sp
                                    )
                                }
                            }
                        }
                    }
                }

                // Right: Quick Peaking Visibility Toggle
                IconButton(
                    onClick = onTogglePeaking,
                    modifier = Modifier
                        .size(52.dp)
                        .clip(CircleShape)
                        .background(if (isPeakingEnabled) PrimaryNeonGreen else BgPanel)
                        .border(1.dp, BorderDefault, CircleShape)
                ) {
                    Icon(
                        imageVector = Icons.Default.Visibility,
                        contentDescription = "Peaking Toggle",
                        tint = if (isPeakingEnabled) BgDark else TextPrimary,
                        modifier = Modifier.size(24.dp)
                    )
                }
            }
        }
    }
}
