using FImageStack.Core.Models;

namespace FImageStack.Core.Quality;

public sealed class HistogramData
{
    public int[] Red { get; } = new int[256];
    public int[] Green { get; } = new int[256];
    public int[] Blue { get; } = new int[256];
    public int[] Luminance { get; } = new int[256];

    public int MaxFrequency { get; set; }
    public double MeanLuminance { get; set; }
    public double ShadowClippingPercent { get; set; }
    public double HighlightClippingPercent { get; set; }
}

public static class HistogramEngine
{
    public static unsafe HistogramData Compute(ImageBuffer<float>? buffer)
    {
        var data = new HistogramData();
        if (buffer == null) return data;

        int width = buffer.Width;
        int height = buffer.Height;
        int channels = buffer.Channels;
        int totalPixels = width * height;
        float* ptr = buffer.DataPointer;

        int[] rHist = new int[256];
        int[] gHist = new int[256];
        int[] bHist = new int[256];
        int[] lHist = new int[256];
        double sumLuma = 0;
        int shadowClipped = 0;
        int highlightClipped = 0;

        for (int i = 0; i < totalPixels; i++)
        {
            int idx = i * channels;
            float r = ptr[idx];
            float g = channels >= 3 ? ptr[idx + 1] : r;
            float b = channels >= 3 ? ptr[idx + 2] : r;

            int rBin = Math.Clamp((int)(r * 255f + 0.5f), 0, 255);
            int gBin = Math.Clamp((int)(g * 255f + 0.5f), 0, 255);
            int bBin = Math.Clamp((int)(b * 255f + 0.5f), 0, 255);

            float luma = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            int lBin = Math.Clamp((int)(luma * 255f + 0.5f), 0, 255);

            rHist[rBin]++;
            gHist[gBin]++;
            bHist[bBin]++;
            lHist[lBin]++;

            sumLuma += luma;
            if (lBin == 0) shadowClipped++;
            if (lBin == 255) highlightClipped++;
        }

        Array.Copy(rHist, data.Red, 256);
        Array.Copy(gHist, data.Green, 256);
        Array.Copy(bHist, data.Blue, 256);
        Array.Copy(lHist, data.Luminance, 256);

        int maxFreq = 0;
        for (int i = 1; i < 255; i++) // exclude pure 0 and 255 to avoid clipping spike dominating chart scale
        {
            if (lHist[i] > maxFreq) maxFreq = lHist[i];
        }
        data.MaxFrequency = Math.Max(maxFreq, 1);
        data.MeanLuminance = sumLuma / totalPixels;
        data.ShadowClippingPercent = (double)shadowClipped / totalPixels * 100.0;
        data.HighlightClippingPercent = (double)highlightClipped / totalPixels * 100.0;

        return data;
    }
}
