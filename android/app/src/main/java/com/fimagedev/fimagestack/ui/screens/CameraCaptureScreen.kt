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
import androidx.compose.material.icons.filled.*
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
import com.fimagedev.fimagestack.camera.FocusMode
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
    focusMode: FocusMode,
    liveManualDiopters: Float,
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
    onFocusModeChanged: (FocusMode) -> Unit,
    onLiveDioptersChanged: (Float) -> Unit,
    onSetNearPoint: () -> Unit,
    onSetFarPoint: () -> Unit,
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

    // Accurate Portrait Camera Sensor Aspect Ratio (Zero stretching!)
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
        // 1. Camera Viewfinder Canvas with 1:1 Aspect Ratio (No Stretching)
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

            // Focus Peaking Neon Overlay Stream (Mapped 1:1)
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

            // Sub-Toolbar: Mode Tabs (Stack vs Mosaic) + Peaking Palette
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 16.dp, vertical = 2.dp),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                // Mode Switcher Tabs
                Row(
                    modifier = Modifier
                        .clip(RoundedCornerShape(20.dp))
                        .background(BgPanel.copy(alpha = 0.85f))
                        .border(1.dp, BorderDefault, RoundedCornerShape(20.dp))
                        .padding(2.dp)
                ) {
                    Box(
                        modifier = Modifier
                            .clip(RoundedCornerShape(16.dp))
                            .background(if (!isMosaicMode) PrimaryNeonGreen else Color.Transparent)
                            .clickable { if (isMosaicMode) onToggleMosaicMode() }
                            .padding(horizontal = 10.dp, vertical = 4.dp),
                        contentAlignment = Alignment.Center
                    ) {
                        Text(
                            text = "⚡ STACK",
                            fontSize = 10.sp,
                            fontWeight = FontWeight.Bold,
                            color = if (!isMosaicMode) BgDark else TextSecondary
                        )
                    }

                    Box(
                        modifier = Modifier
                            .clip(RoundedCornerShape(16.dp))
                            .background(if (isMosaicMode) PrimaryCyan else Color.Transparent)
                            .clickable { if (!isMosaicMode) onToggleMosaicMode() }
                            .padding(horizontal = 10.dp, vertical = 4.dp),
                        contentAlignment = Alignment.Center
                    ) {
                        Text(
                            text = "🔲 MOSAIC",
                            fontSize = 10.sp,
                            fontWeight = FontWeight.Bold,
                            color = if (isMosaicMode) BgDark else TextSecondary
                        )
                    }
                }

                // Neon Peaking HUD Controls
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

        // Exposure Compensation (EV) Slider floating on Right
        ExposureSlider(
            currentEv = currentEv,
            evStepRational = evStepRational,
            minEv = minEv,
            maxEv = maxEv,
            onEvChanged = onEvChanged,
            modifier = Modifier
                .align(Alignment.TopEnd)
                .padding(top = 110.dp, end = 16.dp)
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

        // 4. Bottom Pro Deck (Focus Calibration Dial + Shutter Row)
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .align(Alignment.BottomCenter)
                .navigationBarsPadding()
                .padding(horizontal = 16.dp, vertical = 8.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            // Pro Focus Calibration & Diopter Dial
            FocusCalibrationDeck(
                focusMode = focusMode,
                liveDiopters = liveManualDiopters,
                startDiopters = burstConfig.startDistanceDiopters,
                endDiopters = burstConfig.endDistanceDiopters,
                steps = burstConfig.steps,
                onFocusModeChanged = onFocusModeChanged,
                onLiveDioptersChanged = onLiveDioptersChanged,
                onSetNearPoint = onSetNearPoint,
                onSetFarPoint = onSetFarPoint,
                onStepsChanged = { onBurstConfigChanged(burstConfig.copy(steps = it)) }
            )

            Spacer(modifier = Modifier.height(10.dp))

            // Shutter Row (Gallery Thumbnail | Shutter Button | Lens Switcher)
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                // Left: Gallery Thumbnail of Latest Result
                Box(
                    modifier = Modifier
                        .size(50.dp)
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
                        .size(76.dp)
                        .clip(CircleShape)
                        .background(if (isCapturing) AccentRed else if (isMosaicMode) PrimaryCyan else PrimaryNeonGreen)
                        .clickable(enabled = !isCapturing) { onStartBurstCapture() },
                    contentAlignment = Alignment.Center
                ) {
                    Box(
                        modifier = Modifier
                            .size(64.dp)
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
                                    .size(50.dp)
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

                // Right: Quick Lens Switcher (0.6x | 1x | 2x)
                LensSelectorRow(
                    lenses = availableLenses,
                    selectedLensId = selectedLensId,
                    onLensSelected = onLensSelected
                )
            }
        }
    }
}
