using System.Numerics;
using FImageStack.Core.Astro;
using FImageStack.Core.Models;
using Xunit;

namespace FImageStack.Core.Tests;

public class AstroStackTests
{
    private static ImageBuffer<float> CreateSyntheticStarField(int w, int h, List<(float x, float y, float flux)> stars, float bgNoise = 0.02f)
    {
        var buffer = new ImageBuffer<float>(w, h, 1, PixelFormatType.GrayFloat32);
        var rand = new Random(42);

        // Background noise
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float n = (float)rand.NextDouble() * bgNoise;
                buffer.At(x, y) = 0.04f + n;
            }
        }

        // Add Gaussian star spots
        foreach (var (sx, sy, flux) in stars)
        {
            int r = 4;
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int px = (int)sx + dx;
                    int py = (int)sy + dy;
                    if (px >= 0 && px < w && py >= 0 && py < h)
                    {
                        float distSq = (px - sx) * (px - sx) + (py - sy) * (py - sy);
                        float val = flux * MathF.Exp(-distSq / (2f * 1.2f * 1.2f));
                        buffer.At(px, py) += val;
                    }
                }
            }
        }

        return buffer;
    }

    [Fact]
    public void StarDetector_ShouldDetectSyntheticStarCentroids()
    {
        int w = 64;
        int h = 64;
        var trueStars = new List<(float x, float y, float flux)>
        {
            (15.2f, 20.4f, 0.8f),
            (45.7f, 18.1f, 0.9f),
            (32.0f, 48.3f, 0.7f),
            (50.1f, 52.6f, 0.6f)
        };

        using var starField = CreateSyntheticStarField(w, h, trueStars);
        var detector = new StarDetector();
        var detected = detector.DetectStars(starField, thresholdSigma: 2.5f, maxStars: 20);

        Assert.True(detected.Count >= 4, $"Detected only {detected.Count} stars");

        // Verify each true star is detected near its expected location (< 1.0 pixel error)
        foreach (var (tx, ty, _) in trueStars)
        {
            bool matched = detected.Any(s => MathF.Sqrt((s.X - tx) * (s.X - tx) + (s.Y - ty) * (s.Y - ty)) < 1.0f);
            Assert.True(matched, $"True star at ({tx:F1}, {ty:F1}) was not detected.");
        }
    }

    [Fact]
    public void AstroAlignment_ShouldEstimateRigidTransformFromTriangles()
    {
        var refStars = new List<StarCandidate>
        {
            new() { X = 10f, Y = 10f },
            new() { X = 30f, Y = 15f },
            new() { X = 20f, Y = 40f },
            new() { X = 50f, Y = 45f }
        };

        // Shift by dx = 5, dy = -3
        var tgtStars = new List<StarCandidate>
        {
            new() { X = 15f, Y = 7f },
            new() { X = 35f, Y = 12f },
            new() { X = 25f, Y = 37f },
            new() { X = 55f, Y = 42f }
        };

        var alignEngine = new AstroAlignmentEngine();
        var matrix = alignEngine.EstimateRigidTransform(refStars, tgtStars, tolerance: 0.05f);

        Assert.NotNull(matrix);
        // Translation tx ~ -5, ty ~ +3 for mapping tgt to ref (or vice versa)
        Assert.True(MathF.Abs(matrix[0] - 1.0f) < 0.1f); // Cos theta ~ 1
        Assert.True(MathF.Abs(matrix[2] - (-5.0f)) < 1.0f || MathF.Abs(matrix[2] - 5.0f) < 1.0f);
    }

    [Fact]
    public void AstroCalibration_DarkAndFlatCorrection_ShouldRemoveGlowAndVignette()
    {
        int w = 32;
        int h = 32;
        var light = new StackFrame
        {
            Index = 0,
            Width = w,
            Height = h,
            ColorBuffer = new ImageBuffer<float>(w, h, 3),
            GrayBuffer = new ImageBuffer<float>(w, h, 1)
        };

        // Inject sensor amp glow in bottom-right corner and vignette
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float val = 0.5f;
                if (x > 25 && y > 25) val += 0.3f; // Amp glow
                light.ColorBuffer.At(x, y, 0) = val;
                light.ColorBuffer.At(x, y, 1) = val;
                light.ColorBuffer.At(x, y, 2) = val;
            }
        }

        // Dark frame with the same amp glow
        var dark = new StackFrame
        {
            Index = 0,
            Width = w,
            Height = h,
            ColorBuffer = new ImageBuffer<float>(w, h, 3)
        };
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dVal = (x > 25 && y > 25) ? 0.3f : 0.01f;
                dark.ColorBuffer.At(x, y, 0) = dVal;
                dark.ColorBuffer.At(x, y, 1) = dVal;
                dark.ColorBuffer.At(x, y, 2) = dVal;
            }
        }

        var calEngine = new AstroCalibrationEngine();
        using var masterDark = calEngine.CreateMasterDark(new List<StackFrame> { dark });

        calEngine.CalibrateLightFrame(light, masterDark, null, null);

        // After calibration, bottom-right should no longer have excess glow (should be close to 0.5)
        float calibratedVal = light.ColorBuffer.At(28, 28, 0);
        Assert.True(MathF.Abs(calibratedVal - 0.5f) < 0.05f, $"Amp glow not removed: val={calibratedVal}");

        light.Dispose();
        dark.Dispose();
    }
}
