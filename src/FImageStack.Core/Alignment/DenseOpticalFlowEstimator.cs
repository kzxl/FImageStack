using System.Runtime.CompilerServices;
using FImageStack.Core.Models;

namespace FImageStack.Core.Alignment;

public interface IDenseOpticalFlowEstimator
{
    OpticalFlowField ComputeDenseFlow(
        StackFrame refFrame,
        StackFrame targetFrame,
        int pyramidLevels = 3,
        int windowRadius = 4,
        int iterations = 3);
}

public sealed class DenseOpticalFlowEstimator : IDenseOpticalFlowEstimator
{
    public unsafe OpticalFlowField ComputeDenseFlow(
        StackFrame refFrame,
        StackFrame targetFrame,
        int pyramidLevels = 3,
        int windowRadius = 4,
        int iterations = 3)
    {
        if (refFrame.GrayBuffer == null || targetFrame.GrayBuffer == null)
            throw new ArgumentException("Frames must contain valid GrayBuffer for optical flow.");

        int w = refFrame.Width;
        int h = refFrame.Height;
        pyramidLevels = Math.Clamp(pyramidLevels, 1, 4);

        // 1. Build Gaussian Pyramids
        var refPyramid = BuildPyramid(refFrame.GrayBuffer, pyramidLevels);
        var tgtPyramid = BuildPyramid(targetFrame.GrayBuffer, pyramidLevels);

        // 2. Initialize coarsest flow field
        int coarsestLevel = pyramidLevels - 1;
        int cw = refPyramid[coarsestLevel].Width;
        int ch = refPyramid[coarsestLevel].Height;

        var vx = new ImageBuffer<float>(cw, ch);
        var vy = new ImageBuffer<float>(cw, ch);
        var conf = new ImageBuffer<float>(cw, ch);

        // 3. Coarse-to-Fine Pyramidal Lucas-Kanade
        for (int level = coarsestLevel; level >= 0; level--)
        {
            var curRef = refPyramid[level];
            var curTgt = tgtPyramid[level];
            int curW = curRef.Width;
            int curH = curRef.Height;

            if (level < coarsestLevel)
            {
                // Upscale flow from previous level by 2x and double displacement vectors
                var nextVx = UpscaleFlow(vx, curW, curH, 2.0f);
                var nextVy = UpscaleFlow(vy, curW, curH, 2.0f);
                var nextConf = UpscaleFlow(conf, curW, curH, 1.0f);

                vx.Dispose();
                vy.Dispose();
                conf.Dispose();

                vx = nextVx;
                vy = nextVy;
                conf = nextConf;
            }

            // Refine flow at current level
            RefineFlowLevel(curRef, curTgt, vx, vy, conf, windowRadius, iterations);
        }

        // Clean up pyramid buffers
        for (int i = 0; i < pyramidLevels; i++)
        {
            refPyramid[i].Dispose();
            tgtPyramid[i].Dispose();
        }

        var resultFlow = new OpticalFlowField(w, h);
        vx.DataPointerCopy(resultFlow.Vx.DataPointer, w * h);
        vy.DataPointerCopy(resultFlow.Vy.DataPointer, w * h);
        conf.DataPointerCopy(resultFlow.Confidence.DataPointer, w * h);

        vx.Dispose();
        vy.Dispose();
        conf.Dispose();

        return resultFlow;
    }

    private static unsafe void RefineFlowLevel(
        ImageBuffer<float> refImg,
        ImageBuffer<float> tgtImg,
        ImageBuffer<float> vx,
        ImageBuffer<float> vy,
        ImageBuffer<float> conf,
        int windowRadius,
        int iterations)
    {
        int w = refImg.Width;
        int h = refImg.Height;

        float* refPtr = refImg.DataPointer;
        float* tgtPtr = tgtImg.DataPointer;
        float* vxPtr = vx.DataPointer;
        float* vyPtr = vy.DataPointer;
        float* confPtr = conf.DataPointer;

        for (int iter = 0; iter < iterations; iter++)
        {
            Parallel.For(0, h, y =>
            {
                int yMin = Math.Max(0, y - windowRadius);
                int yMax = Math.Min(h - 1, y + windowRadius);
                int rowOffset = y * w;

                for (int x = 0; x < w; x++)
                {
                    int xMin = Math.Max(0, x - windowRadius);
                    int xMax = Math.Min(w - 1, x + windowRadius);
                    int idx = rowOffset + x;

                    float u = vxPtr[idx];
                    float v = vyPtr[idx];

                    // Accumulate 2x2 Structure Tensor over local window
                    float sxx = 0f, syy = 0f, sxy = 0f;
                    float sxt = 0f, syt = 0f;

                    for (int wy = yMin; wy <= yMax; wy++)
                    {
                        int wyOffset = wy * w;
                        for (int wx = xMin; wx <= xMax; wx++)
                        {
                            int wIdx = wyOffset + wx;

                            // Spatial gradients on reference image
                            int wxLeft = Math.Max(0, wx - 1);
                            int wxRight = Math.Min(w - 1, wx + 1);
                            int wyUp = Math.Max(0, wy - 1);
                            int wyDown = Math.Min(h - 1, wy + 1);

                            float ix = (refPtr[wyOffset + wxRight] - refPtr[wyOffset + wxLeft]) * 0.5f;
                            float iy = (refPtr[wyDown * w + wx] - refPtr[wyUp * w + wx]) * 0.5f;

                            // Warped coordinate in target image
                            float curU = vxPtr[wIdx];
                            float curV = vyPtr[wIdx];
                            float tx = wx + curU;
                            float ty = wy + curV;

                            float tgtVal = SampleBilinear(tgtPtr, w, h, tx, ty);
                            float refVal = refPtr[wIdx];
                            float it = tgtVal - refVal;

                            sxx += ix * ix;
                            syy += iy * iy;
                            sxy += ix * iy;
                            sxt += ix * it;
                            syt += iy * it;
                        }
                    }

                    // Solve 2x2 linear system with Tikhonov regularization
                    float det = sxx * syy - sxy * sxy + 0.001f;
                    float du = (-syy * sxt + sxy * syt) / det;
                    float dv = (sxy * sxt - sxx * syt) / det;

                    // Clamped update
                    vxPtr[idx] += Math.Clamp(du, -1.5f, 1.5f);
                    vyPtr[idx] += Math.Clamp(dv, -1.5f, 1.5f);
                    confPtr[idx] = Math.Clamp((sxx + syy) * 10f, 0.1f, 1.0f);
                }
            });
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe float SampleBilinear(float* img, int w, int h, float x, float y)
    {
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        int x1 = x0 + 1;
        int y1 = y0 + 1;

        if (x0 < 0 || x1 >= w || y0 < 0 || y1 >= h)
        {
            int cx = Math.Clamp((int)MathF.Round(x), 0, w - 1);
            int cy = Math.Clamp((int)MathF.Round(y), 0, h - 1);
            return img[cy * w + cx];
        }

        float wx1 = x - x0;
        float wx0 = 1.0f - wx1;
        float wy1 = y - y0;
        float wy0 = 1.0f - wy1;

        return wx0 * wy0 * img[y0 * w + x0] +
               wx1 * wy0 * img[y0 * w + x1] +
               wx0 * wy1 * img[y1 * w + x0] +
               wx1 * wy1 * img[y1 * w + x1];
    }

    private static unsafe ImageBuffer<float> UpscaleFlow(ImageBuffer<float> srcFlow, int targetW, int targetH, float vectorMultiplier)
    {
        int srcW = srcFlow.Width;
        int srcH = srcFlow.Height;
        var dstFlow = new ImageBuffer<float>(targetW, targetH);

        float* src = srcFlow.DataPointer;
        float* dst = dstFlow.DataPointer;

        float scaleX = (float)srcW / targetW;
        float scaleY = (float)srcH / targetH;

        Parallel.For(0, targetH, y =>
        {
            float srcY = y * scaleY;
            int rowOffset = y * targetW;

            for (int x = 0; x < targetW; x++)
            {
                float srcX = x * scaleX;
                float val = SampleBilinear(src, srcW, srcH, srcX, srcY) * vectorMultiplier;
                dst[rowOffset + x] = val;
            }
        });

        return dstFlow;
    }

    private static unsafe List<ImageBuffer<float>> BuildPyramid(ImageBuffer<float> source, int levels)
    {
        var pyramid = new List<ImageBuffer<float>>(levels);
        pyramid.Add(source.Clone());

        for (int l = 1; l < levels; l++)
        {
            var prev = pyramid[l - 1];
            int prevW = prev.Width;
            int prevH = prev.Height;
            int nextW = Math.Max(2, prevW / 2);
            int nextH = Math.Max(2, prevH / 2);

            var next = new ImageBuffer<float>(nextW, nextH);
            float* pSrc = prev.DataPointer;
            float* pDst = next.DataPointer;

            Parallel.For(0, nextH, y =>
            {
                int sy0 = Math.Min(prevH - 1, y * 2);
                int sy1 = Math.Min(prevH - 1, y * 2 + 1);

                for (int x = 0; x < nextW; x++)
                {
                    int sx0 = Math.Min(prevW - 1, x * 2);
                    int sx1 = Math.Min(prevW - 1, x * 2 + 1);

                    float sum = pSrc[sy0 * prevW + sx0] + pSrc[sy0 * prevW + sx1] +
                                pSrc[sy1 * prevW + sx0] + pSrc[sy1 * prevW + sx1];
                    pDst[y * nextW + x] = sum * 0.25f;
                }
            });

            pyramid.Add(next);
        }

        return pyramid;
    }
}

internal static class BufferExtensions
{
    public static unsafe void DataPointerCopy(this ImageBuffer<float> src, float* dst, int length)
    {
        float* s = src.DataPointer;
        for (int i = 0; i < length; i++) dst[i] = s[i];
    }
}
