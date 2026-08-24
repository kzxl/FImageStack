using FImageStack.Core.Models;

namespace FImageStack.Core.PostProcessing;

public interface IToneMappingEngine
{
    ImageBuffer<float> ApplyToneMapping(
        ImageBuffer<float> linearBuffer,
        ToneMappingOperator op = ToneMappingOperator.ACESFilmic,
        float exposureEV = 0.0f,
        float whitePoint = 4.0f);
}

public sealed class ToneMappingEngine : IToneMappingEngine
{
    public unsafe ImageBuffer<float> ApplyToneMapping(
        ImageBuffer<float> linearBuffer,
        ToneMappingOperator op = ToneMappingOperator.ACESFilmic,
        float exposureEV = 0.0f,
        float whitePoint = 4.0f)
    {
        int w = linearBuffer.Width;
        int h = linearBuffer.Height;
        int ch = linearBuffer.Channels;
        var result = new ImageBuffer<float>(w, h, ch);

        float* src = linearBuffer.DataPointer;
        float* dst = result.DataPointer;
        float expScale = MathF.Pow(2.0f, exposureEV);
        float wpSq = whitePoint * whitePoint;

        Parallel.For(0, h, y =>
        {
            int rowOffset = y * w * ch;
            for (int x = 0; x < w; x++)
            {
                int baseIdx = rowOffset + x * ch;

                for (int c = 0; c < Math.Min(ch, 3); c++)
                {
                    float v = MathF.Max(0f, src[baseIdx + c] * expScale);
                    float mapped = v;

                    switch (op)
                    {
                        case ToneMappingOperator.ACESFilmic:
                            // ACES Filmic Curve (Narkowicz 2015 / Stephen Hill)
                            float a = 2.51f;
                            float b = 0.03f;
                            float cConst = 2.43f;
                            float d = 0.59f;
                            float e = 0.14f;
                            mapped = (v * (a * v + b)) / (v * (cConst * v + d) + e);
                            break;

                        case ToneMappingOperator.ReinhardExtended:
                            // Reinhard Extended Luminance Formulation
                            mapped = (v * (1.0f + (v / wpSq))) / (1.0f + v);
                            break;

                        case ToneMappingOperator.AgX:
                            // AgX Perceptual Log-Sigmoid Approximation
                            float logV = MathF.Log2(MathF.Max(1e-5f, v) + 0.01f);
                            mapped = 1.0f / (1.0f + MathF.Exp(-1.2f * (logV + 1.5f)));
                            break;

                        case ToneMappingOperator.LinearPreserve:
                        default:
                            mapped = v;
                            break;
                    }

                    dst[baseIdx + c] = Math.Clamp(mapped, 0.0f, 1.0f);
                }

                if (ch == 4)
                {
                    dst[baseIdx + 3] = src[baseIdx + 3];
                }
            }
        });

        return result;
    }
}
