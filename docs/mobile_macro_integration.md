# Mobile Macro Computational Photography Integration Guide

This guide documents how to integrate the high-performance `FImageStack.Core` engine into **iOS (Swift)**, **Android (Kotlin)**, and **Flutter** mobile camera applications.

---

## 1. System Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    MOBILE CLIENT (Native App)               │
│  - 1-Tap Capture UI with Realtime Focus Peaking Preview     │
│  - Hardware Camera Controller:                              │
│      * iOS: AVFoundation (setFocusModeLocked:lensPosition:) │
│      * Android: Camera2 API (LENS_FOCUS_DISTANCE steps)     │
│  - Captures burst sequence of 5 to 20 focus slices          │
└──────────────────────────────┬──────────────────────────────┘
                               │ (Zero-Copy Pointers / FFI)
                               ▼
┌─────────────────────────────────────────────────────────────┐
│               FSTACK NATIVE C-ABI BRIDGE                    │
│      fstack_macro_process_raw_rgb(float**, count, ...)      │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────┐
│                 FIMAGESTACK CORE ENGINE                     │
│  1. Sharpness Scoring & Blurry Frame Culling (Quality)      │
│  2. Focus Breathing Compensation & Homography Alignment     │
│  3. Sub-Part DOF Preservation & Region-Adaptive Fusion      │
│  4. Micro-Detail Recovery & Deconvolution                   │
│  5. Continuous Depth Map Generation                         │
└─────────────────────────────────────────────────────────────┘
```

---

## 2. Compiling Core Engine to Native Binaries (Native AOT)

### iOS Target (XCFramework / Static Lib)
```bash
dotnet publish src/FImageStack.Core/FImageStack.Core.csproj \
  -r ios-arm64 \
  -c Release \
  -p:PublishAot=true \
  -p:PublishSingleFile=true \
  -o ./publish/native/ios
```

### Android Target (Shared Object `.so`)
```bash
dotnet publish src/FImageStack.Core/FImageStack.Core.csproj \
  -r linux-bionic-arm64 \
  -c Release \
  -p:PublishAot=true \
  -o ./publish/native/android/arm64-v8a
```

---

## 3. Native C-ABI Function Signature

Exported from `FImageStack.Core.Native.MacroNativeBridge`:

```c
// fstack_macro_bridge.h
#include <stdint.h>

int32_t fstack_macro_process_raw_rgb(
    float** frameBuffers,          // Array of pointers to unmanaged linear RGB float arrays (W * H * 3)
    int32_t frameCount,            // Number of burst frames (e.g. 8)
    int32_t width,                 // Image width in pixels
    int32_t height,                // Image height in pixels
    float* outRgbBuffer,           // Destination buffer (W * H * 3)
    float* outDepthMapBuffer,      // Optional destination for depth map (W * H), can be NULL
    int32_t autoCullBlur,          // 1 = enable automatic culling of blurry/shaken frames, 0 = keep all
    float minSharpnessRatio,       // Sharpness rejection threshold (default: 0.12f)
    int32_t alignmentMode,         // 0=None, 1=Rigid, 2=Similarity, 3=Affine, 4=Homography
    int32_t fusionMethod,          // 0=WTA, 1=Weighted, 2=Pyramid, 3=Wavelet, 4=RegionAdaptive
    float microDetailBoost         // High-frequency detail recovery strength (e.g. 0.35f)
);
```

---

## 4. Platform Integration Code Examples

### A. iOS (Swift + AVFoundation)

```swift
import AVFoundation
import UIKit

class MacroCameraController: NSObject, AVCapturePhotoCaptureDelegate {
    private var captureDevice: AVCaptureDevice!
    private var capturedFrames: [UnsafeMutablePointer<Float>] = []
    
    /// Executes automatic focus bracketing capture across 8 lens distance steps
    func captureMacroBurst(steps: Int = 8) {
        capturedFrames.removeAll()
        
        let minFocus: Float = 0.0  // Closest macro distance
        let maxFocus: Float = 0.5  // Mid distance
        let stepDelta = (maxFocus - minFocus) / Float(steps - 1)
        
        for i in 0..<steps {
            let lensPos = minFocus + Float(i) * stepDelta
            try? captureDevice.lockForConfiguration()
            captureDevice.setFocusModeLocked(lensPosition: lensPos) { _ in
                // Trigger photo capture on hardware lens settled
            }
            captureDevice.unlockForConfiguration()
        }
    }
    
    /// Invokes FImageStack Core Engine via C-ABI
    func processBurst(width: Int32, height: Int32) -> UIImage? {
        let rgbSize = Int(width * height * 3)
        let outRgb = UnsafeMutablePointer<Float>.allocate(capacity: rgbSize)
        let outDepth = UnsafeMutablePointer<Float>.allocate(capacity: Int(width * height))
        
        var framePointers = capturedFrames
        let status = fstack_macro_process_raw_rgb(
            &framePointers,
            Int32(capturedFrames.count),
            width,
            height,
            outRgb,
            outDepth,
            1,      // autoCullBlur = true
            0.12,   // minSharpnessRatio
            2,      // alignmentMode = Similarity
            4,      // fusionMethod = RegionAdaptive
            0.35    // microDetailBoost
        )
        
        guard status == 0 else { return nil }
        return convertFloatRgbToUIImage(outRgb, width: width, height: height)
    }
}
```

### B. Android (Kotlin + Camera2 API)

```kotlin
class MacroCameraManager(private val context: Context) {
    private var cameraDevice: CameraDevice? = null
    private var captureSession: CameraCaptureSession? = null

    /**
     * Executes manual lens focus stepping burst
     */
    fun captureMacroBurst(steps: Int = 8, startDistanceDiopters: Float = 10.0f, endDistanceDiopters: Float = 2.0f) {
        val stepSize = (startDistanceDiopters - endDistanceDiopters) / (steps - 1)
        val requests = mutableListOf<CaptureRequest>()

        for (i in 0 until steps) {
            val focusDistance = startDistanceDiopters - i * stepSize
            val requestBuilder = cameraDevice!!.createCaptureRequest(CameraDevice.TEMPLATE_STILL_CAPTURE).apply {
                set(CaptureRequest.CONTROL_AF_MODE, CaptureRequest.CONTROL_AF_MODE_OFF)
                set(CaptureRequest.LENS_FOCUS_DISTANCE, focusDistance)
            }
            requests.add(requestBuilder.build())
        }

        captureSession?.captureBurst(requests, burstCallback, null)
    }

    // External native bridge declaration
    external fun processMacro(
        frameBuffers: Array<ByteBuffer>,
        width: Int,
        height: Int,
        outRgb: ByteBuffer,
        outDepth: ByteBuffer
    ): Int
}
```

---

## 5. Desktop CLI Usage Examples

You can test macro workflows directly from your PC before deploying to mobile devices:

```bash
# Run macro computational stacking with automatic blur culling and micro-detail boost
fstack --mode macro \
       --input data/macro_insect_burst \
       --output output/macro_result.png \
       --macro-cull true \
       --macro-min-sharpness 0.15 \
       --macro-detail 0.40 \
       --macro-breathing true
```
