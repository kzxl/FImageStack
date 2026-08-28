package com.fimagedev.fimagestack.camera

import android.graphics.Bitmap
import android.graphics.Matrix
import android.media.Image
import com.fimagedev.fimagestack.nativebridge.FStackNative
import java.nio.ByteBuffer

class FocusPeakingAnalyzer(
    private val onPeakingBitmapRendered: (Bitmap) -> Unit
) {

    var isPeakingEnabled: Boolean = true
    var peakingColor: Int = 0 // 0: Neon Green, 1: Red, 2: Yellow, 3: Cyan, 4: Magenta
    var displayMode: Int = 0  // 0: Monochrome Background, 1: Transparent Neon Overlay
    var threshold: Float = 0.035f // Optimum sensitivity for edge sharpness

    private var dstRgbaBuffer: ByteBuffer? = null
    private var rawBitmap: Bitmap? = null
    private var rotatedBitmap: Bitmap? = null
    private val rotationMatrix = Matrix()

    fun analyzeCamera2Image(image: Image, sensorOrientation: Int = 90) {
        if (!isPeakingEnabled) {
            return
        }

        val width = image.width
        val height = image.height
        val requiredBytes = width * height * 4

        if (dstRgbaBuffer == null || dstRgbaBuffer!!.capacity() != requiredBytes) {
            dstRgbaBuffer = ByteBuffer.allocateDirect(requiredBytes)
            rawBitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
        }

        val dstBuf = dstRgbaBuffer!!
        val bmp = rawBitmap!!

        val yPlane = image.planes[0]
        val yBuffer = yPlane.buffer
        val yRowStride = yPlane.rowStride

        dstBuf.clear()

        // 1. Execute Ultra-Fast C++ SIMD Focus Peaking directly on Y-Plane
        val res = FStackNative.renderFocusPeakingYDirect(
            yBuffer = yBuffer,
            yRowStride = yRowStride,
            width = width,
            height = height,
            dstBuffer = dstBuf,
            peakingColor = peakingColor,
            displayMode = displayMode,
            threshold = threshold
        )

        if (res == 0) {
            dstBuf.rewind()
            bmp.copyPixelsFromBuffer(dstBuf)

            // 2. Rotate to match portrait screen orientation
            if (sensorOrientation != 0 && sensorOrientation != 360) {
                rotationMatrix.reset()
                rotationMatrix.postRotate(sensorOrientation.toFloat())

                val rotated = Bitmap.createBitmap(bmp, 0, 0, width, height, rotationMatrix, false)
                onPeakingBitmapRendered(rotated)
            } else {
                onPeakingBitmapRendered(bmp)
            }
        }
    }
}
