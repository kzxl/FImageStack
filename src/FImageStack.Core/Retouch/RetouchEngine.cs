using FImageStack.Core;
using FImageStack.Core.Models;

namespace FImageStack.Core.Retouch;

public enum RetouchToolType
{
    SourceBrush,
    Erase,
    Restore
}

public sealed class RetouchStroke
{
    public int StrokeId { get; set; }
    public RetouchToolType Tool { get; set; } = RetouchToolType.SourceBrush;
    public int SourceFrameIndex { get; set; }
    public float CenterX { get; set; }
    public float CenterY { get; set; }
    public float Radius { get; set; } = 25f;
    public float Feather { get; set; } = 0.5f;
    public float Opacity { get; set; } = 1.0f;
}

public sealed class RetouchLayer : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public List<RetouchStroke> Strokes { get; } = new();
    private readonly Stack<RetouchStroke> _undoneStrokes = new();

    public RetouchLayer(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public void AddStroke(RetouchStroke stroke)
    {
        Strokes.Add(stroke);
        _undoneStrokes.Clear();
    }

    public bool Undo()
    {
        if (Strokes.Count == 0) return false;
        var last = Strokes[^1];
        Strokes.RemoveAt(Strokes.Count - 1);
        _undoneStrokes.Push(last);
        return true;
    }

    public bool Redo()
    {
        if (_undoneStrokes.Count == 0) return false;
        var stroke = _undoneStrokes.Pop();
        Strokes.Add(stroke);
        return true;
    }

    public unsafe ImageBuffer<float> RenderComposite(
        ImageBuffer<float> baseFusedImage,
        IReadOnlyList<StackFrame> frames)
    {
        var output = baseFusedImage.Clone();
        if (Strokes.Count == 0) return output;

        float* outPtr = output.DataPointer;
        float* basePtr = baseFusedImage.DataPointer;
        int frameCount = frames.Count;

        float*[] colorPointers = new float*[frameCount];
        for (int f = 0; f < frameCount; f++)
        {
            colorPointers[f] = frames[f].ColorBuffer!.DataPointer;
        }

        foreach (var stroke in Strokes)
        {
            int targetFrame = Math.Clamp(stroke.SourceFrameIndex, 0, frameCount - 1);
            float* srcColor = colorPointers[targetFrame];

            int x0 = Math.Max(0, (int)(stroke.CenterX - stroke.Radius));
            int y0 = Math.Max(0, (int)(stroke.CenterY - stroke.Radius));
            int x1 = Math.Min(Width, (int)(stroke.CenterX + stroke.Radius + 1));
            int y1 = Math.Min(Height, (int)(stroke.CenterY + stroke.Radius + 1));

            float rSq = stroke.Radius * stroke.Radius;
            float innerRadius = stroke.Radius * (1f - stroke.Feather);
            float innerSq = innerRadius * innerRadius;

            for (int y = y0; y < y1; y++)
            {
                int rowOffset = y * Width;
                float dy = y - stroke.CenterY;
                float dySq = dy * dy;

                for (int x = x0; x < x1; x++)
                {
                    float dx = x - stroke.CenterX;
                    float distSq = dx * dx + dySq;
                    if (distSq > rSq) continue;

                    float weight = stroke.Opacity;
                    if (distSq > innerSq && stroke.Feather > 0)
                    {
                        float dist = MathF.Sqrt(distSq);
                        float featherT = (dist - innerRadius) / (stroke.Radius - innerRadius + 1e-5f);
                        weight *= 0.5f * (1.0f + MathF.Cos(featherT * MathF.PI));
                    }

                    int idx = (rowOffset + x) * 3;

                    if (stroke.Tool == RetouchToolType.SourceBrush)
                    {
                        outPtr[idx] = outPtr[idx] * (1f - weight) + srcColor[idx] * weight;
                        outPtr[idx + 1] = outPtr[idx + 1] * (1f - weight) + srcColor[idx + 1] * weight;
                        outPtr[idx + 2] = outPtr[idx + 2] * (1f - weight) + srcColor[idx + 2] * weight;
                    }
                    else if (stroke.Tool == RetouchToolType.Restore)
                    {
                        outPtr[idx] = outPtr[idx] * (1f - weight) + basePtr[idx] * weight;
                        outPtr[idx + 1] = outPtr[idx + 1] * (1f - weight) + basePtr[idx + 1] * weight;
                        outPtr[idx + 2] = outPtr[idx + 2] * (1f - weight) + basePtr[idx + 2] * weight;
                    }
                }
            }
        }

        return output;
    }

    public void Dispose()
    {
        Strokes.Clear();
        _undoneStrokes.Clear();
    }
}
