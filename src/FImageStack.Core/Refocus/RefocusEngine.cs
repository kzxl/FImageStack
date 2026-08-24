using FImageStack.Core.DepthMap;
using FImageStack.Core.Models;

namespace FImageStack.Core.Refocus;

public interface IRefocusEngine
{
    RefocusPointResult QueryFocusAtPoint(
        DepthMapResult depthResult,
        IReadOnlyList<StackFrame> frames,
        int x,
        int y);

    ImageBuffer<float> RenderSyntheticAperture(
        DepthMapResult depthResult,
        IReadOnlyList<StackFrame> frames,
        SyntheticApertureParams parameters);

    ImageBuffer<float> RenderSelectiveDofRange(
        DepthMapResult depthResult,
        IReadOnlyList<StackFrame> frames,
        float minDepth,
        float maxDepth);
}

public sealed class RefocusEngine : IRefocusEngine
{
    public RefocusPointResult QueryFocusAtPoint(
        DepthMapResult depthResult,
        IReadOnlyList<StackFrame> frames,
        int x,
        int y)
    {
        if (depthResult == null) throw new ArgumentNullException(nameof(depthResult));
        if (frames == null || frames.Count == 0) throw new ArgumentException("Frames list cannot be empty.", nameof(frames));

        int w = depthResult.Width;
        int h = depthResult.Height;
        x = Math.Clamp(x, 0, w - 1);
        y = Math.Clamp(y, 0, h - 1);

        float depth = depthResult.DepthMap.At(x, y);
        float confidence = depthResult.ConfidenceMap.At(x, y);
        int closestFrame = Math.Clamp((int)MathF.Round(depth), 0, frames.Count - 1);

        return new RefocusPointResult
        {
            X = x,
            Y = y,
            ContinuousDepth = depth,
            ClosestFrameIndex = closestFrame,
            FrameConfidence = confidence,
            Description = $"Point ({x}, {y}) is in focus at depth Z={depth:F2} (Best Frame: #{closestFrame + 1}) with confidence {confidence * 100f:F0}%."
        };
    }

    public unsafe ImageBuffer<float> RenderSyntheticAperture(
        DepthMapResult depthResult,
        IReadOnlyList<StackFrame> frames,
        SyntheticApertureParams parameters)
    {
        if (depthResult == null) throw new ArgumentNullException(nameof(depthResult));
        if (frames == null || frames.Count == 0) throw new ArgumentException("Frames list cannot be empty.", nameof(frames));
        parameters ??= new SyntheticApertureParams();

        int w = depthResult.Width;
        int h = depthResult.Height;
        int channels = frames[0].ColorBuffer?.Channels ?? 1;
        var output = new ImageBuffer<float>(w, h, channels);

        float targetZ = parameters.TargetFocalDepth;
        float aperture = Math.Max(0.1f, parameters.ApertureSize);
        float blurMax = Math.Clamp(parameters.BokehBlurRadius, 1f, 16f);
        int frameCount = frames.Count;

        float* depthPtr = depthResult.DepthMap.DataPointer;
        float* outPtr = output.DataPointer;

        Parallel.For(0, h, y =>
        {
            int rowOffset = y * w;
            for (int x = 0; x < w; x++)
            {
                int idx = rowOffset + x;
                float z = depthPtr[idx];
                float deltaZ = MathF.Abs(z - targetZ);

                if (parameters.EnableSelectiveRange)
                {
                    if (z >= parameters.RangeMinDepth && z <= parameters.RangeMaxDepth)
                    {
                        deltaZ = 0f;
                    }
                    else if (z < parameters.RangeMinDepth)
                    {
                        deltaZ = parameters.RangeMinDepth - z;
                    }
                    else
                    {
                        deltaZ = z - parameters.RangeMaxDepth;
                    }
                }

                // If within in-focus slice: sharp pixel from best frame
                int bestK = Math.Clamp((int)MathF.Round(z), 0, frameCount - 1);
                var bestFrame = frames[bestK];

                if (deltaZ <= aperture)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        float val = (bestFrame.ColorBuffer != null)
                            ? bestFrame.ColorBuffer.At(x, y, c)
                            : (bestFrame.GrayBuffer != null ? bestFrame.GrayBuffer.At(x, y) : 0.5f);
                        outPtr[idx * channels + c] = val;
                    }
                }
                else
                {
                    // Defocused bokeh pixel: blend box sample with defocus radius
                    float defocusRatio = Math.Clamp((deltaZ - aperture) / 4.0f, 0f, 1f);
                    int r = Math.Clamp((int)MathF.Round(defocusRatio * blurMax), 1, (int)blurMax);

                    for (int c = 0; c < channels; c++)
                    {
                        float sum = 0f;
                        int count = 0;

                        for (int dy = -r; dy <= r; dy++)
                        {
                            int ny = Math.Clamp(y + dy, 0, h - 1);
                            for (int dx = -r; dx <= r; dx++)
                            {
                                int nx = Math.Clamp(x + dx, 0, w - 1);
                                float val = (bestFrame.ColorBuffer != null)
                                    ? bestFrame.ColorBuffer.At(nx, ny, c)
                                    : (bestFrame.GrayBuffer != null ? bestFrame.GrayBuffer.At(nx, ny) : 0.5f);
                                sum += val;
                                count++;
                            }
                        }

                        outPtr[idx * channels + c] = count > 0 ? sum / count : 0.5f;
                    }
                }
            }
        });

        return output;
    }

    public ImageBuffer<float> RenderSelectiveDofRange(
        DepthMapResult depthResult,
        IReadOnlyList<StackFrame> frames,
        float minDepth,
        float maxDepth)
    {
        var parameters = new SyntheticApertureParams
        {
            EnableSelectiveRange = true,
            RangeMinDepth = minDepth,
            RangeMaxDepth = maxDepth,
            ApertureSize = 0.5f,
            BokehBlurRadius = 8.0f
        };
        return RenderSyntheticAperture(depthResult, frames, parameters);
    }
}
