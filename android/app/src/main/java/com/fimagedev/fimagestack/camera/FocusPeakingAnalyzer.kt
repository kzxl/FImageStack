package com.fimagedev.fimagestack.camera

import android.graphics.Bitmap
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import com.fimagedev.fimagestack.nativebridge.FStackNative
import java.nio.ByteBuffer

class FocusPeakingAnalyzer(
    private val onPeakingBitmapRendered: (Bitmap) -> Unit
) : ImageAnalysis.Analyzer {

    var isPeakingEnabled: Boolean = true
    var peakingColor: Int = 0 // 0: Neon Green, 1: Red, 2: Yellow, 3: Cyan, 4: Magenta
    var displayMode: Int = 0  // 0: Monochrome Background, 1: Color Overlay
    var threshold: Float = 0.045f

    private var srcRgbaBuffer: ByteBuffer? = null
    private var dstRgbaBuffer: ByteBuffer? = null
    private var outputBitmap: Bitmap? = null

    override fun analyze(image: ImageProxy) {
        val width = image.width
        val height = image.height

        if (!isPeakingEnabled) {
            image.close()
            return
        }

        val requiredBytes = width * height * 4

        if (srcRgbaBuffer == null || srcRgbaBuffer!!.capacity() != requiredBytes) {
            srcRgbaBuffer = ByteBuffer.allocateDirect(requiredBytes)
            dstRgbaBuffer = ByteBuffer.allocateDirect(requiredBytes)
            outputBitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
        }

        val srcBuf = srcRgbaBuffer!!
        val dstBuf = dstRgbaBuffer!!
        val bmp = outputBitmap!!

        // Convert YUV_420_888 to RGBA inside direct buffer
        val planes = image.planes
        val yBuffer = planes[0].buffer
        val uBuffer = planes[1].buffer
        val vBuffer = planes[2].buffer

        val yRowStride = planes[0].rowStride
        val uvRowStride = planes[1].rowStride
        val uvPixelStride = planes[1].pixelStride

        srcBuf.clear()

        // Fast inline YUV to RGBA conversion
        for (y in 0 until height) {
            val yOffset = y * yRowStride
            val uvOffset = (y / 2) * uvRowStride

            for (x in 0 until width) {
                val yVal = (yBuffer.get(yOffset + x).toInt() and 0xFF) - 16
                val uVal = (uBuffer.get(uvOffset + (x / 2) * uvPixelStride).toInt() and 0xFF) - 128
                val vVal = (vBuffer.get(uvOffset + (x / 2) * uvPixelStride).toInt() and 0xFF) - 128

                val y1192 = 1192 * yVal.coerceAtLeast(0)
                var r = (y1192 + 1634 * vVal) shr 10
                var g = (y1192 - 833 * vVal - 400 * uVal) shr 10
                var b = (y1192 + 2066 * uVal) shr 10

                r = r.coerceIn(0, 255)
                g = g.coerceIn(0, 255)
                b = b.coerceIn(0, 255)

                srcBuf.put(r.toByte())
                srcBuf.put(g.toByte())
                srcBuf.put(b.toByte())
                srcBuf.put(255.toByte())
            }
        }

        srcBuf.rewind()
        dstBuf.rewind()

        // Execute Native SIMD Focus Peaking Engine
        FStackNative.renderFocusPeakingRgbaDirect(
            srcBuffer = srcBuf,
            width = width,
            height = height,
            dstBuffer = dstBuf,
            peakingColor = peakingColor,
            displayMode = displayMode,
            threshold = threshold
        )

        dstBuf.rewind()
        bmp.copyPixelsFromBuffer(dstBuf)

        onPeakingBitmapRendered(bmp)
        image.close()
    }
}
