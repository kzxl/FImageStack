#include <jni.h>
#include <string>
#include <vector>
#include <cmath>
#include <algorithm>
#include <android/log.h>

#define TAG "FStackNative"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, TAG, __VA_ARGS__)

extern "C" {

/**
 * High-performance realtime Focus Peaking directly from Camera2 Y-Plane (Luminance)
 * Writes directly to RGBA Output Direct ByteBuffer (Zero-Copy)
 */
JNIEXPORT jint JNICALL
Java_com_fimagedev_fimagestack_nativebridge_FStackNative_renderFocusPeakingYDirect(
    JNIEnv* env,
    jobject /* this */,
    jobject yDirectBuffer,
    jint yRowStride,
    jint width,
    jint height,
    jobject dstDirectBuffer,
    jint peakingColor,
    jint displayMode,
    jfloat threshold
) {
    auto* yPlane = static_cast<const unsigned char*>(env->GetDirectBufferAddress(yDirectBuffer));
    auto* dstRgba = static_cast<unsigned char*>(env->GetDirectBufferAddress(dstDirectBuffer));

    if (!yPlane || !dstRgba || width <= 0 || height <= 0) {
        return -1;
    }

    // Neon color mappings
    unsigned char pR = 25, pG = 255, pB = 38; // Neon Green default (0)
    switch (peakingColor) {
        case 1: pR = 255; pG = 25;  pB = 25;  break; // Red
        case 2: pR = 255; pG = 242; pB = 0;   break; // Yellow
        case 3: pR = 0;   pG = 230; pB = 255; break; // Cyan
        case 4: pR = 255; pG = 13;  pB = 217; break; // Magenta
        default: break;
    }

    const float threshScaled = threshold * 255.0f;
    const bool isMono = (displayMode == 0); // 0: Monochrome Background, 1: Transparent Neon Overlay

    #pragma omp parallel for schedule(static)
    for (int y = 1; y < height - 1; y++) {
        int yOffset = y * yRowStride;
        int prevOffset = (y - 1) * yRowStride;
        int nextOffset = (y + 1) * yRowStride;
        int outRow = y * width * 4;

        for (int x = 1; x < width - 1; x++) {
            int lCenter = yPlane[yOffset + x];
            int lLeft   = yPlane[yOffset + (x - 1)];
            int lRight  = yPlane[yOffset + (x + 1)];
            int lUp     = yPlane[prevOffset + x];
            int lDown   = yPlane[nextOffset + x];

            // 5-point discrete 2D Laplacian operator: |4*C - L - R - U - D|
            int laplacian = std::abs((lCenter << 2) - lLeft - lRight - lUp - lDown);

            int outIdx = outRow + x * 4;

            if (laplacian >= threshScaled) {
                // In-focus edge pixel: High-visibility Neon Glow
                dstRgba[outIdx + 0] = pR;
                dstRgba[outIdx + 1] = pG;
                dstRgba[outIdx + 2] = pB;
                dstRgba[outIdx + 3] = 255;
            } else if (isMono) {
                // Grayscale background mode
                auto gray = static_cast<unsigned char>(lCenter);
                dstRgba[outIdx + 0] = gray;
                dstRgba[outIdx + 1] = gray;
                dstRgba[outIdx + 2] = gray;
                dstRgba[outIdx + 3] = 255;
            } else {
                // Transparent overlay mode (Only glowing edges are visible over live preview)
                dstRgba[outIdx + 0] = 0;
                dstRgba[outIdx + 1] = 0;
                dstRgba[outIdx + 2] = 0;
                dstRgba[outIdx + 3] = 0;
            }
        }
    }

    return 0;
}

/**
 * High-performance realtime Focus Peaking over raw RGBA buffer
 */
JNIEXPORT jint JNICALL
Java_com_fimagedev_fimagestack_nativebridge_FStackNative_renderFocusPeakingRgbaDirect(
    JNIEnv* env,
    jobject /* this */,
    jobject srcDirectBuffer,
    jint width,
    jint height,
    jobject dstDirectBuffer,
    jint peakingColor,
    jint displayMode,
    jfloat threshold
) {
    auto* srcRgba = static_cast<const unsigned char*>(env->GetDirectBufferAddress(srcDirectBuffer));
    auto* dstRgba = static_cast<unsigned char*>(env->GetDirectBufferAddress(dstDirectBuffer));

    if (!srcRgba || !dstRgba || width <= 0 || height <= 0) {
        return -1;
    }

    unsigned char pR = 25, pG = 255, pB = 38;
    switch (peakingColor) {
        case 1: pR = 255; pG = 25;  pB = 25;  break;
        case 2: pR = 255; pG = 242; pB = 0;   break;
        case 3: pR = 0;   pG = 230; pB = 255; break;
        case 4: pR = 255; pG = 13;  pB = 217; break;
        default: break;
    }

    const float threshScaled = threshold * 255.0f;
    const bool isMono = (displayMode == 0);

    #pragma omp parallel for schedule(static)
    for (int y = 1; y < height - 1; y++) {
        int row = y * width * 4;
        int prevRow = (y - 1) * width * 4;
        int nextRow = (y + 1) * width * 4;

        for (int x = 1; x < width - 1; x++) {
            int curr = row + x * 4;
            int left = row + (x - 1) * 4;
            int right = row + (x + 1) * 4;
            int up = prevRow + x * 4;
            int down = nextRow + x * 4;

            int lCenter = (srcRgba[curr] + (srcRgba[curr + 1] << 1) + srcRgba[curr + 2]) >> 2;
            int lLeft   = (srcRgba[left] + (srcRgba[left + 1] << 1) + srcRgba[left + 2]) >> 2;
            int lRight  = (srcRgba[right] + (srcRgba[right + 1] << 1) + srcRgba[right + 2]) >> 2;
            int lUp     = (srcRgba[up] + (srcRgba[up + 1] << 1) + srcRgba[up + 2]) >> 2;
            int lDown   = (srcRgba[down] + (srcRgba[down + 1] << 1) + srcRgba[down + 2]) >> 2;

            int laplacian = std::abs((lCenter << 2) - lLeft - lRight - lUp - lDown);

            if (laplacian >= threshScaled) {
                dstRgba[curr + 0] = pR;
                dstRgba[curr + 1] = pG;
                dstRgba[curr + 2] = pB;
                dstRgba[curr + 3] = 255;
            } else if (isMono) {
                auto gray = static_cast<unsigned char>(lCenter);
                dstRgba[curr + 0] = gray;
                dstRgba[curr + 1] = gray;
                dstRgba[curr + 2] = gray;
                dstRgba[curr + 3] = 255;
            } else {
                dstRgba[curr + 0] = 0;
                dstRgba[curr + 1] = 0;
                dstRgba[curr + 2] = 0;
                dstRgba[curr + 3] = 0;
            }
        }
    }

    return 0;
}

/**
 * Returns FImageStack Native Engine Version
 */
JNIEXPORT jstring JNICALL
Java_com_fimagedev_fimagestack_nativebridge_FStackNative_getNativeEngineVersion(
    JNIEnv* env,
    jobject /* this */
) {
    return env->NewStringUTF("1.2.0-macro-arm64");
}

} // extern "C"
