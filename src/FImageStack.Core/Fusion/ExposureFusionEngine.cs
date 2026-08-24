using FImageStack.Core.Models;

namespace FImageStack.Core.Fusion;

public sealed class ExposureFusionEngine : IFusionEngine
{
    public FusionMethod Method => FusionMethod.HDRFocusExposure;

    public unsafe ImageBuffer<float> Fuse(IReadOnlyList<StackFrame> frames, DepthMapResult depthResult, FusionSettings settings)
    {
        int width = depthResult.Width;
        int height = depthResult.Height;
        int frameCount = frames.Count;
        int levels = Math.Clamp(settings.PyramidLevels, 2, 7);

        // Step 1: Compute composite Mertens-Focus Exposure Weights for each frame
        var frameWeights = new ImageBuffer<float>[frameCount];
        for (int f = 0; f < frameCount; f++)
        {
            frameWeights[f] = new ImageBuffer<float>(width, height, 1);
        }

        float*[] colorPtrs = new float*[frameCount];
        float*[] focusPtrs = new float*[frameCount];
        float*[] weightPtrs = new float*[frameCount];

        for (int f = 0; f < frameCount; f++)
        {
            colorPtrs[f] = frames[f].ColorBuffer!.DataPointer;
            focusPtrs[f] = frames[f].FocusMap != null ? frames[f].FocusMap!.DataPointer : null;
            weightPtrs[f] = frameWeights[f].DataPointer;
        }

        Parallel.For(0, height, y =>
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                int pixelIdx = rowOffset + x;
                int colorIdx = pixelIdx * 3;
                float sumWeight = 0f;

                for (int f = 0; f < frameCount; f++)
                {
                    float r = colorPtrs[f][colorIdx];
                    float g = colorPtrs[f][colorIdx + 1];
                    float b = colorPtrs[f][colorIdx + 2];

                    // 1. Focus Measure / Contrast Weight (Modified Laplacian or Tenengrad)
                    float focusVal = (focusPtrs[f] != null) ? focusPtrs[f][pixelIdx] : 0.5f;
                    float wContrast = MathF.Pow(focusVal + 1e-4f, 2.0f);

                    // 2. Well-Exposedness Gaussian Weight (Mertens Curve around 0.5 midpoint, sigma = 0.2)
                    float dr = r - 0.5f;
                    float dg = g - 0.5f;
                    float db = b - 0.5f;
                    float expDist = (dr * dr + dg * dg + db * db) / (2f * 0.2f * 0.2f);
                    float wExposure = MathF.Exp(-expDist);

                    // 3. Color Saturation Weight
                    float mean = (r + g + b) / 3f;
                    float satVar = ((r - mean) * (r - mean) + (g - mean) * (g - mean) + (b - mean) * (b - mean)) / 3f;
                    float wSaturation = MathF.Sqrt(satVar);

                    // Unified Composite Weight
                    float totalW = (wContrast * 1.5f) * (wExposure + 1e-3f) * MathF.Sqrt(wSaturation + 1e-3f);
                    weightPtrs[f][pixelIdx] = totalW;
                    sumWeight += totalW;
                }

                // Normalize weights across all frames for this pixel
                float invSum = sumWeight > 0 ? 1f / sumWeight : 1f / frameCount;
                for (int f = 0; f < frameCount; f++)
                {
                    weightPtrs[f][pixelIdx] *= invSum;
                }
            }
        });

        // Step 2: Multi-Scale Laplacian Pyramid Fusion
        var fusedLaplacians = new ImageBuffer<float>[levels];
        int curW = width;
        int curH = height;
        for (int l = 0; l < levels; l++)
        {
            fusedLaplacians[l] = new ImageBuffer<float>(curW, curH, 3);
            if (l < levels - 1)
            {
                curW = (curW + 1) / 2;
                curH = (curH + 1) / 2;
            }
        }

        for (int f = 0; f < frameCount; f++)
        {
            var colorPyr = MultiScalePyramidFusionEngine.BuildLaplacianPyramid(frames[f].ColorBuffer!, levels);
            var weightPyr = MultiScalePyramidFusionEngine.BuildGaussianPyramid(frameWeights[f], levels);

            for (int l = 0; l < levels; l++)
            {
                MultiScalePyramidFusionEngine.BlendPyramidLevel(fusedLaplacians[l], colorPyr[l], weightPyr[l]);
                colorPyr[l].Dispose();
                weightPyr[l].Dispose();
            }
        }

        for (int f = 0; f < frameCount; f++)
        {
            frameWeights[f].Dispose();
        }

        // Collapse fused pyramid back into full resolution output
        var finalImage = MultiScalePyramidFusionEngine.ReconstructFromLaplacianPyramid(fusedLaplacians);

        for (int l = 0; l < levels; l++)
        {
            fusedLaplacians[l].Dispose();
        }

        return finalImage;
    }
}
