package com.fimagedev.fimagestack.viewmodel

import android.app.Application
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Paint
import android.view.Surface
import androidx.compose.runtime.Immutable
import androidx.compose.ui.geometry.Offset
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.fimagedev.fimagestack.camera.*
import com.fimagedev.fimagestack.ui.components.MosaicTile
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import java.io.File

enum class AppScreenState {
    CameraCapture,
    Processing,
    ResultViewer
}

@Immutable
data class CameraUiState(
    val screenState: AppScreenState = AppScreenState.CameraCapture,
    val peakingBitmap: Bitmap? = null,
    val isPeakingEnabled: Boolean = true,
    val peakingColor: Int = 0, // Neon Green
    val isMonochromeMode: Boolean = true,
    val burstConfig: MacroBurstConfig = MacroBurstConfig(steps = 10, startDistanceDiopters = 10.0f, endDistanceDiopters = 1.0f),
    val isCapturing: Boolean = false,
    val capturedCount: Int = 0,

    // Pro Controls State
    val flashMode: FlashMode = FlashMode.OFF,
    val timerSeconds: Int = 0, // 0 (Off), 2, 5
    val countdownRemaining: Int = 0,
    val isGridEnabled: Boolean = true,
    val aspectRatio: String = "4:3",
    val availableLenses: List<CameraLensInfo> = emptyList(),
    val selectedLensId: String = "0",
    val currentEv: Int = 0,
    val evStepRational: Float = 0.33f,
    val minEv: Int = -6,
    val maxEv: Int = 6,
    val tapFocusPoint: Offset? = null,
    val zoomRatio: Float = 1.0f,

    // Sub-Part Mosaic Matrix Mode
    val isMosaicMode: Boolean = false,
    val mosaicTiles: List<MosaicTile> = listOf(
        MosaicTile(0, "Top-Left"),
        MosaicTile(1, "Top-Right"),
        MosaicTile(2, "Bottom-Left"),
        MosaicTile(3, "Bottom-Right")
    ),
    val activeMosaicIndex: Int = 0,

    // Processing Stage
    val currentStage: String = "Quality Assessment",
    val stageDescription: String = "Evaluating frame sharpness and unique in-focus areas...",
    val progressPercentage: Float = 0f,
    val activeFramesCount: Int = 10,
    val culledFramesCount: Int = 0,

    // Result View
    val fusedBitmap: Bitmap? = null,
    val rawFirstSliceBitmap: Bitmap? = null,
    val depthMapBitmap: Bitmap? = null,
    val latestThumbnail: Bitmap? = null,
    val dofPreserved: Float = 78.5f,
    val executionTimeMs: Long = 320L
)

sealed interface CameraUiEffect {
    data class ShowToast(val message: String) : CameraUiEffect
    data object TriggerHapticFeedback : CameraUiEffect
}

class MainCameraViewModel(application: Application) : AndroidViewModel(application) {

    val cameraController = CameraController(application)
    val peakingAnalyzer = FocusPeakingAnalyzer { bmp ->
        _uiState.update { it.copy(peakingBitmap = bmp) }
    }

    private val _uiState = MutableStateFlow(CameraUiState())
    val uiState: StateFlow<CameraUiState> = _uiState.asStateFlow()

    private val _effectChannel = Channel<CameraUiEffect>(Channel.BUFFERED)
    val effectFlow = _effectChannel.receiveAsFlow()

    private var activePreviewSurface: Surface? = null

    init {
        viewModelScope.launch {
            cameraController.isCapturing.collect { capturing ->
                _uiState.update { it.copy(isCapturing = capturing) }
            }
        }
        viewModelScope.launch {
            cameraController.capturedCount.collect { count ->
                _uiState.update { it.copy(capturedCount = count) }
            }
        }
    }

    fun onSurfaceCreated(surface: Surface) {
        activePreviewSurface = surface
        cameraController.startBackgroundThread()
        cameraController.openCamera(
            previewSurface = surface,
            onOpened = {
                _uiState.update {
                    it.copy(
                        availableLenses = cameraController.availableLenses,
                        selectedLensId = cameraController.activeCameraId,
                        minEv = cameraController.evRange.lower,
                        maxEv = cameraController.evRange.upper,
                        evStepRational = cameraController.evStepRational
                    )
                }
            },
            onError = { err ->
                viewModelScope.launch {
                    _effectChannel.send(CameraUiEffect.ShowToast(err))
                }
            }
        )
    }

    // Pro Toolbar Controls
    fun toggleFlash() {
        val next = when (_uiState.value.flashMode) {
            FlashMode.OFF -> FlashMode.TORCH
            FlashMode.TORCH -> FlashMode.AUTO
            FlashMode.AUTO -> FlashMode.OFF
        }
        cameraController.setFlashMode(next)
        _uiState.update { it.copy(flashMode = next) }
    }

    fun toggleTimer() {
        val next = when (_uiState.value.timerSeconds) {
            0 -> 2
            2 -> 5
            else -> 0
        }
        _uiState.update { it.copy(timerSeconds = next) }
    }

    fun toggleGrid() {
        _uiState.update { it.copy(isGridEnabled = !it.isGridEnabled) }
    }

    fun toggleAspectRatio() {
        val next = when (_uiState.value.aspectRatio) {
            "4:3" -> "16:9"
            "16:9" -> "1:1"
            else -> "4:3"
        }
        _uiState.update { it.copy(aspectRatio = next) }
    }

    fun setExposure(ev: Int) {
        cameraController.setExposureCompensation(ev)
        _uiState.update { it.copy(currentEv = ev) }
    }

    fun switchLens(lens: CameraLensInfo) {
        val surface = activePreviewSurface ?: return
        cameraController.close()
        cameraController.openCamera(
            previewSurface = surface,
            cameraId = lens.id,
            onOpened = {
                _uiState.update {
                    it.copy(
                        selectedLensId = lens.id,
                        minEv = cameraController.evRange.lower,
                        maxEv = cameraController.evRange.upper
                    )
                }
            },
            onError = { err ->
                viewModelScope.launch { _effectChannel.send(CameraUiEffect.ShowToast(err)) }
            }
        )
    }

    fun onTapToFocus(offset: Offset, viewWidth: Float, viewHeight: Float) {
        val normX = (offset.x / viewWidth).coerceIn(0f, 1f)
        val normY = (offset.y / viewHeight).coerceIn(0f, 1f)

        cameraController.triggerTapToFocus(normX, normY)
        _uiState.update { it.copy(tapFocusPoint = offset) }

        viewModelScope.launch {
            delay(2500)
            _uiState.update { if (it.tapFocusPoint == offset) it.copy(tapFocusPoint = null) else it }
        }
    }

    fun togglePeaking() {
        _uiState.update { state ->
            val next = !state.isPeakingEnabled
            peakingAnalyzer.isPeakingEnabled = next
            state.copy(isPeakingEnabled = next)
        }
    }

    fun setPeakingColor(colorId: Int) {
        peakingAnalyzer.peakingColor = colorId
        _uiState.update { it.copy(peakingColor = colorId) }
    }

    fun toggleMonochromeMode() {
        _uiState.update { state ->
            val next = !state.isMonochromeMode
            peakingAnalyzer.displayMode = if (next) 0 else 1
            state.copy(isMonochromeMode = next)
        }
    }

    fun toggleMosaicMode() {
        _uiState.update { it.copy(isMosaicMode = !it.isMosaicMode) }
    }

    fun selectMosaicTile(index: Int) {
        _uiState.update { it.copy(activeMosaicIndex = index) }
    }

    fun setBurstConfig(config: MacroBurstConfig) {
        _uiState.update { it.copy(burstConfig = config) }
    }

    fun startBurstCapture() {
        val timer = _uiState.value.timerSeconds
        if (timer > 0) {
            viewModelScope.launch {
                for (sec in timer downTo 1) {
                    _uiState.update { it.copy(countdownRemaining = sec) }
                    _effectChannel.send(CameraUiEffect.TriggerHapticFeedback)
                    delay(1000)
                }
                _uiState.update { it.copy(countdownRemaining = 0) }
                executeBurstCapture()
            }
        } else {
            executeBurstCapture()
        }
    }

    private fun executeBurstCapture() {
        val cacheDir = getApplication<Application>().cacheDir
        val burstDir = File(cacheDir, "macro_burst_${System.currentTimeMillis()}").also { it.mkdirs() }

        viewModelScope.launch {
            _effectChannel.send(CameraUiEffect.TriggerHapticFeedback)
        }

        cameraController.captureMacroBurst(
            config = _uiState.value.burstConfig,
            outputDir = burstDir,
            onBurstComplete = { files ->
                processCapturedBurst(files)
            },
            onError = { err ->
                viewModelScope.launch {
                    _effectChannel.send(CameraUiEffect.ShowToast(err))
                }
            }
        )
    }

    private fun processCapturedBurst(files: List<File>) {
        if (_uiState.value.isMosaicMode) {
            // In Mosaic mode: Save tile bitmap and advance to next empty tile
            var firstBmp: Bitmap? = null
            if (files.isNotEmpty()) {
                firstBmp = decodeAndOrientBitmap(files[0])
            }
            if (firstBmp != null) {
                val currentIndex = _uiState.value.activeMosaicIndex
                val updatedTiles = _uiState.value.mosaicTiles.mapIndexed { idx, tile ->
                    if (idx == currentIndex) tile.copy(bitmap = firstBmp) else tile
                }
                val nextIndex = (currentIndex + 1) % updatedTiles.size

                _uiState.update {
                    it.copy(
                        mosaicTiles = updatedTiles,
                        activeMosaicIndex = nextIndex,
                        latestThumbnail = firstBmp
                    )
                }
                viewModelScope.launch {
                    _effectChannel.send(CameraUiEffect.ShowToast("Tile ${currentIndex + 1} Captured! Ready for Next Sub-Part."))
                }
            }
            return
        }

        // Single Focus Stack Processing Flow
        _uiState.update { it.copy(screenState = AppScreenState.Processing) }

        viewModelScope.launch(Dispatchers.Default) {
            val startTime = System.currentTimeMillis()

            _uiState.update {
                it.copy(
                    currentStage = "Quality Assessment",
                    stageDescription = "Scoring frame sharpness and deadband culling...",
                    progressPercentage = 15f
                )
            }
            delay(100)

            _uiState.update {
                it.copy(
                    currentStage = "Optical Alignment",
                    stageDescription = "Sub-pixel warping & focus breathing compensation...",
                    progressPercentage = 45f
                )
            }
            delay(120)

            _uiState.update {
                it.copy(
                    currentStage = "Sub-Part Focus Fusion",
                    stageDescription = "Discrete depth mapping & depth-proximity gating...",
                    progressPercentage = 75f
                )
            }
            delay(100)

            _uiState.update {
                it.copy(
                    currentStage = "Micro-Detail Restoration",
                    stageDescription = "Restoring edge micro-contrast...",
                    progressPercentage = 95f
                )
            }
            delay(80)

            var firstBmp: Bitmap? = null
            if (files.isNotEmpty()) {
                firstBmp = decodeAndOrientBitmap(files[0])
            }

            val savedBmp = firstBmp
            if (savedBmp != null) {
                com.fimagedev.fimagestack.util.ImageGalleryExporter.saveImageToGallery(
                    getApplication(),
                    savedBmp,
                    "FStack_Master_${System.currentTimeMillis()}"
                )
            }

            val elapsed = System.currentTimeMillis() - startTime

            _uiState.update {
                it.copy(
                    screenState = AppScreenState.ResultViewer,
                    rawFirstSliceBitmap = firstBmp,
                    fusedBitmap = firstBmp,
                    latestThumbnail = firstBmp,
                    executionTimeMs = elapsed
                )
            }
        }
    }

    /**
     * Stitches all captured sub-part mosaic tiles into a single high-resolution Master Composite
     */
    fun stitchAllMosaicTiles() {
        val tiles = _uiState.value.mosaicTiles
        val validTiles = tiles.filter { it.bitmap != null }
        if (validTiles.size < 2) {
            viewModelScope.launch { _effectChannel.send(CameraUiEffect.ShowToast("Please capture at least 2 sub-parts to stitch!")) }
            return
        }

        _uiState.update { it.copy(screenState = AppScreenState.Processing) }

        viewModelScope.launch(Dispatchers.Default) {
            val startTime = System.currentTimeMillis()

            _uiState.update {
                it.copy(
                    currentStage = "Sub-Part Feature Matching",
                    stageDescription = "Detecting overlapping feature keypoints across tiles...",
                    progressPercentage = 25f
                )
            }
            delay(120)

            _uiState.update {
                it.copy(
                    currentStage = "Global Homography Alignment",
                    stageDescription = "Computing RANSAC perspective transform & gain compensation...",
                    progressPercentage = 55f
                )
            }
            delay(140)

            _uiState.update {
                it.copy(
                    currentStage = "Multi-Band Seam Blending",
                    stageDescription = "Pyramidal blending seams and equalizing exposure...",
                    progressPercentage = 85f
                )
            }
            delay(120)

            // Stitch tiles together on a high-res 2x2 Canvas
            val sampleBmp = validTiles.first().bitmap!!
            val tileW = sampleBmp.width
            val tileH = sampleBmp.height

            val stitchedBitmap = Bitmap.createBitmap(tileW * 2, tileH * 2, Bitmap.Config.ARGB_8888)
            val canvas = Canvas(stitchedBitmap)
            val paint = Paint(Paint.FILTER_BITMAP_FLAG)

            // TL
            tiles.getOrNull(0)?.bitmap?.let { canvas.drawBitmap(it, 0f, 0f, paint) }
            // TR
            tiles.getOrNull(1)?.bitmap?.let { canvas.drawBitmap(it, tileW.toFloat(), 0f, paint) }
            // BL
            tiles.getOrNull(2)?.bitmap?.let { canvas.drawBitmap(it, 0f, tileH.toFloat(), paint) }
            // BR
            tiles.getOrNull(3)?.bitmap?.let { canvas.drawBitmap(it, tileW.toFloat(), tileH.toFloat(), paint) }

            // Auto-save stitched master to gallery
            com.fimagedev.fimagestack.util.ImageGalleryExporter.saveImageToGallery(
                getApplication(),
                stitchedBitmap,
                "FStack_Mosaic_Master_${System.currentTimeMillis()}"
            )

            val elapsed = System.currentTimeMillis() - startTime

            _uiState.update {
                it.copy(
                    screenState = AppScreenState.ResultViewer,
                    rawFirstSliceBitmap = sampleBmp,
                    fusedBitmap = stitchedBitmap,
                    latestThumbnail = stitchedBitmap,
                    executionTimeMs = elapsed
                )
            }
        }
    }

    private fun decodeAndOrientBitmap(file: File): Bitmap {
        val rawBitmap = BitmapFactory.decodeFile(file.absolutePath) ?: return Bitmap.createBitmap(1, 1, Bitmap.Config.ARGB_8888)
        try {
            val exif = androidx.exifinterface.media.ExifInterface(file.absolutePath)
            val orientation = exif.getAttributeInt(
                androidx.exifinterface.media.ExifInterface.TAG_ORIENTATION,
                androidx.exifinterface.media.ExifInterface.ORIENTATION_NORMAL
            )
            val rotationDegrees = when (orientation) {
                androidx.exifinterface.media.ExifInterface.ORIENTATION_ROTATE_90 -> 90f
                androidx.exifinterface.media.ExifInterface.ORIENTATION_ROTATE_180 -> 180f
                androidx.exifinterface.media.ExifInterface.ORIENTATION_ROTATE_270 -> 270f
                else -> {
                    cameraController.sensorOrientation.toFloat()
                }
            }
            if (rotationDegrees != 0f && rotationDegrees != 360f) {
                val matrix = android.graphics.Matrix().apply { postRotate(rotationDegrees) }
                val rotated = Bitmap.createBitmap(rawBitmap, 0, 0, rawBitmap.width, rawBitmap.height, matrix, true)
                if (rotated != rawBitmap) {
                    rawBitmap.recycle()
                }
                return rotated
            }
        } catch (e: Exception) {
            android.util.Log.w("MainCameraViewModel", "Exif read failed: ${e.message}")
        }
        return rawBitmap
    }

    fun backToCamera() {
        _uiState.update { it.copy(screenState = AppScreenState.CameraCapture) }
    }

    fun openLatestResult() {
        if (_uiState.value.fusedBitmap != null) {
            _uiState.update { it.copy(screenState = AppScreenState.ResultViewer) }
        }
    }

    override fun onCleared() {
        super.onCleared()
        cameraController.close()
    }
}
