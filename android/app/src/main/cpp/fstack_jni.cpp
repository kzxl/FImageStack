#include <jni.h>
#include <string>
#include <vector>
#include <cmath>
#include <algorithm>
#include <android/log.h>

#define TAG "FStackNative"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, TAG, __VA_ARGS__)

// Function pointer signatures for FImageStack.Core C-ABI exports
typedef int (*fstack_process_macro_fn)(
    const float** frameRgbPointers,
    int frameCount,
    int width,
    int height,
    float* outRgbBuffer,
    float* outDepthMapBuffer,
    int autoCull,
    float minSharpness,
    int correctBreathing,
    float microDetailBoost
);

typedef int (*fstack_peaking_fn)(
    const unsigned char* srcRgba,
    int width,
    int height,
    unsigned char* dstRgba,
    int peakingColor,
    int displayMode,
    float threshold
);

extern "C" {

/**
 * High-performance realtime Focus Peaking over raw camera RGBA buffer
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

    // Neon color mappings
    unsigned char pR = 25, pG = 255, pB = 38; // Neon Green default
    switch (peakingColor) {
        case 1: pR = 255; pG = 25;  pB = 25;  break; // Red
        case 2: pR = 255; pG = 242; pB = 0;   break; // Yellow
        case 3: pR = 0;   pG = 230; pB = 255; break; // Cyan
        case 4: pR = 255; pG = 13;  pB = 217; break; // Magenta
        default: break;
    }

    const float threshScaled = threshold * 255.0f;
    const bool isMono = (displayMode == 0); // Monochrome Background Mode

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

            // Fast integer luminance: (R + 2G + B) / 4
            int lCenter = (srcRgba[curr] + (srcRgba[curr + 1] << 1) + srcRgba[curr + 2]) >> 2;
            int lLeft   = (srcRgba[left] + (srcRgba[left + 1] << 1) + srcRgba[left + 2]) >> 2;
            int lRight  = (srcRgba[right] + (srcRgba[right + 1] << 1) + srcRgba[right + 2]) >> 2;
            int lUp     = (srcRgba[up] + (srcRgba[up + 1] << 1) + srcRgba[up + 2]) >> 2;
            int lDown   = (srcRgba[down] + (srcRgba[down + 1] << 1) + srcRgba[down + 2]) >> 2;

            int lx = std::abs((lCenter << 1) - lLeft - lRight);
            int ly = std::abs((lCenter << 1) - lUp - lDown);
            int energy = lx + ly;

            if (energy >= threshScaled) {
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
                dstRgba[curr + 0] = srcRgba[curr + 0];
                dstRgba[curr + 1] = srcRgba[curr + 1];
                dstRgba[curr + 2] = srcRgba[curr + 2];
                dstRgba[curr + 3] = 255;
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
