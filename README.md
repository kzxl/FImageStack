# 🔬 FImageStack (FStack) — Next-Gen Computational Imaging & Pro Macro Photography Platform

[![.NET 9.0](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Android 15 Client](https://img.shields.io/badge/Android-Jetpack%20Compose%20%2B%20NDK-3DDC84?logo=android&logoColor=white)](android/)
[![WPF Studio](https://img.shields.io/badge/GUI-WPF%20Studio%20Dark-blue?logo=windows&logoColor=white)](src/FImageStack.UI/)
[![Tests](https://img.shields.io/badge/Unit%20Tests-114%2F114%20PASS%20(100%25)-10B981)](#-kiểm-thử--chất-lượng)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64%20%7C%20Android%20ARM64-0284C7)](https://github.com/)
[![License](https://img.shields.io/badge/License-MIT-purple.svg)](LICENSE)

**FImageStack** là một nền tảng **Nhiếp ảnh Tính toán (Computational Imaging Platform)** thế hệ mới, hỗ trợ song song cả ứng dụng **Desktop Studio (.NET 9 WPF + C++ Native)** và ứng dụng di động thực địa **Android Pro Macro Camera (Jetpack Compose + Camera2 + NDK SIMD)**.

Hệ thống tích hợp 8 phân hệ xử lý ảnh tính toán hiện đại: Focus Stacking siêu phân giải vi mô (Macro/Microscopy), Khử nhiễu thống kê đa khung hình (Statistical Noise), HDR dải tương phản cao (Mertens/Debevec), Thiên văn sâu (Astrophotography), Siêu phân giải không gian (HST Subpixel Drizzle), Phục hồi quang học (Deconvolution/Dehazing), Tái tạo mô hình 3D (PLY/OBJ) và Ghép chồng dữ liệu thô cảm biến (Computational RAW Bayer Fusion).

---

## 🏛️ Sơ Đồ Kiến Trúc Nền Tảng (Core Architecture)

```text
FImageStack Platform (Desktop Studio & Mobile Pro Camera)
│
├── 1. Focus Stacking ────────── Multi-Scale Laplacian Pyramid, Wavelet DWT, Focus Volume 3D, Virtual Aperture DOF
├── 2. Noise Stacking ────────── SIMD Mean, Median, Kappa-Sigma Clipping, Winsorized, Streaming O(1) RAM
├── 3. HDR Exposure Fusion ───── Mertens Multi-Scale Fusion, Debevec Physical Radiance, Motion Deghosting, ACES/AgX
├── 4. Astro Deep-Sky ────────── Star Centroid Detector, Asterism Triangles, Dark/Flat Calibration, MTF Auto-Stretch
├── 5. Super Resolution ──────── HST Subpixel Drizzle (Variable Pixel Linear Reconstruction) + Multi-frame IBP
├── 6. Optical Restoration ───── Richardson-Lucy Deconvolution + TV Damping, Dark Channel Prior Dehazing
├── 7. 3D Depth Reconstruction ─ Continuous Depth Maps, Sobel Surface Normals, Point Cloud (.ply), 3D Mesh (.obj)
├── 8. Image Alignment ───────── 6-DOF Affine, 8-DOF Homography, Optical Flow, Elastic Local Mesh Warping
├── 9. Computational RAW ─────── Bayer CFA Mosaic Fusion trước Demosaic (Google HDR+), Edge-Directed Demosaic
└── 10. Android Pro Camera ───── Camera2 Manual Focus Dial, Hardware SIMD Peaking, Sub-Part Mosaic Stacking
```

---

## 🖼️ Bộ Ảnh Mẫu Kiểm Thử Trực Quan (Visual Demo Samples)

Các kết quả thực nghiệm được xử lý trực tiếp từ các tập dữ liệu chụp thực tế trong thư mục `data/`:

### 1. Kiểm tra Vi mạch Điện tử & Đường dẫn SMD (PCB Electronics Inspection)
> Lấy nét toàn phần bo mạch vi điện tử, triệt tiêu quang sai và làm nổi bật từng chân hàn IC, cuộn cảm và tụ điện micro.

| Ảnh Ghép Nét Toàn Phần (Master Fused) | Bản Đồ Dò Nét Neon (Focus Peaking Stream) |
| :---: | :---: |
| ![PCB Master Result](data/macro_pcb_result.png) | ![PCB Peaking Stream](data/macro_pcb_peaking.png) |
| *Ảnh Master nét căng mọi tầng linh kiện* | *Luồng vi sai 2D Laplacian bắt cạnh nét thời gian thực* |

---

### 2. Tiêu bản Côn trùng & Sinh học (Biological Entomology Specimen)
> Xử lý cấu trúc râu, lông tơ và mắt kép phức tạp của bọ cánh cứng với thuật toán bù chuyển động và khử bóng ma đè lớp.

| Ảnh Ghép Nét Hoàn Chỉnh (Master Fused) | Bản Đồ Độ Sâu 3D (3D Depth Map) |
| :---: | :---: |
| ![Beetle Master Result](data/macro_beetle_result.png) | ![Depth Map](data/macro_depth_map.png) |
| *Khôi phục trọn vẹn râu và bề mặt bọ cánh cứng* | *Tái tạo độ sâu quang học phân lớp không gian 3D* |

---

### 3. Cấu trúc Sợi Vải Vi mô & Thực vật (Micro-Fabric & Botanical Macro)
> Bóc tách từng sợi chỉ dệt vi mô và chi tiết nhị hoa không bị nhòe mờ.

| Sợi Dệt Vi Mô (Micro-Fabric Textile) | Nhị Hoa Thực Vật (Botanical Bloom Macro) |
| :---: | :---: |
| ![Fabric Result](data/macro_fabric_result.png) | ![Flower Result](data/macro_flower_result.png) |
| *Cấu trúc sợi dệt tương phản siêu chi tiết* | *Độ chuyển nét tự nhiên từ tiền cảnh đến hậu cảnh* |

---

### 4. Ghép Nối Đa Mảnh Ma Trận (Sub-Part Mosaic Matrix Fusion)
> Chụp từng phân vùng mẫu vật lớn ở độ phóng đại cực cao và tự động ghép nối thành ảnh siêu phân giải không vết nối.

| Ghép Nối Ma Trận Đa Vùng (Multi-Region Mosaic Master) | So Sánh Khử Che Khuất (Occlusion Boundary Repair) |
| :---: | :---: |
| ![Mosaic Result](data/demo_multi_region_result.png) | ![Occlusion Compare](data/compare_occlusion.png) |
| *Ghép ma trận phân vùng liền mạch không vệt đen* | *Khử triệt để viền mờ halo khi các lớp chồng lên nhau* |

---

## 📱 Ứng Dụng Di Động Android (FImageStack Pro Mobile Client)

Nằm tại thư mục `android/`, ứng dụng Android mang sức mạnh xử lý Focus Stacking trực tiếp ra thực địa:

* **Công nghệ cốt lõi**: Kotlin + Jetpack Compose (Material 3 Dark Theme) + Camera2 API cấp thấp + NDK C++ SIMD (`-O3 -static-openmp`).
* **Lấy nét thủ công thời gian thực (Manual Focus & Diopter Dial)**:
  * Khóa hoàn toàn autofocus gây sai nét (`CONTROL_AF_MODE_OFF`).
  * Điều khiển trực tiếp motor thấu kính vật lý qua thanh trượt Diopter ($0.5\text{D} \rightarrow 10.0\text{D}$ tương đương $\infty \rightarrow 10\text{cm}$).
  * **Hiệu chuẩn 1 chạm (1-Tap Calibration)**: Nút `[SET NEAR]` và `[SET FAR]` để thiết lập chặn trên/chặn dưới cho chuỗi chụp Focus Bracketing tự động.
* **Focus Peaking phần cứng siêu tốc (Zero-Copy C++ SIMD)**:
  * Trích xuất trực tiếp kênh độ sáng **Y (Luminance)** từ luồng $60\text{fps}$ `ImageReader(YUV_420_888)`.
  * Vi sai 2D Laplacian với 5 bảng màu Neon (Xanh lá, Đỏ, Vàng, Cyan, Hồng) hoặc chế độ nền đen trắng (Monochrome).
* **Ghép nối Đa mảnh Ma trận (Sub-Part Mosaic Stacking)**:
  * Chế độ **`[🔲 MOSAIC]`** hỗ trợ chụp từng góc mẫu vật lớn ($2 \times 2$ Grid hoặc $1 \times 2$ Panorama).
  * Bản đồ thu nhỏ `SubPartMosaicHud` theo dõi tiến độ từng mảnh và tự động chuyển ô tiếp theo.
  * Bấm **`⚡ STITCH`** để tự động căn chỉnh và hòa trộn biên mượt mà (Seam Feathering).
* **Khung ngắm chuẩn tỉ lệ 1:1 không méo hình (True Sensor Aspect Ratio)**:
  * Tự động căn chỉnh theo cảm biến phần cứng ($4:3$, $16:9$, $1:1$), loại bỏ hoàn toàn hiện tượng kéo dãn hình ảnh.
  * Chế độ so sánh A/B (Split Comparison View) với thanh trượt tương tác hiển thị đúng tỷ lệ quang học.
* **Tự động lưu thư viện & Chia sẻ (Gallery Auto-Save & Native Share)**:
  * Tự động lưu ảnh Master JPEG $98\%$ vào album `Bộ nhớ máy > Pictures > FImageStack` qua chuẩn `MediaStore API`.
  * Nút Share tích hợp `FileProvider` gửi ảnh trực tiếp qua Zalo, Telegram, Google Drive, Gmail.

---

## 📑 Các Phân Hệ Thuật Toán Cốt Lõi

### 1. 🔍 Focus Stacking & 3D Depth Reconstruction
* **5 Thuật toán Fusion**: `Multi-Scale Laplacian Pyramid`, `HDR Focus & Exposure (Mertens Hybrid)`, `2D Wavelet DWT Fusion`, `Focus-Weighted Continuous Blend`, `Winner-Takes-All (WTA Fast)`.
* **4 Phương pháp đo nét**: `Modified Laplacian (SML)`, `Tenengrad (Sobel Gradient)`, `Local Variance (Texture)`, `2D Wavelet Sharpness`.
* **Khử lỗi quang học**: Motion-Aware Ghost Suppression, Occlusion Boundary Feathering, Edge Discontinuity Reconstruction, Artifact Hunter Scan.
* **Tái tạo 3D**: Khẩu độ ảo `f/1.4 → f/64` từ Focus Volume 3D, xuất file Point Cloud (`.ply`) và Surface Mesh (`.obj`) với pháp vector Sobel Normals.

### 2. ✨ Statistical Noise Stacking
* Tăng tỉ số tín hiệu trên nhiễu (SNR) lên tới **+15dB** qua đa thuật toán thống kê:
  - `Kappa-Sigma Clipping (κ-σ)`: Lọc bỏ tia vũ trụ, nhiễu xung, hot pixels.
  - `SIMD Arithmetic Mean`: Trung bình cộng tối ưu AVX2/AVX-512.
  - `Median Filter`: Triệt tiêu nhiễu muối tiêu.
  - `Streaming Accumulator (Welford O(1) RAM)`: Tính trung bình & phương sai không tốn RAM.

### 3. 🌈 Pure HDR Radiance & Tone Mapping
* **Mertens Multi-Scale Exposure Fusion**: Ghép phơi sáng tự nhiên không cần đường cong phản hồi cảm biến.
* **Debevec Physical Radiance Map**: Tái tạo bản đồ độ rọi vật lý tuyến tính $E(x, y)$ và thời gian $t_k$.
* **Tone Mapping Studio**: Đường cong điện ảnh `ACES Filmic`, `AgX High-Dynamic Range`, `Reinhard Extended`.

### 4. 🌌 Astro Deep-Sky Stacking & Alignment
* **Star Centroid Detector**: Khớp Gaussian 2D subpixel, FWHM và hệ số tròn (Roundness $\ge 0.6$).
* **Asterism Triangle Matching**: Thuật toán tam giác sao bất biến với góc xoay và dịch chuyển ngắm kính thiên văn.
* **Hiệu chuẩn quang học**: Tự động trừ Master Dark, chia Master Flat, trừ Master Bias, cân bằng nền trời và MTF Auto-Stretch.

### 5. 🔭 HST Subpixel Drizzle Super-Resolution
* **Thuật toán Drizzle (Fruchter & Hook 2002)**: Tái tạo tuyến tính biến thiên diện tích pixel (chuẩn kính Hubble/HST).
* Phóng to độ phân giải $2\times, 3\times, 4\times$ vượt giới hạn Nyquist mà không gây viền răng cưa.

### 6. ⚡ Optical Image Restoration (Deconvolution & Dehazing)
* **Richardson-Lucy Deconvolution**: Giải chập lặp với PSF (`Gaussian`, `Defocus Disc`, `Airy Disk`, `Motion Blur`) + Total Variation (TV) damping.
* **Dark Channel Prior Dehazing**: Ước lượng ánh sáng khí quyển toàn cục và làm mượt biên bằng Guided Filter.

### 7. 📸 Computational RAW (Bayer Burst Fusion)
* **Merge-before-Demosaic (Google HDR+ Pipeline)**: Ghép chồng đa khung hình trực tiếp trên lưới lọc Bayer trước khi Demosaicing.
* **Edge-Directed Adaptive Demosaicing**: Nội suy Green theo gradient bậc 2, nội suy Red/Blue qua trường chênh lệch màu trơn tru.

---

## 🎨 Giao Diện Desktop (WPF Studio UI)

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

---

## 💻 Dòng Lệnh Đa Chế Độ (CLI Batch Processing)

```bash
# 1. Focus Stacking cơ bản
FImageStack.Cli.exe --mode focus --input "data/macro_stack" --output "out/fused.tif" --method pyramid --analyze-quality --repair

# 2. Xuất mô hình 3D Surface Mesh (.obj) từ Focus Stack
FImageStack.Cli.exe --mode focus --input "data/macro_stack" --output "out/fused.tif" --export-3d "out/mesh.obj"

# 3. Statistical Noise Stacking (Khử nhiễu với Kappa-Sigma)
FImageStack.Cli.exe --mode noise --input "data/burst_shots" --output "out/clean.png" --noise-method kappasigma --kappa 2.5

# 4. Pure HDR Merge & Tone Mapping ACES
FImageStack.Cli.exe --mode hdr --input "data/bracket_hdr" --output "out/hdr_aces.tif" --hdr-method mertens

# 5. Astrophotography Stacking (Sao, Dark/Flat Calibration & Auto-Stretch)
FImageStack.Cli.exe --mode astro --input "data/lights" --output "out/deepsky.tif" --astro-dark "data/darks" --astro-flat "data/flats"

# 6. HST Subpixel Drizzle Super-Resolution 2x
FImageStack.Cli.exe --mode drizzle --input "data/dithered_burst" --output "out/super_res_2x.png" --drizzle-scale 2.0 --drizzle-pixfrac 0.7

# 7. Optical Restoration (Khử sương mù Dehaze & Giải chập Deconvolve)
FImageStack.Cli.exe --mode restore --input "data/landscape" --output "out/restored.png" --dehaze --deconvolve --psf-radius 2.5
```

---

## 📦 Hướng Dẫn Cài Đặt & Biên Dịch

### 1. Build Desktop Solution (.NET 9 C#)
```bash
# Clone repository
git clone https://github.com/kzxl/FImageStack.git
cd FImageStack

# Build toàn bộ solution
dotnet build

# Chạy toàn bộ 114 unit tests
dotnet test
```

### 2. Publish Desktop App (Full & Lite)
```bash
# Option 1: Full Self-Contained (Copy & Chạy ngay, không cần cài .NET)
dotnet publish src/FImageStack.UI/FImageStack.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/FImageStack-UI-Full

# Option 2: Lite Framework-Dependent (Siêu nhẹ)
dotnet publish src/FImageStack.UI/FImageStack.UI.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./publish/FImageStack-UI-Lite
```

### 3. Build & Cài Đặt Android App (.APK)
```powershell
# 1-Click Build Android APK (Root Batch script)
.\build_android.bat

# Hoặc dùng Gradle Wrapper trực tiếp:
cd android
.\gradlew.bat assembleDebug

# Cài đặt file APK trực tiếp vào điện thoại qua ADB:
adb install -r "android/app/build/outputs/apk/debug/app-debug.apk"
```

---

## 📸 Danh Mục Dữ Liệu Mẫu Kiểm Thử (Real Datasets)

Dự án tích hợp sẵn các bộ dữ liệu chụp thực tế trong thư mục `data/real_samples/`:

| Thư mục | Số Lượng Frame | Đối Tượng Chụp | Nguồn Trích Dẫn & Bản Quyền | Mục Tiêu Kiểm Thử |
| :--- | :---: | :--- | :--- | :--- |
| **`01_macro_beetle`** | **12 ảnh** | Bọ cánh cứng (Macro Beetle) | [Interactive Digital Photomontage (SIGGRAPH)](https://grail.cs.washington.edu/projects/photomontage/) | Cấu trúc râu côn trùng, ranh giới phức tạp, khử bóng ma & viền tách lớp |
| **`02_macro_pcb_electronics`** | **10 ảnh** | Bo mạch điện tử SMD / IC | [focus-stack Benchmark Dataset](https://github.com/PetteriAimonen/focus-stack) | Kiểm tra chân hàn vi mô, canh chỉnh phối cảnh Homography |
| **`03_macro_specimen_36frames`** | **36 ảnh** | Tiêu bản lát cắt sinh học sâu | Focus Stacking Test Sequences / Helicon Focus | Biểu đồ sóng 2D Focus Wave & Synthetic Aperture DOF |
| **`04_macro_specimen_fast5`** | **5 ảnh** | Mẫu vật macro bước thưa | Helicon Focus Sample Sequences | Test tốc độ Fast Preview (0.3s) & chẩn đoán khoảng trống tiêu cự |
| **`05_macro_flower_botanical`** | **14 ảnh** | Nhị hoa & Cánh hoa thực vật | Botanical Macro Archive | Độ mịn của vùng out-of-focus bokeh tự nhiên |
| **`06_macro_fabric_tie`** | **8 ảnh** | Cấu trúc dệt sợi vải vi mô | Macro Textile Research Suite | Khôi phục độ tương phản vi mô (Micro-detail boost) |
| **`07_macro_multi_depth`** | **16 ảnh** | Mẫu vật nhiều lớp che khuất | Multi-Depth Laboratory Series | Khử lỗi che khuất (Occlusion boundary feathering) |
| **`08_macro_nature_bloom`** | **12 ảnh** | Chồi non & Thực vật tự nhiên | Botanical Photography Series | Khử rung lắc lá cây và chuyển động môi trường |
| **`09_optical_multizone`** | **20 ảnh** | Tiêu bản đo độ chuẩn quang học | Optical Calibration Target | Đánh giá độ phân giải subpixel và quang sai thấu kính |

---

## 🧪 Kiểm Thử & Đảm Bảo Chất Lượng

```text
Passed!  - Failed: 0, Passed: 114, Skipped: 0, Total: 114, Duration: 658 ms - FImageStack.Core.Tests.dll (net9.0)
```

| Test Suite | Số Lượng Test | Nội dung kiểm thử |
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

## 📄 Bản Quyền & Giấy Phép (License)

Dự án được phân phối dưới giấy phép **MIT License**. Mọi đóng góp, báo cáo lỗi (Issues) và yêu cầu kéo (Pull Requests) đều được hoan nghênh!
