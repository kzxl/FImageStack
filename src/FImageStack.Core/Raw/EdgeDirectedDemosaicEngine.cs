using FImageStack.Core.Models;
using FImageStack.Core.PostProcessing;

namespace FImageStack.Core.Raw;

public interface IDemosaicEngine
{
    ImageBuffer<float> Demosaic(RawBayerBuffer raw, RawStackSettings settings);
}

public sealed class EdgeDirectedDemosaicEngine : IDemosaicEngine
{
    private readonly IToneMappingEngine _toneMappingEngine;

    public EdgeDirectedDemosaicEngine(IToneMappingEngine? toneMappingEngine = null)
    {
        _toneMappingEngine = toneMappingEngine ?? new ToneMappingEngine();
    }

    public unsafe ImageBuffer<float> Demosaic(RawBayerBuffer raw, RawStackSettings settings)
    {
        if (raw == null) throw new ArgumentNullException(nameof(raw));

        int w = raw.Width;
        int h = raw.Height;
        var pattern = raw.Pattern;

        var rgbOutput = new ImageBuffer<float>(w, h, 3, PixelFormatType.RgbFloat32);
        float* rawPtr = raw.Data.DataPointer;
        float* rgbPtr = rgbOutput.DataPointer;

        // Step 1: Directional Green Channel Reconstruction
        using var greenBuffer = new ImageBuffer<float>(w, h, 1, PixelFormatType.GrayFloat32);
        float* gPtr = greenBuffer.DataPointer;

        Parallel.For(0, h, y =>
        {
            int row = y * w;
            int yMod = y & 1;

            for (int x = 0; x < w; x++)
            {
                int xMod = x & 1;
                int cIdx = BayerFusionEngine.GetCfaChannelIndex(pattern, xMod, yMod);

                if (cIdx == 1 || cIdx == 2)
                {
                    // Photosite is already Green (Gr or Gb)
                    gPtr[row + x] = rawPtr[row + x];
                }
                else
                {
                    // Photosite is Red or Blue -> directional interpolation along gradient
                    int xL1 = Math.Max(0, x - 1);
                    int xR1 = Math.Min(w - 1, x + 1);
                    int xL2 = Math.Max(0, x - 2);
                    int xR2 = Math.Min(w - 1, x + 2);

                    int yU1 = Math.Max(0, y - 1);
                    int yD1 = Math.Min(h - 1, y + 1);
                    int yU2 = Math.Max(0, y - 2);
                    int yD2 = Math.Min(h - 1, y + 2);

                    float gL = rawPtr[row + xL1];
                    float gR = rawPtr[row + xR1];
                    float gU = rawPtr[yU1 * w + x];
                    float gD = rawPtr[yD1 * w + x];

                    float center = rawPtr[row + x];
                    float cL2 = rawPtr[row + xL2];
                    float cR2 = rawPtr[row + xR2];
                    float cU2 = rawPtr[yU2 * w + x];
                    float cD2 = rawPtr[yD2 * w + x];

                    float gradH = MathF.Abs(gL - gR) + MathF.Abs(2f * center - cL2 - cR2);
                    float gradV = MathF.Abs(gU - gD) + MathF.Abs(2f * center - cU2 - cD2);

                    float gEstH = 0.5f * (gL + gR) + 0.25f * (2f * center - cL2 - cR2);
                    float gEstV = 0.5f * (gU + gD) + 0.25f * (2f * center - cU2 - cD2);

                    if (gradH < gradV * 0.95f)
                    {
                        gPtr[row + x] = Math.Clamp(gEstH, 0f, 1f);
                    }
                    else if (gradV < gradH * 0.95f)
                    {
                        gPtr[row + x] = Math.Clamp(gEstV, 0f, 1f);
                    }
                    else
                    {
                        gPtr[row + x] = Math.Clamp(0.5f * (gEstH + gEstV), 0f, 1f);
                    }
                }
            }
        });

        // Step 2: Red and Blue Reconstruction using Color Differences (R - G and B - G)
        using var redDiff = new ImageBuffer<float>(w, h, 1, PixelFormatType.GrayFloat32);
        using var blueDiff = new ImageBuffer<float>(w, h, 1, PixelFormatType.GrayFloat32);
        float* rdPtr = redDiff.DataPointer;
        float* bdPtr = blueDiff.DataPointer;

        // Fill known R - G and B - G samples
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            int yMod = y & 1;
            for (int x = 0; x < w; x++)
            {
                int xMod = x & 1;
                int cIdx = BayerFusionEngine.GetCfaChannelIndex(pattern, xMod, yMod);
                if (cIdx == 0) // Red
                {
                    rdPtr[row + x] = rawPtr[row + x] - gPtr[row + x];
                }
                else if (cIdx == 3) // Blue
                {
                    bdPtr[row + x] = rawPtr[row + x] - gPtr[row + x];
                }
            }
        }

        // Bilinear interpolation of smooth color differences
        Parallel.For(0, h, y =>
        {
            int row = y * w;
            int yMod = y & 1;

            for (int x = 0; x < w; x++)
            {
                int xMod = x & 1;
                int cIdx = BayerFusionEngine.GetCfaChannelIndex(pattern, xMod, yMod);

                float gVal = gPtr[row + x];
                float rDiffVal = 0f;
                float bDiffVal = 0f;

                int xL = Math.Max(0, x - 1);
                int xR = Math.Min(w - 1, x + 1);
                int yU = Math.Max(0, y - 1);
                int yD = Math.Min(h - 1, y + 1);

                if (cIdx == 0)
                {
                    // Known Red
                    rDiffVal = rdPtr[row + x];
                    // Blue is at 4 diagonals (TL, TR, BL, BR)
                    bDiffVal = 0.25f * (bdPtr[yU * w + xL] + bdPtr[yU * w + xR] + bdPtr[yD * w + xL] + bdPtr[yD * w + xR]);
                }
                else if (cIdx == 3)
                {
                    // Known Blue
                    bDiffVal = bdPtr[row + x];
                    // Red is at 4 diagonals
                    rDiffVal = 0.25f * (rdPtr[yU * w + xL] + rdPtr[yU * w + xR] + rdPtr[yD * w + xL] + rdPtr[yD * w + xR]);
                }
                else if (cIdx == 1) // Gr (Red left/right, Blue up/down in RGGB)
                {
                    if (pattern == BayerPatternType.RGGB || pattern == BayerPatternType.GBRG)
                    {
                        rDiffVal = 0.5f * (rdPtr[row + xL] + rdPtr[row + xR]);
                        bDiffVal = 0.5f * (bdPtr[yU * w + x] + bdPtr[yD * w + x]);
                    }
                    else
                    {
                        bDiffVal = 0.5f * (bdPtr[row + xL] + bdPtr[row + xR]);
                        rDiffVal = 0.5f * (rdPtr[yU * w + x] + rdPtr[yD * w + x]);
                    }
                }
                else // Gb (Blue left/right, Red up/down in RGGB)
                {
                    if (pattern == BayerPatternType.RGGB || pattern == BayerPatternType.GBRG)
                    {
                        rDiffVal = 0.5f * (rdPtr[yU * w + x] + rdPtr[yD * w + x]);
                        bDiffVal = 0.5f * (bdPtr[row + xL] + bdPtr[row + xR]);
                    }
                    else
                    {
                        bDiffVal = 0.5f * (bdPtr[yU * w + x] + bdPtr[yD * w + x]);
                        rDiffVal = 0.5f * (rdPtr[row + xL] + rdPtr[row + xR]);
                    }
                }

                float rVal = Math.Clamp(gVal + rDiffVal, 0f, 1f);
                float bVal = Math.Clamp(gVal + bDiffVal, 0f, 1f);

                // Step 3: White Balance Gains
                if (settings.ApplyWhiteBalance)
                {
                    rVal *= raw.WhiteBalanceGains[0];
                    gVal *= raw.WhiteBalanceGains[1];
                    bVal *= raw.WhiteBalanceGains[2];
                }

                // Step 4: Color Correction Matrix (3x3)
                float finalR = rVal;
                float finalG = gVal;
                float finalB = bVal;

                if (settings.ApplyColorMatrix && raw.ColorMatrix.Length >= 9)
                {
                    var m = raw.ColorMatrix;
                    finalR = m[0] * rVal + m[1] * gVal + m[2] * bVal;
                    finalG = m[3] * rVal + m[4] * gVal + m[5] * bVal;
                    finalB = m[6] * rVal + m[7] * gVal + m[8] * bVal;
                }

                int dstIdx = (y * w + x) * 3;
                rgbPtr[dstIdx]     = Math.Clamp(finalR, 0f, 1f);
                rgbPtr[dstIdx + 1] = Math.Clamp(finalG, 0f, 1f);
                rgbPtr[dstIdx + 2] = Math.Clamp(finalB, 0f, 1f);
            }
        });

        // Step 5: Tone Mapping
        var toneMapped = _toneMappingEngine.ApplyToneMapping(
            rgbOutput,
            settings.ToneMapping,
            settings.ExposureEV);

        rgbOutput.Dispose();
        return toneMapped;
    }
}
