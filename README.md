# 🔬 FImageStack (FStack) — Next-Gen Computational Imaging & Computational Photography Platform

[![.NET 9.0](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WPF Studio](https://img.shields.io/badge/GUI-WPF%20Studio%20Dark-blue?logo=windows&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
[![Tests](https://img.shields.io/badge/Unit%20Tests-114%2F114%20PASS%20(100%25)-10B981)](#-kiểm-thử--chất-lượng)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0284C7?logo=windows)](https://github.com/)
[![License](https://img.shields.io/badge/License-MIT-purple.svg)](LICENSE)

**FImageStack** là một **Computational Imaging Platform** (nền tảng nhiếp ảnh tính toán) tổng quát thế hệ mới viết bằng C# .NET 9.0 tối ưu hiệu năng cao (Zero-GC Unmanaged Tensors, SIMD AVX2/AVX-512, Multi-Threaded Parallelism, GPU DirectCompute). Không chỉ dừng lại ở Focus Stacking, FImageStack tích hợp 8 trụ cột thuật toán nhiếp ảnh tính toán hiện đại phục vụ từ nhiếp ảnh siêu vi mô (Macro/Microscopy), dải tương phản cao (HDR), thiên văn sâu (Astrophotography), siêu phân giải quang học (HST Drizzle Super-Resolution), phục hồi quang học (Deconvolution/Dehazing), tái tạo 3D (3D Mesh/PLY) cho đến xử lý dữ liệu thô cảm biến (Computational RAW Bayer Fusion).

---

## 🏛️ Sơ Đồ Kiến Trúc Nền Tảng (Core Architecture)

```text
FImageStack (Computational Imaging Platform)
│
├── 1. Focus Stack ────────── [Hoàn tất] Multi-Scale Pyramid, Wavelet DWT, Focus Volume 3D, Virtual Aperture DOF
├── 2. Noise Stack ────────── [Hoàn tất] SIMD Mean, Median, Kappa-Sigma Clipping, Winsorized, Streaming O(1) RAM
├── 3. HDR Stack ──────────── [Hoàn tất] Mertens Multi-Scale Fusion, Debevec Radiance, Motion Deghosting, ACES/AgX
├── 4. Astro Stack ────────── [Hoàn tất] Star Centroid Detector, Asterism Triangles, Dark/Flat Calibration, Auto-Stretch
├── 5. Super Resolution ──── [Hoàn tất] HST Subpixel Drizzle (Variable Pixel Linear Reconstruction) + Multi-frame IBP
├── 6. Image Restoration ─── [Hoàn tất] Richardson-Lucy Deconvolution + TV Damping, Dark Channel Prior Dehazing
├── 7. Depth Reconstruction ─ [Hoàn tất] Continuous Depth Map, Sobel Normal Maps, PLY Point Cloud, OBJ 3D Surface Meshes
├── 8. Image Alignment ────── [Hoàn tất] Similarity, 6-DOF Affine, 8-DOF Homography, Optical Flow, Elastic Mesh, Asterisms
└── 9. Computational RAW ──── [Hoàn tất] Bayer CFA Mosaic Fusion trước Demosaic (Google HDR+), Edge-Directed Demosaic
```

---

## 📑 Mục Lục
1. [Các Phân Hệ Tính Năng Cốt Lõi](#-các-phân-hệ-tính-năng-cốt-lõi)
2. [Giao Diện Đồ Họa (WPF Studio UI)](#-giao-diện-đồ-họa-wpf-studio-ui)
3. [Dòng Lệnh Đa Chế Độ (CLI Batch Processing)](#-dòng-lệnh-đa-chế-độ-cli-batch-processing)
4. [Cấu Trúc Source Code](#-cấu-trúc-source-code)
5. [Hướng Dẫn Cài Đặt & Biên Dịch](#-hướng-dẫn-cài-đặt--biên-dịch)
6. [Kiểm Thử & Chất Lượng](#-kiểm-thử--chất-lượng)
7. [Dữ Liệu Mẫu Kiểm Thử & Nguồn Dẫn](#-dữ-liệu-mẫu-kiểm-thử--nguồn-dẫn)

---

## 🌟 Các Phân Hệ Tính Năng Cốt Lõi

### 1. 🔍 Focus Stacking & 3D Depth Reconstruction
* **5 Thuật toán Fusion**: `Multi-Scale Laplacian Pyramid`, `HDR Focus & Exposure (Mertens Hybrid)`, `2D Wavelet DWT Fusion`, `Focus-Weighted Continuous Blend`, `Winner-Takes-All (WTA Fast)`.
* **4 Phương pháp đo nét**: `Modified Laplacian (SML)`, `Tenengrad (Sobel Gradient)`, `Local Variance (Texture)`, `2D Wavelet Sharpness`.
* **Khử lỗi quang học & Tự động phục hồi**: Motion-Aware Ghost Suppression, Occlusion Boundary Feathering, Edge Discontinuity Reconstruction, Artifact Hunter Scan.
* **Tái tạo 3D & Khẩu độ ảo**: Khẩu độ ảo `f/1.4 → f/64` từ Focus Volume 3D, xuất file 3D Point Cloud (`.ply`) và Surface Mesh (`.obj`) với pháp vector Sobel Normals.

### 2. ✨ Statistical Noise Stacking
* **Khử nhiễu đa khung hình**: Tăng tỉ số tín hiệu trên nhiễu (SNR) lên tới **+15dB** mà không làm mất chi tiết vi mô như các bộ lọc làm mờ không gian (spatial blur).
* **Đa thuật toán thống kê**:
  - `Kappa-Sigma Clipping (κ-σ)`: Lọc bỏ nhiễu xung cực hạn, cosmic ray, hot pixels với $\kappa \in [1.0\sigma, 5.0\sigma]$.
  - `SIMD Arithmetic Mean`: Trung bình cộng siêu tốc tối ưu AVX2/AVX-512 cho nhiễu Gaussian.
  - `Median Filter`: Lọc trung vị triệt tiêu nhiễu muối tiêu (salt-and-pepper).
  - `Winsorized Mean`: Trung bình giới hạn phân vị bền vững.
  - `Streaming Accumulator (Welford O(1) RAM)`: Tính trung bình & phương sai trực tiếp trên luồng không tốn bộ nhớ đệm RAM.

### 3. 🌈 Pure HDR Radiance & Tone Mapping
* **Mertens Multi-Scale Exposure Fusion**: Ghép đa mức phơi sáng giữ chi tiết vùng tối (shadows) và vùng sáng (highlights) tự nhiên không cần tính đường cong phản hồi cảm biến.
* **Debevec Physical Radiance Map**: Tái tạo bản đồ độ rọi vật lý tuyến tính thực thụ $E(x, y)$ và thời gian phơi sáng $t_k$.
* **Adaptive Motion Deghosting**: Nhận diện chuyển động giữa các bracket và khóa về frame chuẩn để triệt tiêu bóng ma chuyển động.
* **Tone Mapping Studio**: Đường cong điện ảnh `ACES Filmic`, `AgX High-Dynamic Range`, `Reinhard Extended`, và `Linear RAW Preserve`.

### 4. 🌌 Astro Deep-Sky Stacking & Alignment
* **Star Centroid Detector**: Ước lượng nền trời nhiễu qua Median/MAD, dò đỉnh lân cận 8 hướng, khớp Gaussian 2D subpixel, FWHM và hệ số tròn (Roundness $\ge 0.6$).
* **Asterism Triangle Matching**: Thuật toán tam giác sao bất biến với tỉ lệ cạnh $(L_1/L_3, L_2/L_3)$ ghép cặp sao tự động bất chấp góc xoay, dịch chuyển và bước nhảy góc ngắm kính thiên văn.
* **Khử nhiễu hiệu chuẩn quang học (Master Calibration)**: Tự động trừ Master Dark, chia Master Flat, trừ Master Bias trước khi xếp chồng.
* **Background Neutralization & MTF Auto-Stretch**: Tự động cân bằng nền trời và kéo giãn histogram phi tuyến tính làm nổi bật tinh vân (nebula) và dải ngân hà.

### 5. 🔭 HST Subpixel Drizzle Super-Resolution
* **Thuật toán Drizzle (Fruchter & Hook 2002)**: Tái tạo tuyến tính biến thiên diện tích pixel (Variable Pixel Linear Reconstruction — chuẩn kính viễn vọng không gian Hubble/HST).
* **Phóng to độ phân giải $2\times, 3\times, 4\times$**: Chiếu các "giọt" pixel co nhỏ (`pixfrac` $p \in [0.1, 1.0]$) lên lưới ma trận subpixel mục tiêu, tích phân diện tích giao nhau chính xác để khôi phục tần số quang học vượt giới hạn Nyquist mà không gây viền ringing hay tạo ảo ảnh.

### 6. ⚡ Optical Image Restoration (Deconvolution & Dehazing)
* **Richardson-Lucy Deconvolution**: Giải chập quang học lặp với hàm phân bố điểm PSF (`Gaussian`, `Defocus Disc`, `Airy Disk`, `Motion Blur`) và trọng số giảm chấn vi phân toàn phần Total Variation (TV) chống nhiễu hạt.
* **Dark Channel Prior Dehazing (He et al.)**: Ước lượng ánh sáng khí quyển toàn cục $\vec{A}$, tính bản đồ truyền dẫn $t(x)$ và làm mượt biên bằng bộ lọc hướng dẫn (Guided Filter) để khử sương mù, mờ hơi nước trong ảnh phong cảnh và macro.

### 7. 📸 Computational RAW (Bayer Burst Fusion)
* **Xử lý RAW cảm biến nguyên bản**: Quản lý mảng lọc màu CFA (`RGGB`, `BGGR`, `GRBG`, `GBRG`), Black Level, White Level, White Balance gains và ma trận hiệu chỉnh màu $3 \times 3$ Color Matrix (CCM).
* **Merge-before-Demosaic (Google HDR+ Pipeline)**: Ghép chồng đa khung hình trực tiếp trên lưới lọc Bayer trước khi Demosaicing $\to$ triệt tiêu hoàn toàn sự lan truyền nhiễu nội suy qua các điểm ảnh lân cận.
* **Edge-Directed Adaptive Demosaicing**: Nội suy kênh Green theo hướng gradient vi phân bậc 2, nội suy Red/Blue qua trường chênh lệch màu trơn tru ($R - G, B - G$) $\to$ triệt tiêu răng cưa (zippering) và sai lệch màu sắc (false color).

---

## 🎨 Giao Diện Đồ Họa (WPF Studio UI)

Giao diện Studio Dark Theme tương phản cao (.NET 9 WPF) với thanh điều hướng 5 Tab chuyên nghiệp:

```text
 ┌─────────────────────────────────────────────────────────────────────────────────────────────┐
 │ ⚡ FImageStack Pro   [📋 Scorecard Grade A+]   [🎯 Hunt Artifacts]   [Quick Samples...]     │
 ├──────────────┬───────────────────────────────────────────────────────────────┬──────────────┤
 │ FRAMES (50)  │ VIEW TABS: [✨ Fused] [🌊 Focus Wave] [🎯 Virtual DOF] [🧪 A/B Lab] │ [⚙️ Stack]    │
 │              ├───────────────────────────────────────────────────────────────┤ [🔬 Modes]    │
 │ #01 [94%] 👁️ │  CANVAS HIỂN THỊ CHÍNH (LayoutTransform Scale 10% → 1000%)    │ [🎨 Tone]     │
 │ #02 [96%] 👁️ │  ✦ Cuộn chuột để Zoom In / Zoom Out                           │ [🖌️ Retouch]  │
 │ #03 [98%] 👁️ │  ✦ Nhấp chuột giữa/phải để Pan kéo rê                         │ [📊 Metrics]  │
 │ #04 [91%] 👁️ │  ✦ Floating HUD Toolbar: [ ➖ | 100% | ➕ | Fit | 1:1 | 2:1 ]  ├──────────────┤
 │              │  ✦ HUD Pixel Probe: Tọa độ, Độ nét, DOF, Độ tin cậy           │ [⚡ PREVIEW]  │
 │              │  ✦ Live Dynamic Histogram, Highlight & Shadow Clipping Alerts │ [🚀 MASTER]   │
 └──────────────┴───────────────────────────────────────────────────────────────┴──────────────┘
```

- **Tab `⚙️ Stack`**: Chọn Preset cấu hình, thuật toán Focus Fusion, đo nét, canh chỉnh, AI Diagnostics.
- **Tab `🔬 Modes`**: Chuyển đổi linh hoạt giữa Focus Stacking, Statistical Noise, Pure HDR, Astro Stacking, HST Drizzle Super-Res, công cụ Dehaze, Deconvolve và xuất 3D Model (`.obj`/`.ply`).
- **Tab `🎨 Tone`**: Live Histogram, Tone Mapping ACES Filmic/AgX, Exposure, Contrast, Clarity, USM Sharpening, Saturation.
- **Tab `🖌️ Retouch`**: Cọ vẽ đè nét trực tiếp lên Canvas từ frame gốc kèm hệ thống Multi-Layer Undo/Redo.
- **Tab `📊 Metrics`**: Bảng 6 chỉ số chất lượng, danh sách Artifact Hotspots với nút `🎯 JUMP`.

---

## 💻 Dòng Lệnh Đa Chế Độ (CLI Batch Processing)

FImageStack CLI hỗ trợ xử lý tự động hàng loạt qua tham số `--mode`:

```bash
# 1. Focus Stacking cơ bản
FImageStack.Cli.exe --mode focus --input "data/macro_stack" --output "out/fused.tif" --method pyramid --analyze-quality --repair

# 2. Xuất mô hình 3D Surface Mesh (.obj) từ Focus Stack
FImageStack.Cli.exe --mode focus --input "data/macro_stack" --output "out/fused.tif" --export-3d "out/mesh.obj"

# 3. Statistical Noise Stacking (Khử nhiễu đa khung hình với Kappa-Sigma)
FImageStack.Cli.exe --mode noise --input "data/burst_shots" --output "out/clean.png" --noise-method kappasigma --kappa 2.5

# 4. Pure HDR Merge & Tone Mapping
FImageStack.Cli.exe --mode hdr --input "data/bracket_hdr" --output "out/hdr_aces.tif" --hdr-method mertens

# 5. Astrophotography Stacking (Sao, Dark/Flat Calibration & Auto-Stretch)
FImageStack.Cli.exe --mode astro --input "data/lights" --output "out/deepsky.tif" --astro-dark "data/darks" --astro-flat "data/flats"

# 6. HST Subpixel Drizzle Super-Resolution 2x
FImageStack.Cli.exe --mode drizzle --input "data/dithered_burst" --output "out/super_res_2x.png" --drizzle-scale 2.0 --drizzle-pixfrac 0.7

# 7. Optical Restoration (Khử sương mù Dehaze & Giải chập Deconvolve)
FImageStack.Cli.exe --mode restore --input "data/landscape" --output "out/restored.png" --dehaze --deconvolve --psf-radius 2.5
```

---

## 🏛️ Cấu Trúc Source Code

```text
FImageStack/
├── src/
│   ├── FImageStack.Core/             # Thuật toán Computational Photography & Optical Math
│   │   ├── Algorithms/               # Laplacian Pyramid, Wavelet DWT, Continuous Blend, WTA
│   │   ├── Alignment/                # Global Affine/Homography + Local Elastic Mesh
│   │   ├── Astro/                    # Star Detector, Triangle Asterism Alignment, Calibration
│   │   ├── Depth3D/                  # Sobel Normal Maps, PLY Point Clouds, OBJ 3D Meshes
│   │   ├── FocusMeasure/             # SML Laplacian, Tenengrad, Variance, Wavelet Sharpness
│   │   ├── Fusion/                   # Multi-Scale Pyramid & Wavelet Fusion Engines
│   │   ├── Hdr/                      # Mertens Fusion, Debevec Radiance, Motion Deghosting
│   │   ├── Lab/                      # A/B Stack Lab Multi-Algorithm Parallel Benchmarking
│   │   ├── Models/                   # ImageBuffer<T>, StackFrame, ProcessedStackResult
│   │   ├── Noise/                    # SIMD Mean, Median, Kappa-Sigma, Winsorized, Welford O(1)
│   │   ├── PostProcessing/           # ACES Filmic, AgX, Reinhard, Clarity, Color Matrix
│   │   ├── Presets/                  # Preset Profiles for Macro, Astro, HDR, Drizzle, High-Power
│   │   ├── Quality/                  # Artifact Hunter, Focus Wave 2D, Quality Predictor
│   │   ├── Raw/                      # Raw Bayer CFA Buffer, Burst Fusion, Edge-Directed Demosaic
│   │   ├── Reconstruction/           # Focus Breathing Compensation, Edge Reconstruction
│   │   ├── Refocus/                  # Focus Volume 3D, Virtual Focus, Synthetic Aperture Bokeh
│   │   ├── Restoration/              # Richardson-Lucy Deconvolution, PSF Generator, Dehazing
│   │   ├── Retouch/                  # Canvas Brush Painting & Multi-layer Undo/Redo
│   │   ├── SuperResolution/          # HST Subpixel Drizzle Super-Resolution Engine
│   │   └── Tiling/                   # Memory-Bounded Gigapixel Tiled Engine (100MP+)
│   ├── FImageStack.Application/      # StackService Orchestrator, ProjectService, Contracts
│   ├── FImageStack.Infrastructure/   # Image IO (RAW LibRaw/Tiff/Png), Fast Bitmaps, Exif
│   ├── FImageStack.UI/               # WPF Studio Dark Theme UI (.NET 9 MVVM)
│   └── FImageStack.Cli/              # Headless Multi-Mode CLI Automation Tool
├── tests/
│   └── FImageStack.Core.Tests/       # 114 Unit & Integration Tests (xUnit)
└── tools/
    └── FImageStack.DatasetGenerator/ # Bộ sinh dataset giả lập chuỗi macro 50 frames
```

---

## 📦 Hướng Dẫn Cài Đặt & Biên Dịch

### Yêu Cầu Môi Trường
- **Hệ điều hành**: Windows 10 / 11 (64-bit)
- **SDK phát triển**: [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

### 1. Build Dự Án & Chạy Unit Tests
```bash
# Clone repository
git clone https://github.com/kzxl/FImageStack.git
cd FImageStack

# Build toàn bộ solution
dotnet build

# Chạy toàn bộ 114 unit tests
dotnet test
```

### 2. Publish Bản Chạy (Hỗ Trợ Đủ 2 Tùy Chọn)

#### Option 1: Full (Self-Contained — Copy & Chạy ngay, không cần cài .NET Runtime)
```bash
# Publish WPF UI Studio
dotnet publish src/FImageStack.UI/FImageStack.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/FImageStack-UI-Full

# Publish CLI Batch Tool
dotnet publish src/FImageStack.Cli/FImageStack.Cli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/FImageStack-Cli-Full
```

#### Option 2: Lite (Framework-Dependent — Siêu nhẹ, yêu cầu máy cài sẵn .NET 9 Runtime)
```bash
# Publish WPF UI Studio
dotnet publish src/FImageStack.UI/FImageStack.UI.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./publish/FImageStack-UI-Lite

# Publish CLI Batch Tool
dotnet publish src/FImageStack.Cli/FImageStack.Cli.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./publish/FImageStack-Cli-Lite
```

---

## 🧪 Kiểm Thử & Chất Lượng

Hệ thống được bảo vệ bởi **114 Unit & Integration Tests** bao phủ toàn bộ các module:

```text
Passed!  - Failed: 0, Passed: 114, Skipped: 0, Total: 114, Duration: 658 ms - FImageStack.Core.Tests.dll (net9.0)
```

| Test Suite | Test Count | Nội dung kiểm thử |
| :--- | :---: | :--- |
| **`BayerRawFusionTests`** | 3 | Chuẩn hóa Black/White level, ghép Bayer CFA trước demosaic, nội suy vi phân cạnh |
| **`DrizzleSuperResTests`** | 2 | Tích phân diện tích giao nhau pixel drop, khôi phục lưới siêu phân giải $2\times$ |
| **`AstroStackTests`** | 3 | Dò tâm sao Gaussian subpixel, khớp tam giác sao asterism, trừ Master Dark/Flat |
| **`ImageRestorationTests`** | 6 | Tạo hàm PSF, giải chập Richardson-Lucy + TV damping, Dark Channel Dehazing |
| **`NoiseStackTests`** | 4 | SIMD Mean, Median, $\kappa$-$\sigma$ clipping loại hot pixel, Streaming Welford $O(1)$ |
| **`HdrStackTests`** | 3 | Mertens exposure fusion, Debevec radiance mapping, tone-mapping ACES Filmic |
| **`Depth3DTests`** | 3 | Tính pháp vector Sobel Normals, xuất file 3D Point Cloud PLY & Surface Mesh OBJ |
| **`Focus & Pipeline Tests`** | 90 | Pyramid, Wavelet, Elastic Mesh, Artifact Hunter, Virtual DOF, Tiling, Retouch |

---

## 📸 Dữ Liệu Mẫu Kiểm Thử & Nguồn Dẫn

Dự án tích hợp sẵn các bộ ảnh chụp thực tế tại thư mục `data/real_samples/`:

| Thư mục | Số Frame | Đối Tượng | Nguồn Trích Dẫn & Bản Quyền | Mục Tiêu Kiểm Thử |
| :--- | :---: | :--- | :--- | :--- |
| **`01_macro_beetle`** | **12 ảnh** | Bọ cánh cứng (Macro Beetle) | [Interactive Digital Photomontage Dataset (SIGGRAPH 2004)](https://grail.cs.washington.edu/projects/photomontage/) — Aseem Agarwala et al., University of Washington | Benchmark râu côn trùng, ranh giới phức tạp, khử bóng ma & viền tách lớp |
| **`02_macro_pcb_electronics`** | **10 ảnh** | Bo mạch điện tử SMD / IC | [focus-stack Benchmark Dataset](https://github.com/PetteriAimonen/focus-stack) — Petteri Aimonen | Đo độ sắc nét đường mạch vi mô, canh chỉnh sai lệch phối cảnh Homography |
| **`03_macro_specimen_36frames`** | **36 ảnh** | Tiêu bản lát cắt sinh học | [Focus Stacking Test Sequences](https://github.com/bznick98/Focus_Stacking) / Helicon Focus Tutorial Samples | Đo biểu đồ sóng 2D Focus Wave & Synthetic Aperture DOF |
| **`04_macro_specimen_fast5`** | **5 ảnh** | Mẫu vật macro bước dịch thưa | Helicon Focus Sample Sequences | Test tốc độ Fast Preview (0.3s) & chẩn đoán khoảng trống tiêu cự (Focus Gap) |

---

## 📄 Bản Quyền & Giấy Phép (License)

Dự án được phân phối dưới giấy phép **MIT License**. Mọi đóng góp, báo cáo lỗi (Issues) và yêu cầu kéo (Pull Requests) đều được hoan nghênh!
