using FImageStack.Core.DepthMap;
using FImageStack.Core.Models;

namespace FImageStack.Core.Fusion;

public sealed class RegionAdaptiveFusionEngine : IFusionEngine
{
    public FusionMethod Method => FusionMethod.RegionAdaptive;

    private readonly IFusionEngine _pyramidEngine = new MultiScalePyramidFusionEngine();
    private readonly IFusionEngine _confidenceEngine = new ConfidenceWeightedFusionEngine();
    private readonly IFusionEngine _occlusionEngine = new OcclusionAwareFusionEngine();

    public ImageBuffer<float> Fuse(
        IReadOnlyList<StackFrame> frames,
        DepthMapResult depthResult,
        FusionSettings settings)
    {
        return FuseCore(frames, depthResult, settings);
    }

    public ImageBuffer<float> FuseStack(
        IReadOnlyList<StackFrame> frames,
        FusionSettings settings,
        IProgress<StackProgress>? progress = null)
    {
        var depthEstimator = new StandardDepthMapEstimator();
        using var depthResult = depthEstimator.EstimateDepthMap(frames, settings.EnableDepthSmoothing, settings.SmoothingRadius);
        return FuseCore(frames, depthResult, settings, progress);
    }

    public unsafe ImageBuffer<float> FuseCore(
        IReadOnlyList<StackFrame> frames,
        DepthMapResult depthResult,
        FusionSettings settings,
        IProgress<StackProgress>? progress = null)
    {
        if (frames == null || frames.Count == 0)
            throw new ArgumentException("Stack contains no frames.", nameof(frames));

        int w = depthResult.Width;
        int h = depthResult.Height;
        int channels = frames[0].ColorBuffer?.Channels ?? 1;

        // 1. Generate 3 Component Fused Layers in Parallel
        progress?.Report(new StackProgress("Region Fusion", 10, "Synthesizing multi-algorithm candidate layers..."));

        ImageBuffer<float> imgPyramid = null!;
        ImageBuffer<float> imgDepth = null!;
        ImageBuffer<float> imgEdge = null!;

        Parallel.Invoke(
            () => imgPyramid = _pyramidEngine.Fuse(frames, depthResult, settings),
            () => imgDepth = _confidenceEngine.Fuse(frames, depthResult, settings),
            () => imgEdge = _occlusionEngine.Fuse(frames, depthResult, settings)
        );

        // 2. Classify Regions
        progress?.Report(new StackProgress("Region Fusion", 60, "Classifying semantic image regions..."));
        using var regionMap = ClassifyRegions(frames, imgDepth);

        // 3. Composite Final Image with Partition of Unity
        progress?.Report(new StackProgress("Region Fusion", 80, "Blending regional layers seamlessly..."));
        var output = new ImageBuffer<float>(w, h, channels);

        float* outPtr = output.DataPointer;
        float* pyrPtr = imgPyramid.DataPointer;
        float* depPtr = imgDepth.DataPointer;
        float* edgPtr = imgEdge.DataPointer;

        float* bgW = regionMap.BackgroundWeight.DataPointer;
        float* subW = regionMap.SubjectWeight.DataPointer;
        float* edgW = regionMap.EdgeWeight.DataPointer;

        Parallel.For(0, h, y =>
        {
            int rowOffset = y * w;
            for (int x = 0; x < w; x++)
            {
                int pIdx = rowOffset + x;
                float wBg = bgW[pIdx];
                float wSub = subW[pIdx];
                float wEdg = edgW[pIdx];

                for (int c = 0; c < channels; c++)
                {
                    int cIdx = pIdx * channels + c;
                    float blended = wBg * pyrPtr[cIdx] + wSub * depPtr[cIdx] + wEdg * edgPtr[cIdx];
                    outPtr[cIdx] = Math.Clamp(blended, 0f, 1f);
                }
            }
        });

        imgPyramid.Dispose();
        imgDepth.Dispose();
        imgEdge.Dispose();

        progress?.Report(new StackProgress("Region Fusion", 100, "Region-adaptive fusion completed."));
        return output;
    }

    public unsafe RegionClassificationMap ClassifyRegions(
        IReadOnlyList<StackFrame> frames,
        ImageBuffer<float> referenceComposite)
    {
        int w = frames[0].Width;
        int h = frames[0].Height;
        var map = new RegionClassificationMap(w, h);

        float* bgW = map.BackgroundWeight.DataPointer;
        float* subW = map.SubjectWeight.DataPointer;
        float* edgW = map.EdgeWeight.DataPointer;
        byte* prim = map.PrimaryRegionMap.DataPointer;

        int total = w * h;
        float[] maxSharpness = new float[total];
        int frameCount = frames.Count;

        for (int k = 0; k < frameCount; k++)
        {
            var f = frames[k];
            if (f.FocusMap != null)
            {
                float* p = f.FocusMap.DataPointer;
                for (int i = 0; i < total; i++)
                {
                    if (p[i] > maxSharpness[i]) maxSharpness[i] = p[i];
                }
            }
        }

        int bgCount = 0, subCount = 0, edgeCount = 0;

        for (int y = 0; y < h; y++)
        {
            int rowOffset = y * w;
            for (int x = 0; x < w; x++)
            {
                int idx = rowOffset + x;
                float conf = maxSharpness[idx];

                // Calculate edge intensity via omnidirectional difference on referenceComposite
                float gx = 0f, gy = 0f;
                if (x > 0 && x < w - 1 && y > 0 && y < h - 1)
                {
                    float c = referenceComposite.At(x, y);
                    float left = referenceComposite.At(x - 1, y);
                    float right = referenceComposite.At(x + 1, y);
                    float up = referenceComposite.At(x, y - 1);
                    float down = referenceComposite.At(x, y + 1);

                    gx = MathF.Max(MathF.Abs(right - c), MathF.Abs(c - left));
                    gy = MathF.Max(MathF.Abs(down - c), MathF.Abs(c - up));
                }

                float edgeMag = MathF.Sqrt(gx * gx + gy * gy);

                // 1. Edge Weight (Hair / High frequency edges)
                float alphaEdge = Math.Clamp((edgeMag - 0.04f) / 0.08f, 0f, 1f);

                // 2. Background Weight (Low confidence & low gradient)
                float alphaBg = (1f - alphaEdge) * Math.Clamp((0.20f - conf) / 0.15f, 0f, 1f);

                // 3. Subject Weight (Remaining solid in-focus body)
                float alphaSub = Math.Max(0f, 1f - alphaEdge - alphaBg);

                // Partition of unity normalization
                float sum = alphaEdge + alphaBg + alphaSub;
                if (sum > 0.001f)
                {
                    alphaEdge /= sum;
                    alphaBg /= sum;
                    alphaSub /= sum;
                }
                else
                {
                    alphaSub = 1f;
                }

                bgW[idx] = alphaBg;
                subW[idx] = alphaSub;
                edgW[idx] = alphaEdge;

                if (alphaEdge >= alphaBg && alphaEdge >= alphaSub)
                {
                    prim[idx] = (byte)SemanticRegionType.FineEdgeHair;
                    edgeCount++;
                }
                else if (alphaBg >= alphaSub)
                {
                    prim[idx] = (byte)SemanticRegionType.SmoothBackground;
                    bgCount++;
                }
                else
                {
                    prim[idx] = (byte)SemanticRegionType.SolidSubject;
                    subCount++;
                }
            }
        }

        map.BackgroundRatio = (float)bgCount / total;
        map.SubjectRatio = (float)subCount / total;
        map.EdgeRatio = (float)edgeCount / total;

        return map;
    }
}
