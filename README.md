# 🔬 FImageStack (FStack) — Next-Gen Macro Focus Stacking & Computational Photography Suite

[![.NET 9.0](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WPF GUI](https://img.shields.io/badge/GUI-WPF%20Studio%20Dark-blue?logo=windows&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
[![Tests](https://img.shields.io/badge/Unit%20Tests-90%2F90%20PASS%20(100%25)-10B981)](#-kiểm-thử--chất-lượng)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0284C7?logo=windows)](https://github.com/)

**FImageStack** là bộ giải pháp và công cụ xử lý chồng nét ảnh vi mô (Macro Focus Stacking) thế hệ mới, tối ưu cho nhiếp ảnh macro, hiển vi quang học phòng lab, trang sức và tài liệu bảo tàng. Ứng dụng kết hợp giữa **toán học quang học chính xác** và **giao diện Studio Dark Theme tương phản cao**.

---

## 📑 Mục Lục
1. [Tính Năng Nổi Bật](#-tính-năng-nổi-bật)
2. [Kiến Trúc Hệ Thống](#-kiến-trúc-hệ-thống)
3. [Giao Diện Đồ Họa (WPF Studio UI)](#-giao-diện-đồ-họa-wpf-studio-ui)
4. [Dòng Lệnh (CLI Batch Processing)](#-dòng-lệnh-cli-batch-processing)
5. [Hướng Dẫn Cài Đặt & Biên Dịch](#-hướng-dẫn-cài-đặt--biên-dịch)
6. [Kiểm Thử & Chất Lượng](#-kiểm-thử--chất-lượng)
7. [Dữ Liệu Mẫu Kiểm Thử & Nguồn Dẫn](#-dữ-liệu-mẫu-kiểm-thử--nguồn-dẫn-sample-datasets--attribution)

---

## 🌟 Tính Năng Nổi Bật

### 1. Đa Thuật Toán Fusion & Đo Nét (Focus Measures)
- **5 Thuật toán Fusion**:
  - `Multi-Scale Laplacian Pyramid`: Tách dải tần số không gian đa mức, giữ chi tiết vi mô cực nét không bị vỡ hạt.
  - `HDR Focus & Exposure (Mertens Hybrid)`: Kết hợp chồng nét tiêu cự và cân bằng dải tương phản động HDR.
  - `2D Wavelet DWT Fusion`: Ghép chi tiết dựa trên biến đổi sóng con Wavelet rời rạc 2D.
  - `Focus-Weighted Continuous Blend`: Hòa trộn trọng số mềm mượt chuyển tiếp tiêu cự.
  - `Winner-Takes-All (WTA Fast)`: Thuật toán tốc độ cao chọn frame nét nhất từng pixel.
- **4 Phương pháp đo nét**: `Modified Laplacian (SML)`, `Tenengrad (Sobel Gradient)`, `Local Variance (Texture)`, `2D Wavelet Sharpness`.

### 2. Canh Chỉnh & Khử Rung Quang Học (Optical Alignment)
- **5 Chế độ Alignment**: `Similarity (Scale/Rot/Trans)`, `Affine (6-DOF)`, `Homography (8-DOF Perspective)`, `Translation Only`, `Locked Tripod`.
- **Local Elastic Mesh Alignment**: Lưới ma trận $8 \times 8$ bù trừ chuyển động vi mô từng vùng do rung gió hoặc bước dịch ray macro 1:1.
- **Lens Distortion Correction**: Khử méo cong ống kính theo mô hình quang học Brown-Conrady (Radial $k_1, k_2$ & Tangential $p_1, p_2$).

### 3. Khử Lỗi Quang Học & Tự Động Phục Hồi (Auto-Repair)
- **Motion-Aware Ghost Suppression**: Khóa vùng chuyển động (cánh côn trùng, lá cây) vào duy nhất 1 frame nét nhất để triệt tiêu bóng ma (ghosting).
- **Occlusion-Aware Handling & Depth Boundary Feathering**: Tự động nhận diện và làm mượt viền cạnh chồng lấp giữa tiền cảnh và hậu cảnh, chống hào quang (halos).
- **Edge Discontinuity Reconstruction**: Tái tạo đường biên bị đứt gãy hoặc răng cưa từ frame nguồn tối ưu.

### 4. Tính Năng Quang Học Tiên Tiến
- **🎯 Artifact Hunter (Pre-Stack Diagnostics)**: Chẩn đoán 1s trước khi render $\to$ đo 6 rủi ro quang học (`Ghost`, `Halo`, `Motion`, `Blur`, `Alignment`, `Exposure`) và phát hiện chính xác frame gây lỗi.
- **🌊 2D Spatio-Temporal Focus Wave Graph**: Biểu đồ trực quan hóa sóng tiêu cự 2D và đo độ đều của bước dịch ray (`Step Uniformity Score`).
- **🎯 Virtual Focus & Synthetic Aperture DOF**: Hậu kỳ tiêu cự sau khi chụp — kéo thanh trượt khẩu độ ảo `f/1.4 → f/64` và dải độ sâu `[Z_min, Z_max]` để xóa phông nghệ thuật từ Focus Volume 3D thực tế.
- **🧪 A/B Stack Lab**: Chạy đua song song 5 thuật toán chồng nét trên cùng 1 chuỗi ảnh, so sánh $100\%$ pixel và tự động chấm điểm xếp hạng.
- **🖼️ Gigapixel Memory-Bounded Tiled Processing**: Tự động chia lưới gạch (512, 1024, 2048, 4096px) xử lý ảnh siêu phân giải 100MP+ mà không bị tràn RAM.

---

## 🏛️ Kiến Trúc Hệ Thống

```text
FImageStack/
├── src/
│   ├── FImageStack.Core/             # 25 thuật toán Fusion, Alignment, Quality, Refocus, Lab
│   │   ├── Algorithms/               # Laplacian Pyramid, Wavelet DWT, HDR Mertens
│   │   ├── Alignment/                # Global Affine/Homography + Local Elastic Mesh
│   │   ├── Calibration/              # Brown-Conrady Lens Distortion Correction
│   │   ├── Lab/                      # A/B Stack Lab Multi-Algorithm Benchmarking
│   │   ├── Quality/                  # Artifact Hunter, Focus Wave, Quality Predictor
│   │   ├── Refocus/                  # Virtual Focus & Synthetic Aperture Bokeh
│   │   ├── Pipeline/                 # Tiled Gigapixel Engine & Caching Graph
│   │   └── Retouch/                  # Canvas Brush Painting & Multi-layer Undo/Redo
│   ├── FImageStack.Application/      # StackService, ProjectService, DTOs & Contracts
│   ├── FImageStack.Infrastructure/   # RAW / TIFF 16-bit / PNG Image IO & Exif Parsing
│   ├── FImageStack.UI/               # WPF Studio Dark Theme UI (.NET 9)
│   └── FImageStack.Cli/              # Headless CLI Tool for Automation & Scripting
├── tests/
│   └── FImageStack.Core.Tests/       # 90 Unit & Integration Tests (xUnit)
└── tools/
    └── FImageStack.DatasetGenerator/ # Bộ sinh dataset giả lập chuỗi macro 50 frames
```

---

## 🎨 Giao Diện Đồ Họa (WPF Studio UI)

Giao diện Studio Dark Theme độ tương phản cao với quy trình làm việc chuẩn chuyên nghiệp:

```text
 ┌─────────────────────────────────────────────────────────────────────────────────────────────┐
 │ ⚡ FImageStack Pro   [📋 Scorecard Grade A+]   [🎯 Hunt Artifacts]   [Quick Samples...]     │
 ├──────────────┬───────────────────────────────────────────────────────────────┬──────────────┤
 │ FRAMES (50)  │ VIEW TABS: [✨ Fused] [🌊 Focus Wave] [🎯 Virtual DOF] [🧪 A/B Lab] │ [⚙️ Stack]    │
 │              ├───────────────────────────────────────────────────────────────┤ [🎨 Tone]     │
 │ #01 [94%] 👁️ │  CANVAS HIỂN THỊ CHÍNH (LayoutTransform Scale 10% → 1000%)    │ [🖌️ Retouch]  │
 │ #02 [96%] 👁️ │  ✦ Cuộn chuột để Zoom In / Zoom Out                           │ [📊 Metrics]  │
 │ #03 [98%] 👁️ │  ✦ Nhấp chuột giữa/phải để Pan kéo rê                         ├──────────────┤
 │ #04 [91%] 👁️ │  ✦ Floating HUD Toolbar: [ ➖ | 100% | ➕ | Fit | 1:1 | 2:1 ]  │ [⚡ PREVIEW]  │
 │              │  ✦ HUD Pixel Probe: Tọa độ, Độ nét, DOF, Độ tin cậy           │ [🚀 MASTER]   │
 └──────────────┴───────────────────────────────────────────────────────────────┴──────────────┘
```

- **Vùng Canvas & Zoom**: Hỗ trợ cuộn chuột phóng to/thu nhỏ mượt mà từ $10\%$ đến $1000\%$, rê kéo ảnh bằng chuột giữa/phải, phím tắt `Space`, thanh công cụ Floating HUD (`Fit`, `1:1`, `2:1`).
- **Sidebar 4 Tab**:
  - `⚙️ Stack`: Chọn Preset (Macro, Landscape, Ultra-Precision), thuật toán Fusion, đo nét, canh chỉnh, AI Diagnostics.
  - `🎨 Tone`: Biểu đồ Histogram thời gian thực, Tone Mapping ACES Filmic/AgX, Exposure, Contrast, Clarity, USM Sharpening, Saturation.
  - `🖌️ Retouch`: Cọ vẽ đè nét trực tiếp lên Canvas từ frame gốc kèm Undo/Redo.
  - `📊 Metrics`: Bảng 5 chỉ số chất lượng, danh sách Artifact Hotspots với nút `🎯 JUMP`.
- **Pinned Action Dock**: Các nút `⚡ FAST PREVIEW` (1280px / 0.3s) và `🚀 FULL MASTER RENDER` cố định ở đáy, không bao giờ bị cuộn mất.

---

## 💻 Dòng Lệnh (CLI Batch Processing)

FImageStack cung cấp công cụ dòng lệnh đầy đủ tính năng:

```bash
# Xử lý thư mục ảnh macro với thuật toán Laplacian Pyramid
FImageStack.Cli.exe -i "D:\MacroShots\Sequence_01" -o "D:\Output\fused_result.tif" -m Pyramid --align Similarity --ghost --edge-fix

# Xem toàn bộ tham số dòng lệnh
FImageStack.Cli.exe --help
```

### Các tùy chọn chính:
- `-i, --input`: Đường dẫn thư mục chứa chuỗi ảnh frame.
- `-o, --output`: Đường dẫn lưu ảnh kết quả (`.tif`, `.png`, `.jpg`).
- `-m, --method`: Thuật toán Fusion (`Pyramid`, `Wavelet`, `HDR`, `FocusWeighted`, `WTA`).
- `--focus-measure`: Phương pháp đo nét (`ModifiedLaplacian`, `Tenengrad`, `LocalVariance`, `Wavelet`).
- `--align`: Chế độ canh chỉnh (`Similarity`, `Affine`, `Homography`, `TranslationOnly`, `None`).
- `--ghost`: Bật khử bóng ma chuyển động (Motion-Aware Ghost Suppression).
- `--edge-fix`: Bật tái tạo viền sắc cạnh (Edge Discontinuity Reconstruction).
- `--tiled`: Kích hoạt chế độ chia ô chống tràn RAM cho ảnh gigapixel.

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

# Chạy toàn bộ 90 unit tests
dotnet test
```

### 2. Publish Bản Chạy (Hỗ Trợ Đủ 2 Tùy Chọn)

#### Option 1: Full (Self-Contained — Copy & Chạy ngay, không cần cài .NET)
```bash
# Publish WPF UI
dotnet publish src/FImageStack.UI/FImageStack.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/FImageStack-UI-Full

# Publish CLI Tool
dotnet publish src/FImageStack.Cli/FImageStack.Cli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/FImageStack-Cli-Full
```

#### Option 2: Lite (Framework-Dependent — Siêu nhẹ ~2.6 MB, yêu cầu máy đã cài .NET 9 Runtime)
```bash
# Publish WPF UI
dotnet publish src/FImageStack.UI/FImageStack.UI.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./publish/FImageStack-UI-Lite

# Publish CLI Tool
dotnet publish src/FImageStack.Cli/FImageStack.Cli.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./publish/FImageStack-Cli-Lite
```

---

## 🧪 Kiểm Thử & Chất Lượng

Hệ thống được bảo vệ bởi **90 Unit & Integration Tests** bao phủ toàn bộ các tầng xử lý:

```text
Passed!  - Failed: 0, Passed: 90, Skipped: 0, Total: 90, Duration: 705 ms
```

- `Algorithms`: Laplacian Pyramid Multi-Scale, Wavelet DWT, Mertens HDR.
- `Alignment`: Similarity, 6-DOF Affine, 8-DOF Homography, 8x8 Elastic Mesh.
- `Quality`: Artifact Hunter Scan, Focus Wave Spatio-Temporal, Shot Quality Predictor.
- `Refocus`: Synthetic Aperture Circle-of-Confusion Bokeh, Continuous Depth Slicing.
- `Lab`: A/B Stack Lab Multi-Algorithm Parallel Engine & Composite Scoring.
- `Pipelines`: Gigapixel Tiling, Memory-Bounded Partitioning, Exif & Raw Metadata.

---

## 📸 Dữ Liệu Mẫu Kiểm Thử & Nguồn Dẫn (Sample Datasets & Attribution)

Dự án tích hợp sẵn các bộ ảnh chụp macro thực tế được tổ chức tại thư mục `data/real_samples/` phục vụ cho việc kiểm thử chất lượng và benchmark thuật toán:

| Thư mục | Số Frame | Đối Tượng | Nguồn Trích Dẫn & Bản Quyền | Mục Tiêu Kiểm Thử |
| :--- | :---: | :--- | :--- | :--- |
| **`01_macro_beetle`** | **12 ảnh** | Bọ cánh cứng (Macro Beetle) | [Interactive Digital Photomontage Dataset (SIGGRAPH 2004)](https://grail.cs.washington.edu/projects/photomontage/) — Aseem Agarwala et al., University of Washington | Benchmark râu côn trùng, ranh giới phức tạp, khử bóng ma & viền tách lớp |
| **`02_macro_pcb_electronics`** | **10 ảnh** | Bo mạch điện tử SMD / IC | [focus-stack Benchmark Dataset](https://github.com/PetteriAimonen/focus-stack) — Petteri Aimonen | Đo độ sắc nét đường mạch vi mô, canh chỉnh sai lệch phối cảnh Homography |
| **`03_macro_specimen_36frames`** | **36 ảnh** | Tiêu bản lát cắt sinh học | [Focus Stacking Test Sequences](https://github.com/bznick98/Focus_Stacking) / Helicon Focus Tutorial Samples | Đo biểu đồ sóng 2D Spatio-Temporal Focus Wave & Synthetic Aperture DOF |
| **`04_macro_specimen_fast5`** | **5 ảnh** | Mẫu vật macro bước dịch thưa | Helicon Focus Sample Sequences | Test tốc độ Fast Preview (0.3s) & chẩn đoán khoảng trống tiêu cự (Focus Gap) |

### Các Nguồn Dataset Mở Đề Xuất Khác:
- **[araujoalexandre/FocusStackingDataset](https://github.com/araujoalexandre/FocusStackingDataset)**: 94 chuỗi ảnh RAW macro bracketing kèm pseudo-ground truth.
- **[Zerene Stacker Educational Tutorials](https://zerenesystems.com/cms/stacker/docs/tutorials)**: Kho ảnh tiêu bản côn trùng và khoáng sản vi mô.
- **[Magic Lantern Focus Stacking Group](http://www.magiclantern.fm/forum/)**: Dữ liệu chụp macro tự động từ máy ảnh Canon DSLR.

---

## 📄 Bản Quyền & Giấy Phép
Dự án phát triển nội bộ cho quy trình xử lý ảnh chất lượng cao. Giữ toàn quyền bản quyền © 2026.
