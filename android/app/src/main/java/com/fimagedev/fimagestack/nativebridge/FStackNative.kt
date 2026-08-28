package com.fimagedev.fimagestack.nativebridge

import android.util.Log
import java.nio.ByteBuffer

object FStackNative {

    init {
        try {
            System.loadLibrary("fimagestack_native")
        } catch (e: Throwable) {
            Log.e("FStackNative", "Failed to load fimagestack_native: ${e.message}")
        }
    }

    /**
     * Realtime Focus Peaking execution directly over Camera2 Y-Plane (Luminance)
     */
    external fun renderFocusPeakingYDirect(
        yBuffer: ByteBuffer,
        yRowStride: Int,
        width: Int,
        height: Int,
        dstBuffer: ByteBuffer,
        peakingColor: Int,
        displayMode: Int,
        threshold: Float
    ): Int

    /**
     * Realtime Focus Peaking execution over direct RGBA byte buffer
     */
    external fun renderFocusPeakingRgbaDirect(
        srcBuffer: ByteBuffer,
        width: Int,
        height: Int,
        dstBuffer: ByteBuffer,
        peakingColor: Int,
        displayMode: Int,
        threshold: Float
    ): Int

    /**
     * Gets native engine version
     */
    external fun getNativeEngineVersion(): String
}
