using FImageStack.Core.Models;

namespace FImageStack.Core.Restoration;

public static class PsfGenerator
{
    public static unsafe ImageBuffer<float> CreatePsf(
        PsfKernelType type, 
        float radius = 2.5f, 
        float angleDegrees = 0.0f)
    {
        int kRadius = Math.Max(1, (int)MathF.Ceiling(radius * 2.0f));
        int size = kRadius * 2 + 1;
        var psf = new ImageBuffer<float>(size, size, 1, PixelFormatType.GrayFloat32);

        float* ptr = psf.DataPointer;
        float sum = 0f;

        switch (type)
        {
            case PsfKernelType.Gaussian:
                float sigma = MathF.Max(0.5f, radius);
                float twoSigmaSq = 2f * sigma * sigma;

                for (int y = -kRadius; y <= kRadius; y++)
                {
                    for (int x = -kRadius; x <= kRadius; x++)
                    {
                        float rSq = x * x + y * y;
                        float val = MathF.Exp(-rSq / twoSigmaSq);
                        int idx = (y + kRadius) * size + (x + kRadius);
                        ptr[idx] = val;
                        sum += val;
                    }
                }
                break;

            case PsfKernelType.DefocusDisc:
                float rLimitSq = radius * radius;
                for (int y = -kRadius; y <= kRadius; y++)
                {
                    for (int x = -kRadius; x <= kRadius; x++)
                    {
                        float dist = MathF.Sqrt(x * x + y * y);
                        // Anti-aliased circle edge
                        float val = Math.Clamp(radius - dist + 0.5f, 0f, 1f);
                        int idx = (y + kRadius) * size + (x + kRadius);
                        ptr[idx] = val;
                        sum += val;
                    }
                }
                break;

            case PsfKernelType.MotionBlur:
                float rad = angleDegrees * MathF.PI / 180f;
                float cosA = MathF.Cos(rad);
                float sinA = MathF.Sin(rad);

                for (int y = -kRadius; y <= kRadius; y++)
                {
                    for (int x = -kRadius; x <= kRadius; x++)
                    {
                        // Distance along and perpendicular to motion line
                        float projL = x * cosA + y * sinA;
                        float projPerp = -x * sinA + y * cosA;

                        float val = 0f;
                        if (MathF.Abs(projL) <= radius && MathF.Abs(projPerp) <= 0.75f)
                        {
                            val = Math.Clamp(1f - MathF.Abs(projPerp) / 0.75f, 0f, 1f);
                        }

                        int idx = (y + kRadius) * size + (x + kRadius);
                        ptr[idx] = val;
                        sum += val;
                    }
                }
                break;

            case PsfKernelType.AiryDisk:
            default:
                float w0 = MathF.Max(0.5f, radius);
                for (int y = -kRadius; y <= kRadius; y++)
                {
                    for (int x = -kRadius; x <= kRadius; x++)
                    {
                        float r = MathF.Sqrt(x * x + y * y) / w0;
                        float val;
                        if (r < 1e-4f) val = 1.0f;
                        else
                        {
                            // Airy pattern approximation: (2 * J1(pi*r) / (pi*r))^2
                            float xArg = MathF.PI * r;
                            val = MathF.Pow(MathF.Sin(xArg) / xArg, 2.0f);
                        }

                        int idx = (y + kRadius) * size + (x + kRadius);
                        ptr[idx] = val;
                        sum += val;
                    }
                }
                break;
        }

        // Normalize sum = 1.0
        if (sum > 0f)
        {
            float invSum = 1.0f / sum;
            int total = size * size;
            for (int i = 0; i < total; i++)
            {
                ptr[i] *= invSum;
            }
        }
        else
        {
            ptr[(kRadius * size) + kRadius] = 1.0f; // Delta impulse
        }

        return psf;
    }
}
