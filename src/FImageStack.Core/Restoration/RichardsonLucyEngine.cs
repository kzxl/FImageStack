using FImageStack.Core.Models;

namespace FImageStack.Core.Restoration;

public interface IRichardsonLucyEngine
{
    ImageBuffer<float> Deconvolve(ImageBuffer<float> blurredImage, ImageBuffer<float> psf, DeconvolutionOptions options);
}

public sealed class RichardsonLucyEngine : IRichardsonLucyEngine
{
    public unsafe ImageBuffer<float> Deconvolve(
        ImageBuffer<float> blurredImage, 
        ImageBuffer<float> psf, 
        DeconvolutionOptions options)
    {
        if (blurredImage == null) throw new ArgumentNullException(nameof(blurredImage));
        if (psf == null) throw new ArgumentNullException(nameof(psf));

        int w = blurredImage.Width;
        int h = blurredImage.Height;
        int ch = blurredImage.Channels;
        int kSize = psf.Width;
        int kRadius = kSize / 2;

        // Clone blurred image as initial estimate u^(0)
        var estimate = blurredImage.Clone();
        var convBuffer = new ImageBuffer<float>(w, h, ch, blurredImage.Format);
        var ratioBuffer = new ImageBuffer<float>(w, h, ch, blurredImage.Format);
        var updateBuffer = new ImageBuffer<float>(w, h, ch, blurredImage.Format);

        // Flipped PSF for adjoint convolution h*
        using var psfFlipped = FlipPsf(psf);

        float* dPtr = blurredImage.DataPointer;
        float* uPtr = estimate.DataPointer;
        float* cPtr = convBuffer.DataPointer;
        float* rPtr = ratioBuffer.DataPointer;
        float* bPtr = updateBuffer.DataPointer;
        float* hPtr = psf.DataPointer;
        float* hfPtr = psfFlipped.DataPointer;

        int iterations = Math.Max(1, options.Iterations);
        float tvWeight = options.TvDampingWeight;

        for (int iter = 0; iter < iterations; iter++)
        {
            // 1. Forward Convolution: c = u * h
            Convolve2D(uPtr, cPtr, w, h, ch, hPtr, kSize, kRadius);

            // 2. Ratio: r = d / (c + eps)
            Parallel.For(0, h, y =>
            {
                int rowOffset = y * w * ch;
                for (int x = 0; x < w; x++)
                {
                    int baseIdx = rowOffset + x * ch;
                    for (int c = 0; c < ch; c++)
                    {
                        float denom = cPtr[baseIdx + c] + 1e-6f;
                        rPtr[baseIdx + c] = dPtr[baseIdx + c] / denom;
                    }
                }
            });

            // 3. Adjoint Convolution: b = r * h*
            Convolve2D(rPtr, bPtr, w, h, ch, hfPtr, kSize, kRadius);

            // 4. Multiplicative Update with TV Damping: u = u * b / (1 - lambda * Laplacian(u))
            Parallel.For(0, h, y =>
            {
                int yPrev = Math.Max(0, y - 1);
                int yNext = Math.Min(h - 1, y + 1);
                int rowOffset = y * w * ch;

                for (int x = 0; x < w; x++)
                {
                    int xPrev = Math.Max(0, x - 1);
                    int xNext = Math.Min(w - 1, x + 1);
                    int baseIdx = rowOffset + x * ch;

                    for (int c = 0; c < ch; c++)
                    {
                        float currentU = uPtr[baseIdx + c];
                        float update = bPtr[baseIdx + c];

                        // TV Laplacian Damping
                        float lap = 0f;
                        if (tvWeight > 0f)
                        {
                            float uUp = uPtr[(yPrev * w + x) * ch + c];
                            float uDown = uPtr[(yNext * w + x) * ch + c];
                            float uLeft = uPtr[(y * w + xPrev) * ch + c];
                            float uRight = uPtr[(y * w + xNext) * ch + c];
                            lap = (uUp + uDown + uLeft + uRight - 4f * currentU);
                        }

                        float factor = update / (1.0f - tvWeight * lap);
                        uPtr[baseIdx + c] = Math.Clamp(currentU * factor, 0f, 1.5f);
                    }
                }
            });
        }

        convBuffer.Dispose();
        ratioBuffer.Dispose();
        updateBuffer.Dispose();

        return estimate;
    }

    private static unsafe void Convolve2D(
        float* src, 
        float* dst, 
        int w, 
        int h, 
        int ch, 
        float* kernel, 
        int kSize, 
        int kRadius)
    {
        Parallel.For(0, h, y =>
        {
            int dstRowOffset = y * w * ch;

            for (int x = 0; x < w; x++)
            {
                int dstBase = dstRowOffset + x * ch;

                for (int c = 0; c < ch; c++)
                {
                    float sum = 0f;

                    for (int ky = -kRadius; ky <= kRadius; ky++)
                    {
                        int sy = Math.Clamp(y + ky, 0, h - 1);
                        int srcRowOffset = sy * w * ch;
                        int kRowOffset = (ky + kRadius) * kSize;

                        for (int kx = -kRadius; kx <= kRadius; kx++)
                        {
                            int sx = Math.Clamp(x + kx, 0, w - 1);
                            float kVal = kernel[kRowOffset + (kx + kRadius)];
                            sum += src[srcRowOffset + sx * ch + c] * kVal;
                        }
                    }

                    dst[dstBase + c] = sum;
                }
            }
        });
    }

    private static unsafe ImageBuffer<float> FlipPsf(ImageBuffer<float> psf)
    {
        int size = psf.Width;
        var flipped = new ImageBuffer<float>(size, size, 1, PixelFormatType.GrayFloat32);
        float* s = psf.DataPointer;
        float* d = flipped.DataPointer;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                d[y * size + x] = s[(size - 1 - y) * size + (size - 1 - x)];
            }
        }

        return flipped;
    }
}
