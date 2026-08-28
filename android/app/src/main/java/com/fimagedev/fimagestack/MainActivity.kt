package com.fimagedev.fimagestack

import android.Manifest
import android.content.pm.PackageManager
import android.os.Bundle
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.viewModels
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Surface
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.core.content.ContextCompat
import com.fimagedev.fimagestack.ui.screens.CameraCaptureScreen
import com.fimagedev.fimagestack.ui.screens.ProcessingScreen
import com.fimagedev.fimagestack.ui.screens.ResultViewerScreen
import com.fimagedev.fimagestack.ui.theme.BgDark
import com.fimagedev.fimagestack.ui.theme.FImageStackTheme
import com.fimagedev.fimagestack.viewmodel.AppScreenState
import com.fimagedev.fimagestack.viewmodel.CameraUiEffect
import com.fimagedev.fimagestack.viewmodel.MainCameraViewModel

class MainActivity : ComponentActivity() {

    private val viewModel: MainCameraViewModel by viewModels()

    private val cameraPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { isGranted ->
        if (!isGranted) {
            Toast.makeText(this, "Camera permission required for Macro Focus Stacking", Toast.LENGTH_LONG).show()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        if (ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
            cameraPermissionLauncher.launch(Manifest.permission.CAMERA)
        }

        setContent {
            FImageStackTheme {
                Surface(
                    modifier = Modifier.fillMaxSize(),
                    color = BgDark
                ) {
                    val state by viewModel.uiState.collectAsState()

                    // Handle One-Time Side Effects
                    LaunchedEffect(Unit) {
                        viewModel.effectFlow.collect { effect ->
                            when (effect) {
                                is CameraUiEffect.ShowToast -> {
                                    Toast.makeText(this@MainActivity, effect.message, Toast.LENGTH_SHORT).show()
                                }
                                is CameraUiEffect.TriggerHapticFeedback -> {
                                    window.decorView.performHapticFeedback(android.view.HapticFeedbackConstants.LONG_PRESS)
                                }
                            }
                        }
                    }

                    when (state.screenState) {
                        AppScreenState.CameraCapture -> {
                            CameraCaptureScreen(
                                peakingBitmap = state.peakingBitmap,
                                isPeakingEnabled = state.isPeakingEnabled,
                                peakingColor = state.peakingColor,
                                isMonochromeMode = state.isMonochromeMode,
                                burstConfig = state.burstConfig,
                                isCapturing = state.isCapturing,
                                capturedCount = state.capturedCount,
                                flashMode = state.flashMode,
                                timerSeconds = state.timerSeconds,
                                countdownRemaining = state.countdownRemaining,
                                isGridEnabled = state.isGridEnabled,
                                aspectRatio = state.aspectRatio,
                                availableLenses = state.availableLenses,
                                selectedLensId = state.selectedLensId,
                                currentEv = state.currentEv,
                                evStepRational = state.evStepRational,
                                minEv = state.minEv,
                                maxEv = state.maxEv,
                                tapFocusPoint = state.tapFocusPoint,
                                latestThumbnail = state.latestThumbnail,
                                onToggleFlash = { viewModel.toggleFlash() },
                                onToggleTimer = { viewModel.toggleTimer() },
                                onToggleGrid = { viewModel.toggleGrid() },
                                onToggleAspectRatio = { viewModel.toggleAspectRatio() },
                                onEvChanged = { viewModel.setExposure(it) },
                                onLensSelected = { viewModel.switchLens(it) },
                                onTapFocus = { offset, w, h -> viewModel.onTapToFocus(offset, w, h) },
                                onTogglePeaking = { viewModel.togglePeaking() },
                                onColorSelected = { viewModel.setPeakingColor(it) },
                                onToggleMono = { viewModel.toggleMonochromeMode() },
                                onBurstConfigChanged = { viewModel.setBurstConfig(it) },
                                onStartBurstCapture = { viewModel.startBurstCapture() },
                                onOpenLatestResult = { viewModel.openLatestResult() },
                                onSurfaceCreated = { viewModel.onSurfaceCreated(it) }
                            )
                        }

                        AppScreenState.Processing -> {
                            ProcessingScreen(
                                currentStage = state.currentStage,
                                stageDescription = state.stageDescription,
                                progressPercentage = state.progressPercentage,
                                activeFramesCount = state.activeFramesCount,
                                culledFramesCount = state.culledFramesCount
                            )
                        }

                        AppScreenState.ResultViewer -> {
                            val fusedBitmap = state.fusedBitmap
                            val rawBitmap = state.rawFirstSliceBitmap

                            if (fusedBitmap != null && rawBitmap != null) {
                                ResultViewerScreen(
                                    fusedBitmap = fusedBitmap,
                                    rawFirstSliceBitmap = rawBitmap,
                                    depthMapBitmap = state.depthMapBitmap,
                                    dofPreservedPercentage = state.dofPreserved,
                                    executionTimeMs = state.executionTimeMs,
                                    onBackToCamera = { viewModel.backToCamera() },
                                    onExportResult = {
                                        Toast.makeText(this, "Master image exported to Gallery!", Toast.LENGTH_SHORT).show()
                                    }
                                )
                            }
                        }
                    }
                }
            }
        }
    }
}
