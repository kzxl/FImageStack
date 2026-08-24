using System.Runtime.CompilerServices;
using FImageStack.Core.Models;

namespace FImageStack.Core.FocusVolume;

public interface IFocusVolumeEngine
{
    FocusVolume BuildVolume(IReadOnlyList<StackFrame> frames);
    DepthMapResult ProcessVolume(FocusVolume volume, IReadOnlyList<StackFrame> frames, bool enable3DSmoothing = true, int smoothRadius = 2);
}

public sealed class FocusVolumeEngine : IFocusVolumeEngine
{
    private readonly Quality.IMultiFrameConsensusEngine _consensusEngine;
    private readonly IFocusTransitionFitter _transitionFitter;

    public FocusVolumeEngine(
        Quality.IMultiFrameConsensusEngine? consensusEngine = null,
        IFocusTransitionFitter? transitionFitter = null)
    {
        _consensusEngine = consensusEngine ?? new Quality.MultiFrameConsensusEngine();
        _transitionFitter = transitionFitter ?? new FocusTransitionFitter();
    }

    public FocusVolume BuildVolume(IReadOnlyList<StackFrame> frames)
    {
        if (frames == null || frames.Count == 0)
            throw new ArgumentException("Stack contains no frames.", nameof(frames));

        int width = frames[0].Width;
        int height = frames[0].Height;
        int frameCount = frames.Count;

        var volume = new FocusVolume(width, height, frameCount);

        for (int z = 0; z < frameCount; z++)
        {
            var frame = frames[z];
            if (frame.FocusMap == null)
                throw new InvalidOperationException($"Frame {z} does not have a computed FocusMap.");

            volume.SetSlice(z, frame.FocusMap);
        }

        return volume;
    }

    public unsafe DepthMapResult ProcessVolume(
        FocusVolume volume,
        IReadOnlyList<StackFrame> frames,
        bool enable3DSmoothing = true,
        int smoothRadius = 2)
    {
        // 0. Pre-filter temporal outlier spikes via Multi-Frame Consensus
        _consensusEngine.ApplyConsensusFilter(volume);

        int width = volume.Width;
        int height = volume.Height;
        int frameCount = volume.Slices;

        var result = new DepthMapResult(width, height)
        {
            FocusVolume = volume
        };

        int* srcMap = result.SourceFrameMap.DataPointer;
        float* depthMap = result.DepthMap.DataPointer;
        float* confMap = result.ConfidenceMap.DataPointer;
        float* dofMap = result.DofMap != null ? result.DofMap.DataPointer : null;
        float* gapMap = result.FocusGapMask != null ? result.FocusGapMask.DataPointer : null;
        float* r2Map = result.R2FitMap != null ? result.R2FitMap.DataPointer : null;

        float[] priorityWeights = new float[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            priorityWeights[i] = i < frames.Count ? frames[i].PriorityWeight : 1.0f;
        }

        // Step 1: Continuous Focus Transition Gaussian Modeling + Sub-frame peak fitting
        Parallel.For(0, height, y =>
        {
            Span<float> profile = stackalloc float[frameCount];
            int rowOffset = y * width;

            for (int x = 0; x < width; x++)
            {
                int pixelIdx = rowOffset + x;
                volume.CopyProfile(x, y, profile);

                // Apply priority weights
                for (int f = 0; f < frameCount; f++)
                {
                    profile[f] *= priorityWeights[f];
                }

                // 1. Fit Gaussian Focus Transition Model
                var model = _transitionFitter.FitTransition(profile);

                int discreteBestFrame = Math.Clamp((int)MathF.Round(model.OptimalMu), 0, frameCount - 1);
                float normalizedDepth = frameCount > 1 ? Math.Clamp(model.OptimalMu / (frameCount - 1), 0f, 1f) : 0f;

                // 2. Confidence calculation (ratio of amplitude vs total profile energy)
                float sumVal = 0f;
                for (int f = 0; f < frameCount; f++) sumVal += profile[f];
                float avgSharpness = sumVal / frameCount;

                float maxSharpness = model.PeakAmplitude + model.BaselineFloor;
                float confidence = avgSharpness > 1e-7f
                    ? Math.Clamp((maxSharpness - avgSharpness) / (maxSharpness + 1e-7f), 0f, 1f)
                    : 0f;

                // Modulate confidence with Goodness of Fit R^2
                confidence *= (0.5f + 0.5f * model.GoodnessOfFit);

                // 3. Focus Gap Detection
                bool isFocusGap = maxSharpness < 0.0005f || confidence < 0.10f || model.GoodnessOfFit < 0.20f;

                srcMap[pixelIdx] = discreteBestFrame;
                depthMap[pixelIdx] = normalizedDepth;
                confMap[pixelIdx] = confidence;

                if (dofMap != null)
                {
                    dofMap[pixelIdx] = (model.TransitionSpread * 2.355f) / Math.Max(1, frameCount - 1);
                }

                if (gapMap != null)
                {
                    gapMap[pixelIdx] = isFocusGap ? 1.0f : 0.0f;
                }

                if (r2Map != null)
                {
                    r2Map[pixelIdx] = model.GoodnessOfFit;
                }
            }
        });

        // Step 2: 3D Spatial-Depth Regularization if requested
        if (enable3DSmoothing && smoothRadius > 0 && frameCount > 1)
        {
            ApplySpatialDepthSmoothing(width, height, frameCount, smoothRadius, srcMap, depthMap, confMap);
        }

        return result;
    }

    private static unsafe void ApplySpatialDepthSmoothing(
        int width,
        int height,
        int frameCount,
        int smoothRadius,
        int* srcMap,
        float* depthMap,
        float* confMap)
    {
        using var smoothedDepth = new ImageBuffer<float>(width, height);
        using var smoothedSrc = new ImageBuffer<int>(width, height);
        float* dstDepth = smoothedDepth.DataPointer;
        int* dstSrc = smoothedSrc.DataPointer;

        Parallel.For(0, height, y =>
        {
            int yMin = Math.Max(0, y - smoothRadius);
            int yMax = Math.Min(height - 1, y + smoothRadius);

            for (int x = 0; x < width; x++)
            {
                int xMin = Math.Max(0, x - smoothRadius);
                int xMax = Math.Min(width - 1, x + smoothRadius);
                int currentIdx = y * width + x;
                float centerConf = confMap[currentIdx];
                float centerDepth = depthMap[currentIdx];
                int centerFrame = srcMap[currentIdx];

                // Preserve high-confidence edges
                if (centerConf > 0.85f)
                {
                    dstDepth[currentIdx] = centerDepth;
                    dstSrc[currentIdx] = centerFrame;
                    continue;
                }

                // Bilateral depth filter: spatial distance + depth difference weighting
                float weightSum = 0f;
                float depthSum = 0f;

                for (int wy = yMin; wy <= yMax; wy++)
                {
                    int wOffset = wy * width;
                    for (int wx = xMin; wx <= xMax; wx++)
                    {
                        int nIdx = wOffset + wx;
                        float nConf = confMap[nIdx];
                        float nDepth = depthMap[nIdx];

                        float spatialDistSq = (wx - x) * (wx - x) + (wy - y) * (wy - y);
                        float depthDiff = MathF.Abs(nDepth - centerDepth);

                        float spatialWeight = MathF.Exp(-spatialDistSq / (2f * smoothRadius * smoothRadius));
                        float depthWeight = MathF.Exp(-depthDiff * depthDiff / 0.05f);
                        float w = spatialWeight * depthWeight * (nConf + 0.1f);

                        depthSum += nDepth * w;
                        weightSum += w;
                    }
                }

                float finalDepth = weightSum > 1e-6f ? depthSum / weightSum : centerDepth;
                dstDepth[currentIdx] = finalDepth;

                // Re-derive smoothed discrete frame from smoothed continuous depth
                int smoothedFrame = (int)MathF.Round(finalDepth * (frameCount - 1));
                dstSrc[currentIdx] = Math.Clamp(smoothedFrame, 0, frameCount - 1);
            }
        });

        // Copy back to result buffers
        Parallel.For(0, height, y =>
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                int idx = rowOffset + x;
                depthMap[idx] = dstDepth[idx];
                srcMap[idx] = dstSrc[idx];
            }
        });
    }
}
