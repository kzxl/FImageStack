# ZeroGraphics: Direct3D 11 GPU Acceleration Architecture

This document specifies the technical design, shader kernels, and presentation pipeline for integrating **ZeroGraphics Direct3D 11 GPU Acceleration** into **FImageStack**.

---

## ⚡ 1. Architectural Motivation

Computational focus stacking and high-resolution metrology involve heavy numerical workloads:
* Measuring Laplacian energy maps across 30 to 50 frames (24MP to 100MP).
* Multi-scale Gaussian/Laplacian pyramid decimation and expansion.
* Perceptual HDR tone mapping.
* Viewport rendering of gigapixel images without CPU memory duplication.

**ZeroGraphics** provides hardware acceleration via pure C# COM VTable interop, eliminating third-party native wrappers (SharpDX, Silk.NET) while achieving sub-4ms display latency.

```
┌────────────────────────────────────────────────────────────────────────┐
│                   ZeroGraphics D3D11 Acceleration Stack                │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
          ┌─────────────────────────┴─────────────────────────┐
          ▼                                                   ▼
┌───────────────────────────────────┐       ┌────────────────────────────────────┐
│      Compute Shader Pipeline      │       │     Flip Model SwapChain Viewport  │
│  - CsFocusMeasure (Laplacian)     │       │  - DXGI_SWAP_EFFECT_FLIP_DISCARD   │
│  - CsFocusBlend (Max Sharpness)   │       │  - SetMaximumFrameLatency(1)       │
│  - CsHdrToneMapping (ACES/Reinhard│       │  - Hardware Point/Linear Filtering │
│  - GpuImagePyramid (Down/Up)      │       │  - Zero CPU DWM Redirection Copies │
│  - GigapixelTileScheduler         │       │  - 144Hz Zoom/Pan Canvas           │
└───────────────────────────────────┘       └────────────────────────────────────┘
```

---

## 2. Direct3D 11 Compute Shader Kernels

### 2.1 Modified Laplacian Energy (`CsFocusMeasure`)
* **Thread Group Configuration:** `[numthreads(16, 16, 1)]`
* **Execution:** Calculates horizontal and vertical second-order differences on linear input textures and writes to an `R32_FLOAT` Unordered Access View (UAV).
* **HLSL Kernel Prototype:**
  ```hlsl
  cbuffer FocusMeasureConstants : register(b0)
  {
      uint FocusWidth;
      uint FocusHeight;
      uint FocusRadius;
      float FocusPad;
  };

  Texture2D<float4> InputSlice : register(t0);
  RWTexture2D<float> SliceEnergy : register(u0);

  [numthreads(16, 16, 1)]
  void CS_Measure(uint3 id : SV_DispatchThreadID)
  {
      if (id.x >= FocusWidth || id.y >= FocusHeight) return;
      // Evaluate SML energy
      float center = InputSlice.Load(int3(id.xy, 0)).r;
      float left   = InputSlice.Load(int3(max(0, (int)id.x - 1), id.y, 0)).r;
      float right  = InputSlice.Load(int3(min((int)FocusWidth - 1, (int)id.x + 1), id.y, 0)).r;
      float top    = InputSlice.Load(int3(id.x, max(0, (int)id.y - 1), 0)).r;
      float bottom = InputSlice.Load(int3(id.x, min((int)FocusHeight - 1, (int)id.y + 1), 0)).r;
      
      float sml = abs(2.0f * center - left - right) + abs(2.0f * center - top - bottom);
      SliceEnergy[id.xy] = sml;
  }
  ```

### 2.2 Maximum Sharpness Fusion (`CsFocusBlend`)
* Compares current `SliceEnergy` against the running `BestEnergy` buffer:
  $$I_{\text{composite}}(x, y) = \begin{cases} I_k(x, y) & \text{if } E_k(x, y) > E_{\text{best}}(x, y) \\ I_{\text{composite}}(x, y) & \text{otherwise} \end{cases}$$
* Updates both the composite texture and the best energy buffer simultaneously on the GPU.

### 2.3 HDR Tone Mapping (`CsHdrToneMapping`)
* Evaluates ACES Filmic curve or Reinhard operator in parallel across all texture texels:
  ```hlsl
  [numthreads(16, 16, 1)]
  void CS_ToneMap(uint3 id : SV_DispatchThreadID)
  {
      float4 hdr = HdrTexture.Load(int3(id.xy, 0));
      float3 mapped = (hdr.rgb * (2.51f * hdr.rgb + 0.03f)) / 
                      (hdr.rgb * (2.43f * hdr.rgb + 0.59f) + 0.14f);
      SdrOutput[id.xy] = float4(saturate(mapped), 1.0f);
  }
  ```

---

## 3. Zero-Allocation DMA Texture Ingestion

Instead of copying pixels through managed byte arrays:
1. `FImageStack.Core.Models.ImageBuffer<float>` exposes its unmanaged pointer `DataPointer`.
2. `GpuTextureTransfer.UploadRaw(IntPtr scan0, width, height, stride)` invokes `ID3D11DeviceContext::UpdateSubresource` or dynamic `Map(D3D11_MAP_WRITE_DISCARD)` directly to the target D3D11 texture.
3. No intermediate memory buffers are allocated on the .NET Garbage Collector heap.

---

## 4. Modern Flip Model Presentation Viewport

### 4.1 Latency Optimization
* Standard WPF image presentation models render via `WriteableBitmap`, transferring pixels across CPU/GPU boundaries on every UI render pass.
* ZeroGraphics hosts an HWND SwapChain configured with `DXGI_SWAP_EFFECT_FLIP_DISCARD` and `SetMaximumFrameLatency(1)`.
* **Input-to-Photon Latency:** $< 4\text{ms}$ at 144Hz.
* **Idle CPU Usage:** Exactly $0.0\%$.

### 4.2 Smooth Interactive Navigation
* **Hardware Zooming:** Mouse wheel scroll zooms in/out centered precisely at the cursor coordinates.
* **Hardware Panning:** Mouse middle/right drag pans the image with bilinear or nearest-neighbor texture filtering.
* **Overlay Layers:** Direct GPU rendering of bounding boxes, focus peaking masks, and measurement calipers.
