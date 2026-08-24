using FImageStack.Core.Models;

namespace FImageStack.Core.DepthMap;

public interface IDepthMapEstimator
{
    DepthMapResult EstimateDepthMap(IReadOnlyList<StackFrame> frames, bool enableSmoothing = true, int smoothRadius = 2);
}

public sealed class StandardDepthMapEstimator : IDepthMapEstimator
{
    public unsafe DepthMapResult EstimateDepthMap(IReadOnlyList<StackFrame> frames, bool enableSmoothing = true, int smoothRadius = 2)
    {
        if (frames == null || frames.Count == 0)
            throw new ArgumentException("Stack contains no frames.", nameof(frames));

        int width = frames[0].Width;
        int height = frames[0].Height;
        int frameCount = frames.Count;

        var result = new DepthMapResult(width, height);
        int* srcMap = result.SourceFrameMap.DataPointer;
        float* depthMap = result.DepthMap.DataPointer;
        float* confMap = result.ConfidenceMap.DataPointer;

        // Pointer cache for all focus maps
        float*[] focusPointers = new float*[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            if (frames[i].FocusMap == null)
                throw new InvalidOperationException($"Frame {i} does not have a computed FocusMap.");
            focusPointers[i] = frames[i].FocusMap!.DataPointer;
        }

        // Step 1: Find best frame index & compute initial confidence
        Parallel.For(0, height, y =>
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                int pixelIdx = rowOffset + x;
                float maxVal = -1f;
                int bestFrame = 0;
                float sumVal = 0f;

                for (int f = 0; f < frameCount; f++)
                {
                    float val = focusPointers[f][pixelIdx] * frames[f].PriorityWeight;
                    sumVal += val;
                    if (val > maxVal)
                    {
                        maxVal = val;
                        bestFrame = f;
                    }
                }

                srcMap[pixelIdx] = bestFrame;
                depthMap[pixelIdx] = frameCount > 1 ? (float)bestFrame / (frameCount - 1) : 0f;

                // Confidence: ratio of max sharpness to average sharpness
                float avgVal = sumVal / frameCount;
                confMap[pixelIdx] = avgVal > 1e-6f ? Math.Clamp((maxVal - avgVal) / (maxVal + 1e-6f), 0f, 1f) : 0f;
            }
        });

        // Step 2: Edge-preserving spatial smoothing (Bilateral / Majority Filter) if requested
        if (enableSmoothing && smoothRadius > 0 && frameCount > 1)
        {
            using var smoothedMap = new ImageBuffer<int>(width, height);
            int* dstSmoothed = smoothedMap.DataPointer;

            Parallel.For(0, height, y =>
            {
                int yMin = Math.Max(0, y - smoothRadius);
                int yMax = Math.Min(height - 1, y + smoothRadius);

                for (int x = 0; x < width; x++)
                {
                    int xMin = Math.Max(0, x - smoothRadius);
                    int xMax = Math.Min(width - 1, x + smoothRadius);
                    int currentIdx = y * width + x;
                    int centerFrame = srcMap[currentIdx];
                    float centerConf = confMap[currentIdx];

                    // If confidence is very high, keep original frame to preserve sharp edges
                    if (centerConf > 0.85f)
                    {
                        dstSmoothed[currentIdx] = centerFrame;
                        continue;
                    }

                    // Weighted voting in local neighborhood
                    float[] votes = new float[frameCount];
                    for (int wy = yMin; wy <= yMax; wy++)
                    {
                        int wOffset = wy * width;
                        for (int wx = xMin; wx <= xMax; wx++)
                        {
                            int nIdx = wOffset + wx;
                            int nFrame = srcMap[nIdx];
                            float nConf = confMap[nIdx];
                            votes[nFrame] += nConf + 0.1f;
                        }
                    }

                    int votedFrame = centerFrame;
                    float maxVote = -1f;
                    for (int f = 0; f < frameCount; f++)
                    {
                        if (votes[f] > maxVote)
                        {
                            maxVote = votes[f];
                            votedFrame = f;
                        }
                    }

                    dstSmoothed[currentIdx] = votedFrame;
                }
            });

            // Copy smoothed back to results
            Parallel.For(0, height, y =>
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    int idx = rowOffset + x;
                    int finalFrame = dstSmoothed[idx];
                    srcMap[idx] = finalFrame;
                    depthMap[idx] = (float)finalFrame / (frameCount - 1);
                }
            });
        }

        return result;
    }
}
