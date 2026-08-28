package com.fimagedev.fimagestack.nativebridge

import java.nio.ByteBuffer

object FStackNative {

    init {
        System.loadLibrary("fimagestack_native")
    }

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
