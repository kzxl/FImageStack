package com.fimagedev.fimagestack.camera

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.ImageFormat
import android.graphics.Rect
import android.hardware.camera2.*
import android.hardware.camera2.params.MeteringRectangle
import android.media.ImageReader
import android.os.Build
import android.os.Handler
import android.os.HandlerThread
import android.util.Log
import android.util.Range
import android.view.Surface
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.io.File
import java.io.FileOutputStream
import java.nio.ByteBuffer

enum class FlashMode {
    OFF,
    TORCH,
    AUTO
}

enum class FocusMode {
    MANUAL,
    CONTINUOUS_AF,
    AF_LOCKED
}

data class CameraLensInfo(
    val id: String,
    val label: String,
    val isFront: Boolean,
    val focalLength: Float
)

data class MacroBurstConfig(
    val steps: Int = 10,
    val startDistanceDiopters: Float = 9.0f,
    val endDistanceDiopters: Float = 2.0f
)

class CameraController(private val context: Context) {

    private val cameraManager = context.getSystemService(Context.CAMERA_SERVICE) as CameraManager
    private var cameraDevice: CameraDevice? = null
    private var captureSession: CameraCaptureSession? = null
    private var burstImageReader: ImageReader? = null
    private var peakingImageReader: ImageReader? = null
    private var currentPreviewSurface: Surface? = null

    var peakingAnalyzer: FocusPeakingAnalyzer? = null

    private var backgroundThread: HandlerThread? = null
    private var backgroundHandler: Handler? = null

    private val _isCapturing = MutableStateFlow(false)
    val isCapturing = _isCapturing.asStateFlow()

    private val _capturedCount = MutableStateFlow(0)
    val capturedCount = _capturedCount.asStateFlow()

    var minFocusDistanceDiopters = 10.0f
    var sensorOrientation: Int = 90
    var activeCameraId: String = "0"

    // Focus & Pro Controls State
    var currentFocusMode: FocusMode = FocusMode.MANUAL
    var liveManualDiopters: Float = 8.0f

    var currentFlashMode: FlashMode = FlashMode.OFF
    var currentEvStep: Int = 0
    var evRange: Range<Int> = Range(-6, 6)
    var evStepRational: Float = 0.33f
    var currentZoomRatio: Float = 1.0f
    var maxZoomRatio: Float = 5.0f

    val availableLenses = mutableListOf<CameraLensInfo>()

    fun startBackgroundThread() {
        if (backgroundThread == null) {
            backgroundThread = HandlerThread("CameraBackground").also { it.start() }
            backgroundHandler = Handler(backgroundThread!!.looper)
        }
    }

    fun stopBackgroundThread() {
        backgroundThread?.quitSafely()
        try {
            backgroundThread?.join()
            backgroundThread = null
            backgroundHandler = null
        } catch (e: InterruptedException) {
            Log.e("CameraController", "Interrupted stopping thread: ${e.message}")
        }
    }

    init {
        detectAvailableLenses()
    }

    private fun detectAvailableLenses() {
        try {
            availableLenses.clear()
            for (id in cameraManager.cameraIdList) {
                val chars = cameraManager.getCameraCharacteristics(id)
                val facing = chars.get(CameraCharacteristics.LENS_FACING)
                val focalLengths = chars.get(CameraCharacteristics.LENS_INFO_AVAILABLE_FOCAL_LENGTHS)
                val focal = focalLengths?.firstOrNull() ?: 4.5f

                val isFront = facing == CameraCharacteristics.LENS_FACING_FRONT
                val label = if (isFront) "FRONT" else {
                    when {
                        focal < 3.0f -> "0.6x"
                        focal > 6.0f -> "2.0x"
                        else -> "1.0x"
                    }
                }
                availableLenses.add(CameraLensInfo(id, label, isFront, focal))
            }
        } catch (e: Exception) {
            Log.w("CameraController", "Error detecting lenses: ${e.message}")
        }
    }

    @SuppressLint("MissingPermission")
    fun openCamera(
        previewSurface: Surface,
        cameraId: String? = null,
        onOpened: () -> Unit,
        onError: (String) -> Unit
    ) {
        currentPreviewSurface = previewSurface
        startBackgroundThread()

        try {
            activeCameraId = cameraId ?: cameraManager.cameraIdList.firstOrNull { id ->
                val chars = cameraManager.getCameraCharacteristics(id)
                chars.get(CameraCharacteristics.LENS_FACING) == CameraCharacteristics.LENS_FACING_BACK
            } ?: "0"

            val characteristics = cameraManager.getCameraCharacteristics(activeCameraId)
            minFocusDistanceDiopters = characteristics.get(CameraCharacteristics.LENS_INFO_MINIMUM_FOCUS_DISTANCE) ?: 10.0f
            sensorOrientation = characteristics.get(CameraCharacteristics.SENSOR_ORIENTATION) ?: 90

            evRange = characteristics.get(CameraCharacteristics.CONTROL_AE_COMPENSATION_RANGE) ?: Range(-6, 6)
            val stepVal = characteristics.get(CameraCharacteristics.CONTROL_AE_COMPENSATION_STEP)
            evStepRational = if (stepVal != null && stepVal.denominator != 0) stepVal.numerator.toFloat() / stepVal.denominator else 0.33f

            val maxZoom = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                characteristics.get(CameraCharacteristics.CONTROL_ZOOM_RATIO_RANGE)?.upper ?: 5.0f
            } else 5.0f
            maxZoomRatio = maxZoom.coerceIn(1.0f, 10.0f)

            // High resolution ImageReader for burst captures
            burstImageReader = ImageReader.newInstance(1920, 1080, ImageFormat.JPEG, 20)

            // High-framerate YUV ImageReader for Realtime Focus Peaking
            peakingImageReader = ImageReader.newInstance(640, 480, ImageFormat.YUV_420_888, 2).apply {
                setOnImageAvailableListener({ reader ->
                    try {
                        val image = reader.acquireLatestImage() ?: return@setOnImageAvailableListener
                        peakingAnalyzer?.analyzeCamera2Image(image, sensorOrientation)
                        image.close()
                    } catch (e: Exception) {
                        Log.w("CameraController", "Peaking frame error: ${e.message}")
                    }
                }, backgroundHandler)
            }

            cameraManager.openCamera(activeCameraId, object : CameraDevice.StateCallback() {
                override fun onOpened(camera: CameraDevice) {
                    cameraDevice = camera
                    createSession(previewSurface, onOpened, onError)
                }

                override fun onDisconnected(camera: CameraDevice) {
                    camera.close()
                    cameraDevice = null
                }

                override fun onError(camera: CameraDevice, error: Int) {
                    camera.close()
                    cameraDevice = null
                    onError("Camera open error: $error")
                }
            }, backgroundHandler)

        } catch (e: Exception) {
            onError("Failed to open camera: ${e.message}")
        }
    }

    private fun createSession(
        previewSurface: Surface,
        onOpened: () -> Unit,
        onError: (String) -> Unit
    ) {
        val targets = mutableListOf(previewSurface, burstImageReader!!.surface)
        peakingImageReader?.surface?.let { targets.add(it) }

        cameraDevice?.createCaptureSession(targets, object : CameraCaptureSession.StateCallback() {
            override fun onConfigured(session: CameraCaptureSession) {
                captureSession = session
                startPreview(previewSurface)
                onOpened()
            }

            override fun onConfigureFailed(session: CameraCaptureSession) {
                onError("Failed to configure camera capture session.")
            }
        }, backgroundHandler)
    }

    fun startPreview(previewSurface: Surface? = currentPreviewSurface) {
        val surface = previewSurface ?: return
        try {
            val requestBuilder = cameraDevice?.createCaptureRequest(CameraDevice.TEMPLATE_PREVIEW)?.apply {
                addTarget(surface)
                peakingImageReader?.surface?.let { addTarget(it) }
                applyProControls(this)
            }
            if (requestBuilder != null) {
                captureSession?.setRepeatingRequest(requestBuilder.build(), null, backgroundHandler)
            }
        } catch (e: Exception) {
            Log.e("CameraController", "Preview error: ${e.message}")
        }
    }

    private fun applyProControls(builder: CaptureRequest.Builder) {
        // 1. Focus Mode & Manual Lens Diopter Stepping
        when (currentFocusMode) {
            FocusMode.MANUAL -> {
                builder.set(CaptureRequest.CONTROL_AF_MODE, CaptureRequest.CONTROL_AF_MODE_OFF)
                builder.set(CaptureRequest.LENS_FOCUS_DISTANCE, liveManualDiopters.coerceIn(0f, minFocusDistanceDiopters))
            }
            FocusMode.CONTINUOUS_AF -> {
                builder.set(CaptureRequest.CONTROL_AF_MODE, CaptureRequest.CONTROL_AF_MODE_CONTINUOUS_PICTURE)
            }
            FocusMode.AF_LOCKED -> {
                builder.set(CaptureRequest.CONTROL_AF_MODE, CaptureRequest.CONTROL_AF_MODE_AUTO)
            }
        }

        // 2. Exposure Compensation
        builder.set(CaptureRequest.CONTROL_AE_EXPOSURE_COMPENSATION, currentEvStep.coerceIn(evRange.lower, evRange.upper))

        // 3. Flash / Torch Mode
        when (currentFlashMode) {
            FlashMode.OFF -> {
                builder.set(CaptureRequest.FLASH_MODE, CaptureRequest.FLASH_MODE_OFF)
                builder.set(CaptureRequest.CONTROL_AE_MODE, CaptureRequest.CONTROL_AE_MODE_ON)
            }
            FlashMode.TORCH -> {
                builder.set(CaptureRequest.FLASH_MODE, CaptureRequest.FLASH_MODE_TORCH)
                builder.set(CaptureRequest.CONTROL_AE_MODE, CaptureRequest.CONTROL_AE_MODE_ON)
            }
            FlashMode.AUTO -> {
                builder.set(CaptureRequest.CONTROL_AE_MODE, CaptureRequest.CONTROL_AE_MODE_ON_AUTO_FLASH)
            }
        }

        // 4. Digital Zoom
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            builder.set(CaptureRequest.CONTROL_ZOOM_RATIO, currentZoomRatio)
        }
    }

    /**
     * Updates manual focus distance in real time and moves physical lens
     */
    fun setLiveManualFocus(diopters: Float) {
        currentFocusMode = FocusMode.MANUAL
        liveManualDiopters = diopters.coerceIn(0f, minFocusDistanceDiopters)
        startPreview()
    }

    fun setFocusMode(mode: FocusMode) {
        currentFocusMode = mode
        startPreview()
    }

    fun setExposureCompensation(evStep: Int) {
        currentEvStep = evStep.coerceIn(evRange.lower, evRange.upper)
        startPreview()
    }

    fun setFlashMode(mode: FlashMode) {
        currentFlashMode = mode
        startPreview()
    }

    fun setZoomRatio(ratio: Float) {
        currentZoomRatio = ratio.coerceIn(1.0f, maxZoomRatio)
        startPreview()
    }

    /**
     * Tap to Focus and Meter at normalized screen coordinate (0.0 .. 1.0)
     */
    fun triggerTapToFocus(normX: Float, normY: Float) {
        val session = captureSession ?: return
        val device = cameraDevice ?: return
        val surface = currentPreviewSurface ?: return

        try {
            val chars = cameraManager.getCameraCharacteristics(activeCameraId)
            val sensorRect = chars.get(CameraCharacteristics.SENSOR_INFO_ACTIVE_ARRAY_SIZE) ?: Rect(0, 0, 1920, 1080)

            val boxSize = (sensorRect.width() * 0.15f).toInt()
            val centerX = (normX * sensorRect.width()).toInt()
            val centerY = (normY * sensorRect.height()).toInt()

            val focusRect = Rect(
                (centerX - boxSize / 2).coerceIn(0, sensorRect.width() - boxSize),
                (centerY - boxSize / 2).coerceIn(0, sensorRect.height() - boxSize),
                (centerX + boxSize / 2).coerceIn(boxSize, sensorRect.width()),
                (centerY + boxSize / 2).coerceIn(boxSize, sensorRect.height())
            )

            val meteringRect = MeteringRectangle(focusRect, MeteringRectangle.METERING_WEIGHT_MAX)
            currentFocusMode = FocusMode.AF_LOCKED

            // Trigger AF
            val triggerBuilder = device.createCaptureRequest(CameraDevice.TEMPLATE_PREVIEW).apply {
                addTarget(surface)
                peakingImageReader?.surface?.let { addTarget(it) }
                set(CaptureRequest.CONTROL_AF_REGIONS, arrayOf(meteringRect))
                set(CaptureRequest.CONTROL_AE_REGIONS, arrayOf(meteringRect))
                set(CaptureRequest.CONTROL_AF_MODE, CaptureRequest.CONTROL_AF_MODE_AUTO)
                set(CaptureRequest.CONTROL_AF_TRIGGER, CaptureRequest.CONTROL_AF_TRIGGER_START)
                applyProControls(this)
            }
            session.capture(triggerBuilder.build(), null, backgroundHandler)

        } catch (e: Exception) {
            Log.e("CameraController", "Tap-to-focus error: ${e.message}")
        }
    }

    /**
     * Executes manual lens focus distance stepping burst
     */
    fun captureMacroBurst(
        config: MacroBurstConfig,
        outputDir: File,
        onBurstComplete: (List<File>) -> Unit,
        onError: (String) -> Unit
    ) {
        if (_isCapturing.value || cameraDevice == null || captureSession == null) return

        _isCapturing.value = true
        _capturedCount.value = 0

        val capturedFiles = mutableListOf<File>()
        val steps = config.steps
        val startD = config.startDistanceDiopters.coerceIn(0f, minFocusDistanceDiopters)
        val endD = config.endDistanceDiopters.coerceIn(0f, minFocusDistanceDiopters)
        val stepDelta = if (steps > 1) (startD - endD) / (steps - 1) else 0f

        burstImageReader?.setOnImageAvailableListener({ reader ->
            val image = reader.acquireNextImage()
            if (image != null) {
                val buffer: ByteBuffer = image.planes[0].buffer
                val bytes = ByteArray(buffer.remaining())
                buffer.get(bytes)

                val file = File(outputDir, "macro_frame_${System.currentTimeMillis()}_${capturedFiles.size}.jpg")
                FileOutputStream(file).use { it.write(bytes) }
                image.close()

                capturedFiles.add(file)
                _capturedCount.value = capturedFiles.size

                if (capturedFiles.size >= steps) {
                    _isCapturing.value = false
                    onBurstComplete(capturedFiles)
                }
            }
        }, backgroundHandler)

        try {
            val burstRequests = mutableListOf<CaptureRequest>()
            for (i in 0 until steps) {
                val currentDistance = startD - i * stepDelta
                val requestBuilder = cameraDevice!!.createCaptureRequest(CameraDevice.TEMPLATE_STILL_CAPTURE).apply {
                    addTarget(burstImageReader!!.surface)
                    set(CaptureRequest.CONTROL_AF_MODE, CaptureRequest.CONTROL_AF_MODE_OFF)
                    set(CaptureRequest.LENS_FOCUS_DISTANCE, currentDistance)
                    set(CaptureRequest.JPEG_QUALITY, 95.toByte())
                    set(CaptureRequest.JPEG_ORIENTATION, sensorOrientation)
                    applyProControls(this)
                }
                burstRequests.add(requestBuilder.build())
            }

            captureSession?.captureBurst(burstRequests, object : CameraCaptureSession.CaptureCallback() {
                override fun onCaptureFailed(session: CameraCaptureSession, request: CaptureRequest, failure: CaptureFailure) {
                    Log.w("CameraController", "Frame capture failed: ${failure.reason}")
                }
            }, backgroundHandler)

        } catch (e: Exception) {
            _isCapturing.value = false
            onError("Burst capture error: ${e.message}")
        }
    }

    fun close() {
        captureSession?.close()
        captureSession = null
        cameraDevice?.close()
        cameraDevice = null
        burstImageReader?.close()
        burstImageReader = null
        peakingImageReader?.close()
        peakingImageReader = null
        stopBackgroundThread()
    }
}
