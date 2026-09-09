# FImageStack: System Architecture & Design Principles

## 🏛️ Architecture Overview

**FImageStack** is an ultra-high-performance Computational Imaging and Industrial Metrology engine architected in pure **C# (.NET 9)** with native **Direct3D 11 GPU acceleration** (via **ZeroGraphics**). It follows a strict **Clean Layered Architecture** combined with the **Universe Plugin Architecture v4.0** standards.

```
┌────────────────────────────────────────────────────────────────────────┐
│                      FImageStack.UI (WPF Studio)                       │
│  - Dark-Theme Studio with 5 Navigation Tabs                            │
│  - ZeroGraphics DirectX 11 Flip Model SwapChain Viewport (sub-4ms)     │
│  - Live Histogram, Focus Peaking, Interactive Retouch Brush            │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
┌───────────────────────────────────▼────────────────────────────────────┐
│                    FImageStack.Application Layer                       │
│  - IStackService, IProjectService, IMacroService                       │
│  - Asynchronous Execution Pipeline (CancellationToken, IProgress<T>)   │
│  - Stage Pipeline Orchestrator & Multi-Resolution Presets              │
└───────────────────┬────────────────────────────────┬───────────────────┘
                    │                                │
┌───────────────────▼─────────────┐   ┌──────────────▼───────────────────┐
│     FImageStack.Infrastructure  │   │     FImageStack.Core (Engine)    │
│  - SixLabors.ImageSharp I/O     │   │  - Zero Unmanaged Memory Leaks   │
│  - ZeroGraphics D3D11 Adapter   │   │  - SIMD AVX2 / AVX-512 Loops     │
│  - GPU DMA Texture Upload       │   │  - 9 Computational Subsystems    │
│  - Project JSON & Cache Store   │   │  - Generic ImageBuffer<T> Pointers│
└─────────────────────────────────┘   └──────────────────────────────────┘
```

---

## 1. Core Architectural Pillars

### 1.1 Invariant Core (Physical Laws)
* **`FImageStack.Core`** is strictly UI-agnostic and platform-neutral (`net9.0`).
* Contains the mathematical foundations, generic data models ([ImageBuffer<T>](file:///e:/15.%20Other/FStack/src/FImageStack.Core/Models/ImageBuffer.cs)), and engine interfaces.
* Employs zero third-party UI dependencies and minimal external references.

### 1.2 Zero-Allocation & Unmanaged Memory Model
* Deep image stacks (e.g., 30–100 frames at 45MP) consume tens of gigabytes of working memory. To completely eliminate .NET Garbage Collection (GC) pauses and Large Object Heap (LOH) fragmentation:
  * All pixel buffers are backed by native memory allocated through `NativeMemory.AllocZeroed((nuint)bytes)`.
  * `ImageBuffer<T>` implements `IDisposable` with deterministic finalizer safety.
  * Intermediate scratch calculations reuse memory buffers via `ArrayPool<T>.Shared`.

### 1.3 High-Performance SIMD Acceleration
* Critical inner loops (such as Modified Laplacian, Tenengrad Sobel gradients, Gaussian blur, Mean/Median statistics) are vectorized using `System.Runtime.Intrinsics.X86.Avx2` and `Avx512F` intrinsics.
* Supports fallback to scalar processing when AVX hardware is unavailable.

### 1.4 Pluggable Hardware Acceleration (`IGpuAccelerationEngine`)
* Hardware acceleration is abstracted behind the [IGpuAccelerationEngine](file:///e:/15.%20Other/FStack/src/FImageStack.Core/Acceleration/GpuAccelerationEngine.cs) contract.
* The system supports runtime switching between:
  1. **Direct3D 11 Compute Shaders** (ZeroGraphics sovereign GPU engine).
  2. **CPU AVX2/AVX-512 Multi-Threaded SIMD** (Vectorized fallback engine).

---

## 2. Solution Layering & Responsibilities

```
FImageStack.slnx
│
├── 📂 src/
│   ├── FImageStack.Core            → Invariant Domain Models, Algorithmic Engines, Interfaces
│   ├── FImageStack.Application     → Use Cases, Workflow Orchestration, DTOs, Project Services
│   ├── FImageStack.Infrastructure  → ImageSharp File I/O, File System, GPU Interop Adapters
│   ├── FImageStack.UI              → WPF Studio GUI, ViewModels, Direct3D 11 Canvas Host
│   └── FImageStack.Cli             → Headless Command-Line Interface for Automated Pipelines
│
├── 📂 tests/
│   └── FImageStack.Core.Tests      → 120+ Unit & Algorithmic Regression Tests (xUnit)
│
└── 📂 tools/
    └── FImageStack.DatasetGenerator→ Synthetic Focus-Bracketed Image Generator for Benchmarking
```

### Layer Breakdown:

| Layer | Target | Responsibilities | Key Dependencies |
| :--- | :--- | :--- | :--- |
| **`Core`** | `net9.0` | Math kernels, image representations, fusion logic, metrics | Pure .NET (No external NuGet) |
| **`Application`** | `net9.0` | Orchestration services (`StackService`), pipeline cancellation | `Core`, `Infrastructure` |
| **`Infrastructure`**| `net9.0` | Decoding/encoding (TIFF, PNG, JPEG, RAW), GPU Adapters | `Core`, `SixLabors.ImageSharp` |
| **`UI`** | `net9.0-windows` | MVVM Presentation, Direct3D 11 Viewport, Touch/HUD controls | `Application`, `Core`, `Infrastructure` |
| **`Cli`** | `net9.0` | Headless execution, batch folder processing, CLI parser | `Application`, `Core`, `Infrastructure` |

---

## 3. Data Flow & Processing Pipeline

The canonical execution flow within `StackService.ProcessStackAsync` follows a decoupled 9-stage pipeline:

```
[Input Files: TIFF/JPEG/RAW]
          │
          ▼
   Stage 1: Ingestion & Decode ────────── ImageSharpIO (Decoded to ImageBuffer<float> RGB)
          │
          ▼
   Stage 2: Automatic Frame Selection ──── OptimalFrameRangeSelector (Cull Blurry/Redundant Frames)
          │
          ▼
   Stage 3: Subpixel Alignment ────────── 6-DOF Affine / 8-DOF Homography / Elastic Mesh
          │
          ▼
   Stage 4: Sharpness Analysis ────────── SML / Tenengrad / Variance / Wavelet (Focus Map)
          │
          ▼
   Stage 5: Continuous Depth Map ──────── Parabolic Peak Interpolation + Guided Smoothing
          │
          ▼
   Stage 6: Multi-Scale Fusion ────────── Laplacian Pyramid / Wavelet / Exposure Hybrid
          │
          ▼
   Stage 7: Quality Inspection ────────── 6-Metric Quality Scorecard + Artifact Hunter
          │
          ▼
   Stage 8: Auto Repair & Edge Recon ──── Inpainting + Edge Discontinuity Reconstruction
          │
          ▼
   Stage 9: Output Composite ──────────── Display in Studio Viewport / Export to TIFF/PNG
```

---

## 4. Concurrency & Asynchronous Design

1. **Non-Blocking UI Thread:** All processing occurs on background worker threads via `Task.Run()` or dedicated thread pools.
2. **Cooperative Cancellation:** Every algorithmic loop polls `CancellationToken.ThrowIfCancellationRequested()` at scanline or block boundaries, ensuring cancellation latency is strictly under 100ms.
3. **Fine-Grained Progress Reporting:** Progress is dispatched via `IProgress<StackProgress>`, decoupling computational progress updates from UI Dispatcher synchronization.
