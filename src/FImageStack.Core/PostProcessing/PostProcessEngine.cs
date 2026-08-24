using FImageStack.Core.Models;

namespace FImageStack.Core.PostProcessing;

public sealed class PostProcessSettings
{
    public float Exposure { get; set; } = 0.0f;       // -2.0 to +2.0 EV
    public float Contrast { get; set; } = 1.0f;       // 0.5 to 2.0
    public float Clarity { get; set; } = 0.0f;        // 0.0 to 1.0 (local contrast)
    public float SharpenAmount { get; set; } = 0.3f;  // 0.0 to 1.5
    public float Saturation { get; set; } = 1.0f;     // 0.0 to 2.0
    public bool EnableAutoLevels { get; set; } = false;
}

public interface IPostProcessEngine
{
    ImageBuffer<float> ApplyPostProcessing(ImageBuffer<float> input, PostProcessSettings settings);
}

public sealed class StandardPostProcessEngine : IPostProcessEngine
{
    public unsafe ImageBuffer<float> ApplyPostProcessing(ImageBuffer<float> input, PostProcessSettings settings)
    {
        int width = input.Width;
        int height = input.Height;
        int channels = input.Channels;

        var output = input.Clone();
        float* outPtr = output.DataPointer;
        float* inPtr = input.DataPointer;

        float expMultiplier = MathF.Pow(2.0f, settings.Exposure);
        float contrast = settings.Contrast;
        float saturation = settings.Saturation;
        float sharpen = settings.SharpenAmount;

        // 1. Exposure, Contrast & Saturation in-place
        Parallel.For(0, height, y =>
        {
            int rowOffset = y * width * channels;
            for (int x = 0; x < width; x++)
            {
                int idx = rowOffset + x * channels;

                if (channels >= 3)
                {
                    float r = outPtr[idx] * expMultiplier;
                    float g = outPtr[idx + 1] * expMultiplier;
                    float b = outPtr[idx + 2] * expMultiplier;

                    // Contrast around 0.5 midpoint
                    r = (r - 0.5f) * contrast + 0.5f;
                    g = (g - 0.5f) * contrast + 0.5f;
                    b = (b - 0.5f) * contrast + 0.5f;

                    // Saturation via Rec.709 luminance
                    float luma = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                    r = luma + (r - luma) * saturation;
                    g = luma + (g - luma) * saturation;
                    b = luma + (b - luma) * saturation;

                    outPtr[idx] = Math.Clamp(r, 0f, 1f);
                    outPtr[idx + 1] = Math.Clamp(g, 0f, 1f);
                    outPtr[idx + 2] = Math.Clamp(b, 0f, 1f);
                }
                else
                {
                    float val = outPtr[idx] * expMultiplier;
                    val = (val - 0.5f) * contrast + 0.5f;
                    outPtr[idx] = Math.Clamp(val, 0f, 1f);
                }
            }
        });

        // 2. Unsharp Masking (Sharpening) if enabled
        if (sharpen > 0.01f)
        {
            using var blurBuffer = output.Clone();
            float* blurPtr = blurBuffer.DataPointer;

            // 3x3 Gaussian approximation
            Parallel.For(1, height - 1, y =>
            {
                int prevRow = (y - 1) * width * channels;
                int currRow = y * width * channels;
                int nextRow = (y + 1) * width * channels;

                for (int x = 1; x < width - 1; x++)
                {
                    int xC = x * channels;
                    int xL = (x - 1) * channels;
                    int xR = (x + 1) * channels;

                    for (int c = 0; c < channels; c++)
                    {
                        float sum =
                            outPtr[prevRow + xL + c] * 1f + outPtr[prevRow + xC + c] * 2f + outPtr[prevRow + xR + c] * 1f +
                            outPtr[currRow + xL + c] * 2f + outPtr[currRow + xC + c] * 4f + outPtr[currRow + xR + c] * 2f +
                            outPtr[nextRow + xL + c] * 1f + outPtr[nextRow + xC + c] * 2f + outPtr[nextRow + xR + c] * 1f;

                        blurPtr[currRow + xC + c] = sum * (1f / 16f);
                    }
                }
            });

            // High-pass blend: Out = Out + (Out - Blur) * SharpenAmount
            Parallel.For(0, height, y =>
            {
                int rowOffset = y * width * channels;
                for (int x = 0; x < width; x++)
                {
                    int idx = rowOffset + x * channels;
                    for (int c = 0; c < channels; c++)
                    {
                        float orig = outPtr[idx + c];
                        float blurred = blurPtr[idx + c];
                        float detail = orig - blurred;
                        outPtr[idx + c] = Math.Clamp(orig + detail * sharpen, 0f, 1f);
                    }
                }
            });
        }

        return output;
    }
}
