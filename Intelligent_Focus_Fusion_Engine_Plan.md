# Intelligent Focus Fusion Engine — Development Plan

## 0. Mục tiêu

Xây dựng ứng dụng desktop chuyên nghiệp cho focus stacking/focus fusion:

```text
Input nhiều ảnh focus-bracket
→ Validate
→ Align
→ Phân tích sharpness
→ Depth/Confidence Map
→ Focus Fusion
→ Artifact Detection
→ Auto Repair
→ Preview
→ Retouch
→ Export
```

Ưu tiên:

**Đúng → Ổn định → Nhanh → Chất lượng ảnh → Tự động hóa.**

Không bắt đầu bằng AI. Xây core xử lý ảnh thật tốt trước.

---

# 1. Kiến trúc tổng thể

```text
App
├── UI
├── Application
│   ├── ProjectService
│   ├── StackService
│   ├── PreviewService
│   └── ExportService
│
├── Core
│   ├── Image
│   ├── Alignment
│   ├── FocusMeasure
│   ├── DepthMap
│   ├── Fusion
│   ├── Artifact
│   └── Reconstruction
│
└── Infrastructure
    ├── ImageIO
    ├── Cache
    ├── Parallel
    └── GPU
```

**Nguyên tắc:**
- Core không phụ thuộc UI.
- Source image không được overwrite.
- Mọi bước xử lý phải có thể chạy độc lập.
- Pipeline phải hỗ trợ cancellation/progress.
- Có log và benchmark cho từng stage.
- Có thể thay implementation CPU bằng native/GPU về sau.

---

# 2. Phase 1 — Image Engine

## Mục tiêu

Xây các primitive xử lý ảnh.

```text
Image
Pixel
PixelFormat
ImageBuffer
ImageView
Tile
ROI
```

Hỗ trợ V1:

```text
JPEG
PNG
TIFF 8-bit
TIFF 16-bit
```

Internal processing ưu tiên:

```text
16-bit / float
```

API dự kiến:

```csharp
Image Load(string path);
void Save(Image image, string path);
Image Convert(Image image, PixelFormat format);
Image Crop(Image image, Rectangle roi);
```

Yêu cầu:
- Không load ảnh trùng nhiều lần nếu không cần.
- Có metadata.
- Có width/height/bit depth/color profile.
- Có cơ chế quản lý memory rõ ràng.

---

# 3. Phase 2 — Stack Loader & Project

Input:

```text
Folder
├── IMG001.jpg
├── IMG002.jpg
├── IMG003.jpg
└── ...
```

Model:

```csharp
StackProject
{
    List<ImageFrame> Frames;
    ImageMetadata Metadata;
    StackSettings Settings;
}
```

Tự động:
- Sort frame.
- Kiểm tra kích thước.
- Kiểm tra format.
- Detect duplicate.
- Detect corrupted image.
- Detect exposure bất thường.
- Detect frame không phù hợp.

Output:

```text
StackValidationResult
```

Project phải lưu:
- Source path.
- Frame order.
- Transform.
- Focus map.
- Depth map.
- Fusion settings.
- Artifact map.
- Retouch map.
- Export settings.

---

# 4. Phase 3 — Image Alignment

## V1

Implement:

```text
Translation
Rotation
Scale
```

Pipeline:

```text
Reference Frame
      ↓
Feature / Correlation
      ↓
Transform
      ↓
Warp
      ↓
Aligned Frame
```

Model:

```csharp
FrameTransform
{
    Matrix3x3 Global;
    LocalWarp Warp;
}
```

## V2

Thêm:

```text
Affine
Perspective
Local deformation
Optical flow
```

Yêu cầu:
- Không overwrite source.
- Transform phải reproducible.
- Có alignment score.
- Có confidence.
- Có thể xem overlay trước/sau alignment.

---

# 5. Phase 4 — Focus Measure

Implement ít nhất:

```text
Laplacian
Sobel / Tenengrad
Gradient
```

API:

```csharp
FocusMap CalculateFocus(Image image, FocusMethod method);
```

Normalize:

```text
0.0 = rất mờ
1.0 = rất nét
```

Lưu:

```text
FocusMap[frame][pixel]
```

Có thể sử dụng local window thay vì chỉ đánh giá từng pixel.

Yêu cầu:
- Có smoothing/noise suppression.
- Không để noise bị đánh giá nhầm là detail.
- Benchmark tốc độ từng method.

---

# 6. Phase 5 — Depth Map

Từ FocusMap:

```text
for each pixel:
    tìm frame có focus tốt nhất
    best frame index → depth
```

Output:

```text
DepthMap
ConfidenceMap
SourceFrameMap
```

Ví dụ:

```text
DepthMap[x,y]       = 37
ConfidenceMap[x,y]  = 0.94
SourceFrameMap[x,y] = 18
```

`SourceFrameMap` cực kỳ quan trọng cho retouch và artifact repair.

Cần xử lý:
- Noise trong depth map.
- Outlier.
- Focus gap.
- Smooth depth nhưng không làm mất edge.

---

# 7. Phase 6 — Focus Fusion V1

Implement 3 thuật toán:

## Algorithm A — Winner Takes All

```text
Chọn pixel/frame có sharpness cao nhất.
```

## Algorithm B — Focus Weighted Blend

```text
Pixel output = weighted combination
```

## Algorithm C — Multi-scale Pyramid Blend

```text
Low frequency
Mid frequency
High frequency
```

Blend từng frequency band.

API:

```csharp
Image Fuse(Stack stack, FusionSettings settings);
```

Settings:

```text
BestPixel
Weighted
Pyramid
```

Mục tiêu:
- Không seam.
- Giảm halo.
- Giữ texture.
- Giữ màu.
- Không tạo viền giả.

---

# 8. Phase 7 — Artifact Detection

Sau fusion:

```text
Final
 ↓
Edge Detection
 ↓
Compare source frames
 ↓
Detect artifact
```

Phát hiện:

```text
Halo
Ghost
Seam
Misalignment
Low-confidence region
Focus band
```

Model:

```csharp
ArtifactMap
{
    List<ArtifactRegion> Regions;
}
```

ArtifactRegion:

```text
Type
Rectangle / Mask
Confidence
SuggestedSourceFrame
Severity
```

Output UI:

```text
Detected 17 artifacts
- Halo: 6
- Ghost: 4
- Focus band: 5
- Misalignment: 2
```

---

# 9. Phase 8 — Auto Repair / Reconstruction

Pipeline:

```text
Original Stack
      ↓
Fusion
      ↓
Artifact Map
      ↓
Find best nearby source
      ↓
Generate mask
      ↓
Blend
      ↓
Final
```

Không sửa trực tiếp source.

Lưu:

```text
RepairLayer
RetouchMask
RetouchSourceMap
```

Cho phép:
- Auto Fix All.
- Fix từng artifact.
- Disable từng repair.
- Compare before/after.

Ưu tiên source frame khác trước khi reconstruction.

---

# 10. Phase 9 — Motion Detection

So sánh các frame sau alignment:

```text
Frame A
Frame B
 ↓
Difference
 ↓
Optical Flow
 ↓
Motion Map
```

Phân loại:

```text
Static
Moving
Unknown
```

Fusion:

```text
Static  → normal stacking
Moving  → chọn frame tốt nhất
```

Đặc biệt phục vụ:
- Hoa.
- Lá.
- Côn trùng.
- Ngoài trời.

Mục tiêu giảm ghost do chủ thể chuyển động.

---

# 11. Phase 10 — Stack Quality Analyzer

Sau khi load stack tự động phân tích:

```text
Frames:        80
Valid:         78
Duplicate:      1
Blurred:        1

Alignment:     97%
Focus coverage:94%
Motion:        LOW

Focus gaps:    None

Exposure:      OK
White balance: OK
```

Cảnh báo:

```text
⚠ Focus gap between frame 31 and 32
⚠ Motion detected around subject
⚠ Frame 47 has low sharpness
```

Phải có:
- Overall score.
- Frame score.
- Focus coverage.
- Alignment score.
- Motion score.
- Exposure consistency.
- Duplicate detection.

---

# 12. Phase 11 — Focus Gap Detection

Phân tích độ phủ focus:

```text
Frame 1 ─────────
Frame 2 ─────────
Frame 3 ─────────
Frame 4 ─────
Frame 5             ─────
```

Phát hiện khoảng trống giữa các focus plane.

Output:

```text
Focus gap detected
Between frame 31 and 32
Estimated missing range: X
```

V2 có thể đề xuất số frame cần chụp thêm.

---

# 13. Phase 12 — Tile Processing

Không xử lý toàn bộ ảnh trong RAM.

Chuẩn:

```text
Image
 ↓
512×512 / 1024×1024 tiles
 ↓
Process
 ↓
Cache
 ↓
Merge
```

Bắt buộc hỗ trợ:

```text
CancellationToken
Progress
Parallel processing
Memory limit
Disk cache
```

Mục tiêu:

```text
50 × 24MP
100 × 24MP
```

không crash vì RAM.

Có thể mở rộng:

```text
100 × 50MP+
```

về sau.

---

# 14. Phase 13 — Preview Engine

Không render full resolution khi user thay đổi parameter.

Pipeline:

```text
Original
 ↓
Downsample 25% / 12.5%
 ↓
Preview Engine
```

UI:

```text
┌───────────────────────────────┐
│                               │
│             IMAGE             │
│                               │
└───────────────────────────────┘

Method: [Pyramid ▼]

Sharpness: ████████░░

[Compare] [Depth] [Confidence]

                 [Render Full]
```

Preview:
- Chạy background.
- Có cancellation.
- Debounce khi user kéo slider.
- Không block UI.
- Có zoom/pan.

---

# 15. Phase 14 — Retouch

Cho phép:

```text
Brush Source
Erase Source
Restore
Clone
```

Quan trọng:

**Retouch không phá Final Image.**

Lưu:

```text
RetouchMask
RetouchSourceMap
```

Có:
- Undo/Redo.
- Brush size.
- Feather.
- Opacity.
- Source frame selection.
- Preview realtime ở resolution thấp.

---

# 16. Phase 15 — Export

V1:

```text
TIFF 16-bit
PNG
JPEG
```

V2:

```text
TIFF 32-bit
EXR
DNG
```

Giữ:
- Color profile.
- EXIF/metadata nếu phù hợp.
- Resolution.
- Bit depth.

Export background, có progress và cancellation.

---

# 17. Phase 16 — Performance

Chỉ tối ưu mạnh sau khi algorithm đúng.

Thứ tự:

```text
Correctness
 ↓
Profiling
 ↓
Multithreading
 ↓
SIMD
 ↓
Memory pooling
 ↓
Tile cache
 ↓
GPU
```

Benchmark:

```text
10 × 24MP
50 × 24MP
100 × 24MP
```

Metric:

```text
Load time
Alignment time
Focus-map time
Depth-map time
Fusion time
Artifact detection time
Repair time
Peak RAM
Output time
```

Mỗi stage phải có timing.

---

# 18. Phase 17 — Intelligent Fusion

Sau khi V1 ổn định, thay việc user tự chọn algorithm bằng engine tự phân tích:

```text
Stack Analysis
      ↓
Surface / Edge / Texture classification
      ↓
Choose fusion strategy
      ↓
Local fusion
```

Ví dụ:

```text
Smooth surface → DMap
Hair / texture → PMax
Strong edge → Edge-aware
Low contrast → Gradient
Noise region → Low-frequency blend
```

Mục tiêu:

**Không bắt user hiểu thuật toán.**

---

# 19. Phase 18 — Focus Breathing Correction

Khi focus thay đổi:

```text
Frame 1  → scale 1.000
Frame 20 → scale 1.012
Frame 40 → scale 1.025
```

Model:

```text
Focus position → Scale
```

Correction:

```text
Scale
Rotation
Perspective
Local deformation
```

Mục tiêu:
- Giảm alignment error.
- Giảm seam.
- Cải thiện macro stack.

---

# 20. Phase 19 — Noise-aware Fusion

Không chỉ chọn pixel sắc nhất.

Tính:

```text
Sharpness
+
Noise
+
Confidence
```

Có thể tận dụng nhiều frame để:
- Giảm noise.
- Giữ detail.
- Cải thiện vùng thiếu sáng.

Đặc biệt hữu ích cho macro ISO cao.

---

# 21. Phase 20 — Diffraction-aware Fusion

Hỗ trợ đánh giá:

```text
Aperture
Lens
Focus
Sharpness
Diffraction
```

Mục tiêu:

Không đơn giản:

```text
highest contrast = best pixel
```

mà:

```text
true detail confidence
```

V2 có thể hỗ trợ stack với aperture khác nhau.

---

# 22. Phase 21 — RAW Pipeline

Sau khi pipeline RGB ổn định:

```text
RAW
 ↓
Decode
 ↓
Demosaic
 ↓
Linear RGB
 ↓
Lens correction
 ↓
Alignment
 ↓
Focus analysis
 ↓
Fusion
 ↓
Color transform
 ↓
Output
```

Không nên stack JPEG rồi mới xử lý RAW.

Hỗ trợ về sau:

```text
CR2
CR3
ARW
ORF
RAF
NEF
DNG
```

Có thể dùng thư viện/native decoder thay vì tự viết RAW decoder.

---

# 23. Phase 22 — GPU

Sau khi CPU implementation có test chuẩn.

GPU candidates:

```text
Convolution
Gradient
Laplacian
Optical Flow
Warp
Pyramid
Blend
Resize
```

Kiến trúc:

```text
CPU
├── IO
├── Decode
├── Pipeline
└── Scheduling

GPU
├── Focus Measure
├── Alignment kernels
├── Pyramid
└── Fusion
```

Không đưa toàn bộ pipeline lên GPU.

---

# 24. Phase 23 — Camera Tethering / Adaptive Capture

V2/V3:

```text
Camera
 ↓
Capture
 ↓
Analyze focus coverage
 ↓
Estimate next focus position
 ↓
Capture
```

Thay vì cố định:

```text
1
2
3
4
5
...
100
```

Adaptive:

```text
1
2
3
5
8
13
...
```

Nếu focus thay đổi nhanh → bước nhỏ.

Nếu focus thay đổi chậm → bước lớn.

Có thể tích hợp focus rail về sau.

---

# 25. Phase 24 — Explainable Stack

Cho phép click vào pixel:

```text
X: 1245
Y: 873

Selected source: Frame #37

Sharpness:   0.94
Depth:       42.3
Confidence:  97%

Alternative:
Frame #36 → 0.87
Frame #38 → 0.82
```

Các layer có thể xem:

```text
Final
Source Frame
Depth
Confidence
Focus Map
Motion Map
Artifact Map
```

Tính năng này rất hữu ích để debug và retouch chuyên nghiệp.

---

# 26. Phase 25 — AI

**Chỉ làm sau khi classical algorithm đã ổn định.**

AI chỉ phụ trách:

```text
Artifact Detection
Artifact Classification
Edge Reconstruction
Ghost Removal
Noise-aware Repair
```

Không dùng AI để thay toàn bộ stacking engine.

Pipeline:

```text
Classical Stack
      ↓
Detect Problem
      ↓
AI Repair Candidate
      ↓
Confidence Check
      ↓
Apply / Reject
```

Phải có fallback về classical algorithm.

---

# 27. Test Dataset

Chuẩn bị dataset thực tế:

```text
Test 01: Macro vật thể tĩnh
Test 02: Hoa
Test 03: Lá
Test 04: Côn trùng
Test 05: Hair / fur
Test 06: Transparent object
Test 07: Background contrast thấp
Test 08: Camera movement
Test 09: Focus breathing
Test 10: Moving subject
Test 11: Noise / ISO cao
Test 12: 100-frame stack
Test 13: 200-frame stack
Test 14: 16-bit TIFF
Test 15: RAW
```

Mỗi dataset phải có:
- Input.
- Expected output.
- Benchmark.
- Artifact count.
- Quality score.

---

# 28. Unit Test / Regression Test

Mỗi thuật toán phải có test.

Ví dụ:

```text
AlignmentTest
FocusMeasureTest
DepthMapTest
FusionTest
ArtifactDetectionTest
MotionDetectionTest
TileProcessingTest
ExportTest
```

Không được thay đổi thuật toán làm giảm chất lượng mà không phát hiện.

Lưu một số golden output để regression test.

---

# 29. MVP Definition

MVP được coi là hoàn thành khi:

```text
100 ảnh
 ↓
Load
 ↓
Validate
 ↓
Align
 ↓
Focus Map
 ↓
Depth Map
 ↓
Pyramid Fusion
 ↓
Artifact Detection
 ↓
Auto Repair
 ↓
16-bit TIFF
```

Phải xử lý ổn định:

```text
24MP × 100 frames
```

và không crash do thiếu RAM trong điều kiện benchmark được định nghĩa.

---

# 30. Thứ tự triển khai bắt buộc

```text
01. Image Engine
02. Image Loader
03. Stack Project
04. Alignment
05. Focus Measure
06. Depth Map
07. Fusion
08. Artifact Detection
09. Auto Repair
10. Motion Detection
11. Quality Analyzer
12. Focus Gap Detection
13. Tile Processing
14. Preview
15. Retouch
16. Export
17. Benchmark
18. Intelligent Fusion
19. Focus Breathing
20. Noise-aware Fusion
21. RAW
22. GPU
23. Adaptive Capture
24. Explainable Stack
25. AI
```

**Không nhảy Phase nếu Phase trước chưa có test và benchmark.**

---

# 31. Product Direction

Không định vị sản phẩm đơn thuần là:

> "Một phần mềm focus stacking khác."

Định vị:

> **Intelligent Focus Fusion Engine**

Workflow mục tiêu:

```text
                  CAMERA
                    │
                    ▼
             Capture Analyzer
                    │
                    ▼
                RAW Stack
                    │
                    ▼
             Quality Analyzer
                    │
          ┌─────────┼─────────┐
          ▼         ▼         ▼
       Motion     Depth    Sharpness
        Map        Map       Map
          └─────────┼─────────┘
                    ▼
             Intelligent Fusion
                    │
                    ▼
           Artifact Detection
                    │
                    ▼
            Auto Reconstruction
                    │
                    ▼
             Professional Retouch
                    │
                    ▼
        TIFF / DNG / JPEG / EXR
```

Điểm khác biệt chính:

1. Tự động chọn chiến lược fusion.
2. Motion-aware stacking.
3. Depth-aware processing.
4. Auto artifact detection.
5. Auto repair.
6. Focus quality analysis.
7. Focus gap detection.
8. Focus breathing correction.
9. Tile/GPU processing cho stack cực lớn.
10. Explainable source/depth/confidence.
11. Sau cùng mới thêm AI.

---

# 32. Quy tắc dành cho Coding Agent

Agent phải tuân thủ:

1. Không viết UI trước Core.
2. Không đưa logic xử lý ảnh vào UI.
3. Không overwrite source.
4. Mỗi stage là module độc lập.
5. Mỗi stage có input/output rõ ràng.
6. Mọi stage hỗ trợ cancellation/progress nếu có thể.
7. Có unit test cho thuật toán.
8. Có benchmark cho thuật toán nặng.
9. Không tối ưu mù; phải profiling trước.
10. Không thêm dependency nếu chưa có lý do.
11. Không dùng AI thay cho thuật toán nền tảng.
12. Không thay đổi API công khai tùy tiện.
13. Mọi thay đổi algorithm phải chạy regression test.
14. Memory phải được kiểm soát với stack lớn.
15. Ưu tiên correctness trước performance.
16. Core phải có khả năng thay CPU implementation bằng native/GPU về sau.

---

# 33. Definition of Done cho từng Phase

Một Phase chỉ được đánh dấu `[DONE]` khi có đủ:

```text
[ ] Implementation
[ ] Unit Test
[ ] Error Handling
[ ] Logging
[ ] Benchmark nếu cần
[ ] Memory check nếu cần
[ ] Test dataset thực tế
[ ] Documentation ngắn
```

Agent phải báo cáo:

```text
Phase:
Status:
Implemented:
Tests:
Benchmark:
Known Issues:
Next Phase:
```

---

# 34. Mục tiêu cuối cùng

Sản phẩm hoàn thiện phải có khả năng:

```text
100–500+ frames
        ↓
Automatic analysis
        ↓
High quality alignment
        ↓
Focus/depth estimation
        ↓
Intelligent fusion
        ↓
Motion handling
        ↓
Artifact detection
        ↓
Automatic repair
        ↓
Manual professional retouch
        ↓
16/32-bit high-quality output
```

Mục tiêu không phải chỉ "ghép được nhiều ảnh".

Mục tiêu là xây dựng một **image fusion engine có khả năng tự hiểu frame nào tốt, pixel nào đáng tin cậy, vùng nào có lỗi và cách sửa lỗi**, sau đó mới mở rộng sang RAW, GPU, camera tethering và AI.
