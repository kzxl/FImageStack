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
                    val screenState by viewModel.screenState.collectAsState()

                    when (screenState) {
                        AppScreenState.CameraCapture -> {
                            val peakingBitmap by viewModel.peakingBitmap.collectAsState()
                            val isPeakingEnabled by viewModel.isPeakingEnabled.collectAsState()
                            val peakingColor by viewModel.peakingColor.collectAsState()
                            val isMonochromeMode by viewModel.isMonochromeMode.collectAsState()
                            val burstConfig by viewModel.burstConfig.collectAsState()
                            val isCapturing by viewModel.cameraController.isCapturing.collectAsState()
                            val capturedCount by viewModel.cameraController.capturedCount.collectAsState()

                            CameraCaptureScreen(
                                peakingBitmap = peakingBitmap,
                                isPeakingEnabled = isPeakingEnabled,
                                peakingColor = peakingColor,
                                isMonochromeMode = isMonochromeMode,
                                burstConfig = burstConfig,
                                isCapturing = isCapturing,
                                capturedCount = capturedCount,
                                onTogglePeaking = { viewModel.togglePeaking() },
                                onColorSelected = { viewModel.setPeakingColor(it) },
                                onToggleMono = { viewModel.toggleMonochromeMode() },
                                onBurstConfigChanged = { viewModel.setBurstConfig(it) },
                                onStartBurstCapture = { viewModel.startBurstCapture() },
                                onSurfaceCreated = { viewModel.onSurfaceCreated(it) }
                            )
                        }

                        AppScreenState.Processing -> {
                            val currentStage by viewModel.currentStage.collectAsState()
                            val stageDesc by viewModel.stageDescription.collectAsState()
                            val progress by viewModel.progressPercentage.collectAsState()
                            val activeCount by viewModel.activeFramesCount.collectAsState()
                            val culledCount by viewModel.culledFramesCount.collectAsState()

                            ProcessingScreen(
                                currentStage = currentStage,
                                stageDescription = stageDesc,
                                progressPercentage = progress,
                                activeFramesCount = activeCount,
                                culledFramesCount = culledCount
                            )
                        }

                        AppScreenState.ResultViewer -> {
                            val fusedBitmap = viewModel.fusedBitmap.collectAsState().value
                            val rawBitmap = viewModel.rawFirstSliceBitmap.collectAsState().value
                            val depthBitmap = viewModel.depthMapBitmap.collectAsState().value
                            val dofPreserved by viewModel.dofPreserved.collectAsState()
                            val execTime by viewModel.executionTimeMs.collectAsState()

                            if (fusedBitmap != null && rawBitmap != null) {
                                ResultViewerScreen(
                                    fusedBitmap = fusedBitmap,
                                    rawFirstSliceBitmap = rawBitmap,
                                    depthMapBitmap = depthBitmap,
                                    dofPreservedPercentage = dofPreserved,
                                    executionTimeMs = execTime,
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
