using FImageStack.Core;
using FImageStack.Core.Models;

namespace FImageStack.Core.Motion;

public sealed class MotionDetectionResult : IDisposable
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// Continuous motion intensity map [0.0 = static, 1.0 = heavy movement]
    /// </summary>
    public ImageBuffer<float> MotionMap { get; }

    /// <summary>
    /// Binary mask: true if pixel has significant motion across frames
    /// </summary>
    public ImageBuffer<byte> MovingMask { get; }

    public double OverallMotionPercentage { get; set; }

    public MotionDetectionResult(int width, int height)
    {
        Width = width;
        Height = height;
        MotionMap = new ImageBuffer<float>(width, height, 1, PixelFormatType.GrayFloat32);
        MovingMask = new ImageBuffer<byte>(width, height, 1, PixelFormatType.Gray8);
    }

    public void Dispose()
    {
        MotionMap.Dispose();
        MovingMask.Dispose();
    }
}

public interface IMotionDetector
{
    MotionDetectionResult DetectMotion(IReadOnlyList<StackFrame> frames, float motionThreshold = 0.08f);
}

public sealed class FrameDifferenceMotionDetector : IMotionDetector
{
    public unsafe MotionDetectionResult DetectMotion(IReadOnlyList<StackFrame> frames, float motionThreshold = 0.08f)
    {
        if (frames == null || frames.Count < 2)
            throw new ArgumentException("At least 2 frames are required for motion detection.", nameof(frames));

        int width = frames[0].Width;
        int height = frames[0].Height;
        int frameCount = frames.Count;

        var result = new MotionDetectionResult(width, height);
        float* motionPtr = result.MotionMap.DataPointer;
        byte* maskPtr = result.MovingMask.DataPointer;

        float*[] grayPointers = new float*[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            if (frames[i].GrayBuffer == null)
                throw new InvalidOperationException($"Frame {i} has no GrayBuffer.");
            grayPointers[i] = frames[i].GrayBuffer!.DataPointer;
        }

        // Calculate inter-frame difference variance for each pixel
        long movingPixelCount = 0;

        Parallel.For(0, height, () => 0L, (y, loopState, localMovingCount) =>
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                int idx = rowOffset + x;
                float maxDiff = 0f;
                float sumDiff = 0f;

                // Compare consecutive frame pairs
                for (int f = 0; f < frameCount - 1; f++)
                {
                    float diff = MathF.Abs(grayPointers[f + 1][idx] - grayPointers[f][idx]);
                    sumDiff += diff;
                    if (diff > maxDiff) maxDiff = diff;
                }

                float avgDiff = sumDiff / (frameCount - 1);
                float motionScore = Math.Clamp(maxDiff * 0.7f + avgDiff * 0.3f, 0f, 1f);
                motionPtr[idx] = motionScore;

                if (motionScore > motionThreshold)
                {
                    maskPtr[idx] = 255;
                    localMovingCount++;
                }
                else
                {
                    maskPtr[idx] = 0;
                }
            }
            return localMovingCount;
        },
        localMovingCount => Interlocked.Add(ref movingPixelCount, localMovingCount));

        result.OverallMotionPercentage = (double)movingPixelCount / (width * height) * 100.0;
        return result;
    }
}
