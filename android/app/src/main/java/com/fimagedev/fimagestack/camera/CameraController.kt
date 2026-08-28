package com.fimagedev.fimagestack.camera

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.ImageFormat
import android.hardware.camera2.*
import android.media.ImageReader
import android.os.Handler
import android.os.HandlerThread
import android.util.Log
import android.util.Size
import android.view.Surface
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.io.File
import java.io.FileOutputStream
import java.nio.ByteBuffer

data class MacroBurstConfig(
    val steps: Int = 10,
    val startDistanceDiopters: Float = 10.0f, // 10 cm near macro
    val endDistanceDiopters: Float = 1.0f     // 100 cm far
)

class CameraController(private val context: Context) {

    private val cameraManager = context.getSystemService(Context.CAMERA_SERVICE) as CameraManager
    private var cameraDevice: CameraDevice? = null
    private var captureSession: CameraCaptureSession? = null
    private var imageReader: ImageReader? = null

    private var backgroundThread: HandlerThread? = null
    private var backgroundHandler: Handler? = null

    private val _isCapturing = MutableStateFlow(false)
    val isCapturing = _isCapturing.asStateFlow()

    private val _capturedCount = MutableStateFlow(0)
    val capturedCount = _capturedCount.asStateFlow()

    private var minFocusDistanceDiopters = 10.0f
    var sensorOrientation: Int = 90

    fun startBackgroundThread() {
        backgroundThread = HandlerThread("CameraBackground").also { it.start() }
        backgroundHandler = Handler(backgroundThread!!.looper)
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

    @SuppressLint("MissingPermission")
    fun openCamera(
        previewSurface: Surface,
        onOpened: () -> Unit,
        onError: (String) -> Unit
    ) {
        try {
            val cameraId = cameraManager.cameraIdList.firstOrNull { id ->
                val chars = cameraManager.getCameraCharacteristics(id)
                val facing = chars.get(CameraCharacteristics.LENS_FACING)
                facing == CameraCharacteristics.LENS_FACING_BACK
            } ?: "0"

            val characteristics = cameraManager.getCameraCharacteristics(cameraId)
            minFocusDistanceDiopters = characteristics.get(CameraCharacteristics.LENS_INFO_MINIMUM_FOCUS_DISTANCE) ?: 10.0f
            sensorOrientation = characteristics.get(CameraCharacteristics.SENSOR_ORIENTATION) ?: 90

            // Full resolution ImageReader for high quality burst captures
            imageReader = ImageReader.newInstance(1920, 1080, ImageFormat.JPEG, 15)

            cameraManager.openCamera(cameraId, object : CameraDevice.StateCallback() {
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
        val targets = listOf(previewSurface, imageReader!!.surface)
        cameraDevice?.createCaptureSession(targets, object : CameraCaptureSession.StateCallback() {
            override fun onConfigured(session: CameraCaptureSession) {
                captureSession = session
                // Start live preview
                startPreview(previewSurface)
                onOpened()
            }

            override fun onConfigureFailed(session: CameraCaptureSession) {
                onError("Failed to configure camera capture session.")
            }
        }, backgroundHandler)
    }

    private fun startPreview(previewSurface: Surface) {
        try {
            val requestBuilder = cameraDevice?.createCaptureRequest(CameraDevice.TEMPLATE_PREVIEW)?.apply {
                addTarget(previewSurface)
                set(CaptureRequest.CONTROL_AF_MODE, CaptureRequest.CONTROL_AF_MODE_CONTINUOUS_PICTURE)
            }
            if (requestBuilder != null) {
                captureSession?.setRepeatingRequest(requestBuilder.build(), null, backgroundHandler)
            }
        } catch (e: CameraAccessException) {
            Log.e("CameraController", "Preview error: ${e.message}")
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

        imageReader?.setOnImageAvailableListener({ reader ->
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
                    addTarget(imageReader!!.surface)
                    set(CaptureRequest.CONTROL_AF_MODE, CaptureRequest.CONTROL_AF_MODE_OFF)
                    set(CaptureRequest.LENS_FOCUS_DISTANCE, currentDistance)
                    set(CaptureRequest.JPEG_QUALITY, 95.toByte())
                    set(CaptureRequest.JPEG_ORIENTATION, sensorOrientation)
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
        imageReader?.close()
        imageReader = null
        stopBackgroundThread()
    }
}
