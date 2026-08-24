using FImageStack.Core.Models;

namespace FImageStack.Core.Fusion;

public interface IFusionEngine
{
    FusionMethod Method { get; }
    ImageBuffer<float> Fuse(IReadOnlyList<StackFrame> frames, DepthMapResult depthResult, FusionSettings settings);
}

public sealed class WinnerTakesAllFusionEngine : IFusionEngine
{
    public FusionMethod Method => FusionMethod.WinnerTakesAll;

    public unsafe ImageBuffer<float> Fuse(IReadOnlyList<StackFrame> frames, DepthMapResult depthResult, FusionSettings settings)
    {
        int width = depthResult.Width;
        int height = depthResult.Height;
        var output = new ImageBuffer<float>(width, height, 3, PixelFormatType.RgbFloat32);

        int* srcMap = depthResult.SourceFrameMap.DataPointer;
        float* dst = output.DataPointer;

        int frameCount = frames.Count;
        float*[] colorPointers = new float*[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            if (frames[i].ColorBuffer == null)
                throw new InvalidOperationException($"Frame {i} has no ColorBuffer.");
            colorPointers[i] = frames[i].ColorBuffer!.DataPointer;
        }

        Parallel.For(0, height, y =>
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                int pixelIdx = rowOffset + x;
                int frameIdx = Math.Clamp(srcMap[pixelIdx], 0, frameCount - 1);
                float* srcColor = colorPointers[frameIdx] + pixelIdx * 3;
                float* dstColor = dst + pixelIdx * 3;

                dstColor[0] = srcColor[0];
                dstColor[1] = srcColor[1];
                dstColor[2] = srcColor[2];
            }
        });

        return output;
    }
}

public sealed class FocusWeightedFusionEngine : IFusionEngine
{
    public FusionMethod Method => FusionMethod.FocusWeighted;

    public unsafe ImageBuffer<float> Fuse(IReadOnlyList<StackFrame> frames, DepthMapResult depthResult, FusionSettings settings)
    {
        int width = depthResult.Width;
        int height = depthResult.Height;
        int frameCount = frames.Count;

        var output = new ImageBuffer<float>(width, height, 3, PixelFormatType.RgbFloat32);
        float* dst = output.DataPointer;

        float*[] colorPointers = new float*[frameCount];
        float*[] focusPointers = new float*[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            colorPointers[i] = frames[i].ColorBuffer!.DataPointer;
            focusPointers[i] = frames[i].FocusMap!.DataPointer;
        }

        Parallel.For(0, height, y =>
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                int pixelIdx = rowOffset + x;
                float totalWeight = 0f;
                float r = 0f, g = 0f, b = 0f;

                // Use power/exponent of focus measure to heighten peak weight
                for (int f = 0; f < frameCount; f++)
                {
                    float sharpness = focusPointers[f][pixelIdx];
                    float weight = MathF.Pow(sharpness + 1e-5f, 4.0f);
                    totalWeight += weight;

                    float* srcColor = colorPointers[f] + pixelIdx * 3;
                    r += srcColor[0] * weight;
                    g += srcColor[1] * weight;
                    b += srcColor[2] * weight;
                }

                float invWeight = totalWeight > 0 ? 1f / totalWeight : 1f / frameCount;
                float* dstColor = dst + pixelIdx * 3;
                dstColor[0] = Math.Clamp(r * invWeight, 0f, 1f);
                dstColor[1] = Math.Clamp(g * invWeight, 0f, 1f);
                dstColor[2] = Math.Clamp(b * invWeight, 0f, 1f);
            }
        });

        return output;
    }
}

public sealed class MultiScalePyramidFusionEngine : IFusionEngine
{
    public FusionMethod Method => FusionMethod.MultiScalePyramid;

    public unsafe ImageBuffer<float> Fuse(IReadOnlyList<StackFrame> frames, DepthMapResult depthResult, FusionSettings settings)
    {
        int width = depthResult.Width;
        int height = depthResult.Height;
        int frameCount = frames.Count;
        int levels = Math.Clamp(settings.PyramidLevels, 2, 7);

        // Precompute weights for each frame: normalized focus map with exponent
        var frameWeights = new ImageBuffer<float>[frameCount];
        for (int f = 0; f < frameCount; f++)
        {
            frameWeights[f] = new ImageBuffer<float>(width, height, 1);
        }

        Parallel.For(0, height, y =>
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                int idx = rowOffset + x;
                float sum = 0f;
                for (int f = 0; f < frameCount; f++)
                {
                    float w = MathF.Pow(frames[f].FocusMap!.DataPointer[idx] + 1e-4f, 3.0f);
                    frameWeights[f].DataPointer[idx] = w;
                    sum += w;
                }
                float invSum = sum > 0 ? 1f / sum : 1f / frameCount;
                for (int f = 0; f < frameCount; f++)
                {
                    frameWeights[f].DataPointer[idx] *= invSum;
                }
            }
        });

        // Build Laplacian Pyramid for each frame and weight Gaussian Pyramid
        // We do pyramid decomposition and accumulation level by level
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

        // Process each frame into pyramid and blend into fusedLaplacians
        for (int f = 0; f < frameCount; f++)
        {
            var colorPyr = BuildLaplacianPyramid(frames[f].ColorBuffer!, levels);
            var weightPyr = BuildGaussianPyramid(frameWeights[f], levels);

            for (int l = 0; l < levels; l++)
            {
                BlendPyramidLevel(fusedLaplacians[l], colorPyr[l], weightPyr[l]);
                colorPyr[l].Dispose();
                weightPyr[l].Dispose();
            }
        }

        // Clean up weight buffers
        for (int f = 0; f < frameCount; f++)
        {
            frameWeights[f].Dispose();
        }

        // Reconstruct from fused Laplacian pyramid
        var reconstructed = ReconstructFromLaplacianPyramid(fusedLaplacians);

        // Clean up fused laplacian levels
        for (int l = 0; l < levels; l++)
        {
            fusedLaplacians[l].Dispose();
        }

        return reconstructed;
    }

    private static unsafe void BlendPyramidLevel(ImageBuffer<float> fusedLevel, ImageBuffer<float> frameLaplacian, ImageBuffer<float> frameWeight)
    {
        int w = fusedLevel.Width;
        int h = fusedLevel.Height;
        float* dst = fusedLevel.DataPointer;
        float* src = frameLaplacian.DataPointer;
        float* weight = frameWeight.DataPointer;

        Parallel.For(0, h, y =>
        {
            int rowOffset = y * w;
            for (int x = 0; x < w; x++)
            {
                int pixelIdx = rowOffset + x;
                float wVal = weight[pixelIdx];
                int cIdx = pixelIdx * 3;

                dst[cIdx] += src[cIdx] * wVal;
                dst[cIdx + 1] += src[cIdx + 1] * wVal;
                dst[cIdx + 2] += src[cIdx + 2] * wVal;
            }
        });
    }

    private static unsafe ImageBuffer<float>[] BuildGaussianPyramid(ImageBuffer<float> image, int levels)
    {
        var pyramid = new ImageBuffer<float>[levels];
        pyramid[0] = image.Clone();

        for (int l = 1; l < levels; l++)
        {
            pyramid[l] = Downsample(pyramid[l - 1]);
        }
        return pyramid;
    }

    private static unsafe ImageBuffer<float>[] BuildLaplacianPyramid(ImageBuffer<float> image, int levels)
    {
        var gPyr = BuildGaussianPyramid(image, levels);
        var lPyr = new ImageBuffer<float>[levels];

        for (int l = 0; l < levels - 1; l++)
        {
            using var upsampled = Upsample(gPyr[l + 1], gPyr[l].Width, gPyr[l].Height);
            lPyr[l] = Subtract(gPyr[l], upsampled);
            gPyr[l].Dispose();
        }

        // Base lowest frequency is the smallest Gaussian level
        lPyr[levels - 1] = gPyr[levels - 1];
        return lPyr;
    }

    private static unsafe ImageBuffer<float> ReconstructFromLaplacianPyramid(ImageBuffer<float>[] laplacians)
    {
        int levels = laplacians.Length;
        var current = laplacians[levels - 1].Clone();

        for (int l = levels - 2; l >= 0; l--)
        {
            using var upsampled = Upsample(current, laplacians[l].Width, laplacians[l].Height);
            current.Dispose();
            current = Add(upsampled, laplacians[l]);
        }

        // Clamp values to [0, 1]
        float* ptr = current.DataPointer;
        int total = current.TotalElements;
        for (int i = 0; i < total; i++)
        {
            ptr[i] = Math.Clamp(ptr[i], 0f, 1f);
        }

        return current;
    }

    private static unsafe ImageBuffer<float> Downsample(ImageBuffer<float> src)
    {
        int dstW = (src.Width + 1) / 2;
        int dstH = (src.Height + 1) / 2;
        int channels = src.Channels;
        var dst = new ImageBuffer<float>(dstW, dstH, channels, src.Format);

        float* s = src.DataPointer;
        float* d = dst.DataPointer;
        int srcW = src.Width;
        int srcH = src.Height;

        Parallel.For(0, dstH, dy =>
        {
            int sy0 = Math.Min(dy * 2, srcH - 1);
            int sy1 = Math.Min(dy * 2 + 1, srcH - 1);

            for (int dx = 0; dx < dstW; dx++)
            {
                int sx0 = Math.Min(dx * 2, srcW - 1);
                int sx1 = Math.Min(dx * 2 + 1, srcW - 1);

                for (int c = 0; c < channels; c++)
                {
                    float p00 = s[(sy0 * srcW + sx0) * channels + c];
                    float p01 = s[(sy0 * srcW + sx1) * channels + c];
                    float p10 = s[(sy1 * srcW + sx0) * channels + c];
                    float p11 = s[(sy1 * srcW + sx1) * channels + c];

                    d[(dy * dstW + dx) * channels + c] = (p00 + p01 + p10 + p11) * 0.25f;
                }
            }
        });

        return dst;
    }

    private static unsafe ImageBuffer<float> Upsample(ImageBuffer<float> src, int targetW, int targetH)
    {
        int channels = src.Channels;
        var dst = new ImageBuffer<float>(targetW, targetH, channels, src.Format);

        float* s = src.DataPointer;
        float* d = dst.DataPointer;
        int srcW = src.Width;
        int srcH = src.Height;

        // Bilinear interpolation
        Parallel.For(0, targetH, dy =>
        {
            float sy = (float)dy / targetH * srcH;
            int y0 = Math.Clamp((int)sy, 0, srcH - 1);
            int y1 = Math.Clamp(y0 + 1, 0, srcH - 1);
            float fy = sy - y0;

            for (int dx = 0; dx < targetW; dx++)
            {
                float sx = (float)dx / targetW * srcW;
                int x0 = Math.Clamp((int)sx, 0, srcW - 1);
                int x1 = Math.Clamp(x0 + 1, 0, srcW - 1);
                float fx = sx - x0;

                for (int c = 0; c < channels; c++)
                {
                    float p00 = s[(y0 * srcW + x0) * channels + c];
                    float p01 = s[(y0 * srcW + x1) * channels + c];
                    float p10 = s[(y1 * srcW + x0) * channels + c];
                    float p11 = s[(y1 * srcW + x1) * channels + c];

                    float top = p00 * (1f - fx) + p01 * fx;
                    float bot = p10 * (1f - fx) + p11 * fx;
                    d[(dy * targetW + dx) * channels + c] = top * (1f - fy) + bot * fy;
                }
            }
        });

        return dst;
    }

    private static unsafe ImageBuffer<float> Subtract(ImageBuffer<float> a, ImageBuffer<float> b)
    {
        int w = a.Width;
        int h = a.Height;
        int channels = a.Channels;
        var res = new ImageBuffer<float>(w, h, channels, a.Format);

        float* pa = a.DataPointer;
        float* pb = b.DataPointer;
        float* pr = res.DataPointer;
        int total = a.TotalElements;

        Parallel.For(0, total, i =>
        {
            pr[i] = pa[i] - pb[i];
        });

        return res;
    }

    private static unsafe ImageBuffer<float> Add(ImageBuffer<float> a, ImageBuffer<float> b)
    {
        int w = a.Width;
        int h = a.Height;
        int channels = a.Channels;
        var res = new ImageBuffer<float>(w, h, channels, a.Format);

        float* pa = a.DataPointer;
        float* pb = b.DataPointer;
        float* pr = res.DataPointer;
        int total = a.TotalElements;

        Parallel.For(0, total, i =>
        {
            pr[i] = pa[i] + pb[i];
        });

        return res;
    }
}
