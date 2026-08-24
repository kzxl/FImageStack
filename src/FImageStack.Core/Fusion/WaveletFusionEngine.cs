using FImageStack.Core.DepthMap;
using FImageStack.Core.Models;

namespace FImageStack.Core.Fusion;

public sealed class WaveletFusionEngine : IFusionEngine
{
    public FusionMethod Method => FusionMethod.WaveletDWT;

    public unsafe ImageBuffer<float> Fuse(
        IReadOnlyList<StackFrame> frames,
        DepthMapResult depthResult,
        FusionSettings settings)
    {
        if (frames == null || frames.Count == 0)
            throw new ArgumentException("Frames list cannot be empty.", nameof(frames));

        int width = depthResult.Width;
        int height = depthResult.Height;
        int frameCount = frames.Count;
        int channels = frames[0].ColorBuffer!.Channels;

        var output = new ImageBuffer<float>(width, height, channels, frames[0].ColorBuffer!.Format);
        float* outPtr = output.DataPointer;
        int* srcMap = depthResult.SourceFrameMap.DataPointer;
        float* confMap = depthResult.ConfidenceMap.DataPointer;

        float*[] colorPointers = new float*[frameCount];
        float*[] focusPointers = new float*[frameCount];

        for (int f = 0; f < frameCount; f++)
        {
            colorPointers[f] = frames[f].ColorBuffer!.DataPointer;
            focusPointers[f] = frames[f].FocusMap!.DataPointer;
        }

        // 2D Haar Wavelet Multi-Scale Frequency Fusion
        // Process in 2x2 wavelet blocks
        Parallel.For(0, height / 2, by =>
        {
            int y0 = by * 2;
            int y1 = y0 + 1;

            int row0 = y0 * width;
            int row1 = y1 * width;
            float[] frameWeights = new float[frameCount];

            for (int bx = 0; bx < width / 2; bx++)
            {
                int x0 = bx * 2;
                int x1 = x0 + 1;

                // For this 2x2 block, determine the dominant frame and wavelet detail energy
                float maxEnergy = -1f;
                int bestFrame = 0;
                float sumWeights = 0f;

                for (int f = 0; f < frameCount; f++)
                {
                    float* focus = focusPointers[f];
                    float blockSharp =
                        focus[row0 + x0] + focus[row0 + x1] +
                        focus[row1 + x0] + focus[row1 + x1];

                    float w = MathF.Pow(blockSharp + 1e-6f, 3.0f);
                    frameWeights[f] = w;
                    sumWeights += w;

                    if (blockSharp > maxEnergy)
                    {
                        maxEnergy = blockSharp;
                        bestFrame = f;
                    }
                }

                float invSum = sumWeights > 0 ? 1f / sumWeights : 1f;

                // Decompose 2x2 into LL (Approximation) and LH, HL, HH (Detail)
                // LL is fused via weighted blend; LH, HL, HH are taken from bestFrame to maximize crispness
                for (int c = 0; c < channels; c++)
                {
                    // Approximation LL blend
                    float fusedLL = 0f;
                    for (int f = 0; f < frameCount; f++)
                    {
                        float* col = colorPointers[f];
                        float a = col[(row0 + x0) * channels + c];
                        float b = col[(row0 + x1) * channels + c];
                        float d = col[(row1 + x0) * channels + c];
                        float e = col[(row1 + x1) * channels + c];
                        float ll = 0.5f * (a + b + d + e);
                        fusedLL += ll * (frameWeights[f] * invSum);
                    }

                    // Detail from sharpest frame
                    float* bestCol = colorPointers[bestFrame];
                    float ba = bestCol[(row0 + x0) * channels + c];
                    float bb = bestCol[(row0 + x1) * channels + c];
                    float bd = bestCol[(row1 + x0) * channels + c];
                    float be = bestCol[(row1 + x1) * channels + c];

                    float lh = 0.5f * ((ba + bb) - (bd + be));
                    float hl = 0.5f * ((ba - bb) + (bd - be));
                    float hh = 0.5f * ((ba - bb) - (bd - be));

                    // Inverse 2D Haar DWT Reconstruction:
                    // a = 0.5 * (LL + LH + HL + HH)
                    // b = 0.5 * (LL + LH - HL - HH)
                    // d = 0.5 * (LL - LH + HL - HH)
                    // e = 0.5 * (LL - LH - HL + HH)
                    float outA = Math.Clamp(0.5f * (fusedLL + lh + hl + hh), 0f, 1f);
                    float outB = Math.Clamp(0.5f * (fusedLL + lh - hl - hh), 0f, 1f);
                    float outD = Math.Clamp(0.5f * (fusedLL - lh + hl - hh), 0f, 1f);
                    float outE = Math.Clamp(0.5f * (fusedLL - lh - hl + hh), 0f, 1f);

                    outPtr[(row0 + x0) * channels + c] = outA;
                    outPtr[(row0 + x1) * channels + c] = outB;
                    outPtr[(row1 + x0) * channels + c] = outD;
                    outPtr[(row1 + x1) * channels + c] = outE;
                }
            }
        });

        return output;
    }
}
