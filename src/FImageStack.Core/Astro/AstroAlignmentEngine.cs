using System.Numerics;
using FImageStack.Core.Models;

namespace FImageStack.Core.Astro;

public interface IAstroAlignmentEngine
{
    float[]? EstimateRigidTransform(IReadOnlyList<StarCandidate> refStars, IReadOnlyList<StarCandidate> targetStars, float tolerance = 0.02f);
    void AlignAstroStack(IReadOnlyList<StackFrame> frames, IProgress<StackProgress>? progress = null);
    ImageBuffer<float> WarpFrame(ImageBuffer<float> source, float[] transformMatrix, int targetWidth, int targetHeight);
}

public sealed class AstroAlignmentEngine : IAstroAlignmentEngine
{
    private readonly IStarDetector _starDetector;

    public AstroAlignmentEngine(IStarDetector? starDetector = null)
    {
        _starDetector = starDetector ?? new StarDetector();
    }

    public float[]? EstimateRigidTransform(
        IReadOnlyList<StarCandidate> refStars, 
        IReadOnlyList<StarCandidate> targetStars, 
        float tolerance = 0.02f)
    {
        if (refStars.Count < 3 || targetStars.Count < 3) return null;

        // 1. Build triangles for reference and target
        var refTriangles = BuildTriangles(refStars, maxStars: 25);
        var tgtTriangles = BuildTriangles(targetStars, maxStars: 25);

        // 2. Vote for star point correspondences
        var matchVotes = new Dictionary<(int refIdx, int tgtIdx), int>();

        foreach (var tRef in refTriangles)
        {
            foreach (var tTgt in tgtTriangles)
            {
                if (MathF.Abs(tRef.Ratio1 - tTgt.Ratio1) < tolerance &&
                    MathF.Abs(tRef.Ratio2 - tTgt.Ratio2) < tolerance)
                {
                    // Match the 3 vertices in order of opposite side lengths
                    VotePair(matchVotes, tRef.StarIdx1, tTgt.StarIdx1);
                    VotePair(matchVotes, tRef.StarIdx2, tTgt.StarIdx2);
                    VotePair(matchVotes, tRef.StarIdx3, tTgt.StarIdx3);
                }
            }
        }

        if (matchVotes.Count < 3)
        {
            // Fallback: simple centroid shift if rotation is small
            return FallbackTranslation(refStars, targetStars);
        }

        // 3. Extract top voted star pairs
        var sortedPairs = matchVotes.OrderByDescending(kv => kv.Value).Take(15).ToList();
        if (sortedPairs.Count < 3) return FallbackTranslation(refStars, targetStars);

        var ptRef = new List<Vector2>();
        var ptTgt = new List<Vector2>();

        foreach (var pair in sortedPairs)
        {
            if (pair.Value >= 2) // At least 2 triangle votes
            {
                ptRef.Add(new Vector2(refStars[pair.Key.refIdx].X, refStars[pair.Key.refIdx].Y));
                ptTgt.Add(new Vector2(targetStars[pair.Key.tgtIdx].X, targetStars[pair.Key.tgtIdx].Y));
            }
        }

        if (ptRef.Count < 3) return FallbackTranslation(refStars, targetStars);

        // 4. Solve Least Squares Rigid/Similarity Transform: Tgt -> Ref
        // Ref = R * Tgt + T
        return SolveRigidTransform(ptTgt, ptRef);
    }

    public void AlignAstroStack(IReadOnlyList<StackFrame> frames, IProgress<StackProgress>? progress = null)
    {
        if (frames == null || frames.Count <= 1) return;

        // Frame 0 is reference
        var refFrame = frames[0];
        if (refFrame.GrayBuffer == null) return;

        var refStars = _starDetector.DetectStars(refFrame.GrayBuffer, thresholdSigma: 3.5f, maxStars: 60);

        for (int i = 1; i < frames.Count; i++)
        {
            var targetFrame = frames[i];
            if (targetFrame.GrayBuffer == null) continue;

            var tgtStars = _starDetector.DetectStars(targetFrame.GrayBuffer, thresholdSigma: 3.5f, maxStars: 60);
            var transform = EstimateRigidTransform(refStars, tgtStars);

            if (transform != null && targetFrame.ColorBuffer != null)
            {
                var warpedColor = WarpFrame(targetFrame.ColorBuffer, transform, refFrame.Width, refFrame.Height);
                targetFrame.ColorBuffer.Dispose();
                targetFrame.ColorBuffer = warpedColor;

                if (targetFrame.GrayBuffer != null)
                {
                    var warpedGray = WarpFrame(targetFrame.GrayBuffer, transform, refFrame.Width, refFrame.Height);
                    targetFrame.GrayBuffer.Dispose();
                    targetFrame.GrayBuffer = warpedGray;
                }
            }

            progress?.Report(new StackProgress("Astro Alignment", (double)(i + 1) / frames.Count * 100, $"Aligned star field {i + 1}/{frames.Count}"));
        }
    }

    public unsafe ImageBuffer<float> WarpFrame(
        ImageBuffer<float> source, 
        float[] transformMatrix, 
        int targetWidth, 
        int targetHeight)
    {
        // transformMatrix maps Tgt -> Ref: [a, -b, tx, b, a, ty] (2x3)
        // For backward sampling, we invert the transform: Ref -> Tgt
        float a = transformMatrix[0];
        float b = transformMatrix[1];
        float tx = transformMatrix[2];
        float ty = transformMatrix[5];

        float det = a * a + b * b;
        if (det < 1e-6f) det = 1f;

        // Inverse 2D Rigid:
        // invA = a / det, invB = -b / det
        // invTx = (-tx * a - ty * b) / det
        // invTy = (tx * b - ty * a) / det
        float invA = a / det;
        float invB = -b / det;
        float invTx = (-tx * a - ty * b) / det;
        float invTy = (tx * b - ty * a) / det;

        int ch = source.Channels;
        var output = new ImageBuffer<float>(targetWidth, targetHeight, ch, source.Format);
        float* srcPtr = source.DataPointer;
        float* dstPtr = output.DataPointer;
        int srcW = source.Width;
        int srcH = source.Height;

        Parallel.For(0, targetHeight, y =>
        {
            int dstRowOffset = y * targetWidth * ch;

            for (int x = 0; x < targetWidth; x++)
            {
                // Map (x, y) in output (ref) to (srcX, srcY) in source
                float srcX = invA * x - invB * y + invTx;
                float srcY = invB * x + invA * y + invTy;

                int x0 = (int)MathF.Floor(srcX);
                int y0 = (int)MathF.Floor(srcY);
                int x1 = x0 + 1;
                int y1 = y0 + 1;

                int dstBase = dstRowOffset + x * ch;

                if (x0 >= 0 && x1 < srcW && y0 >= 0 && y1 < srcH)
                {
                    float fx = srcX - x0;
                    float fy = srcY - y0;
                    float wTL = (1f - fx) * (1f - fy);
                    float wTR = fx * (1f - fy);
                    float wBL = (1f - fx) * fy;
                    float wBR = fx * fy;

                    int idxTL = (y0 * srcW + x0) * ch;
                    int idxTR = (y0 * srcW + x1) * ch;
                    int idxBL = (y1 * srcW + x0) * ch;
                    int idxBR = (y1 * srcW + x1) * ch;

                    for (int c = 0; c < ch; c++)
                    {
                        dstPtr[dstBase + c] = wTL * srcPtr[idxTL + c] +
                                              wTR * srcPtr[idxTR + c] +
                                              wBL * srcPtr[idxBL + c] +
                                              wBR * srcPtr[idxBR + c];
                    }
                }
                else
                {
                    for (int c = 0; c < ch; c++) dstPtr[dstBase + c] = 0f;
                }
            }
        });

        return output;
    }

    private static List<StarTriangle> BuildTriangles(IReadOnlyList<StarCandidate> stars, int maxStars)
    {
        var triangles = new List<StarTriangle>();
        int n = Math.Min(stars.Count, maxStars);

        for (int i = 0; i < n - 2; i++)
        {
            for (int j = i + 1; j < n - 1; j++)
            {
                for (int k = j + 1; k < n; k++)
                {
                    float d12 = Distance(stars[i], stars[j]);
                    float d23 = Distance(stars[j], stars[k]);
                    float d31 = Distance(stars[k], stars[i]);

                    if (d12 < 5f || d23 < 5f || d31 < 5f) continue; // Skip degenerates

                    // Sort sides L1 <= L2 <= L3
                    var sides = new (float len, int idxOpposite)[]
                    {
                        (d23, i), // opposite to i is side (j,k)
                        (d31, j), // opposite to j is side (k,i)
                        (d12, k)  // opposite to k is side (i,j)
                    };
                    Array.Sort(sides, (a, b) => a.len.CompareTo(b.len));

                    float l1 = sides[0].len;
                    float l2 = sides[1].len;
                    float l3 = sides[2].len;

                    triangles.Add(new StarTriangle
                    {
                        StarIdx1 = sides[0].idxOpposite,
                        StarIdx2 = sides[1].idxOpposite,
                        StarIdx3 = sides[2].idxOpposite,
                        Ratio1 = l1 / l3,
                        Ratio2 = l2 / l3
                    });
                }
            }
        }

        return triangles;
    }

    private static void VotePair(Dictionary<(int, int), int> votes, int refIdx, int tgtIdx)
    {
        var key = (refIdx, tgtIdx);
        votes[key] = votes.GetValueOrDefault(key, 0) + 1;
    }

    private static float[]? SolveRigidTransform(IReadOnlyList<Vector2> src, IReadOnlyList<Vector2> dst)
    {
        int count = src.Count;
        if (count < 2) return null;

        // Centroids
        Vector2 cSrc = Vector2.Zero;
        Vector2 cDst = Vector2.Zero;
        for (int i = 0; i < count; i++)
        {
            cSrc += src[i];
            cDst += dst[i];
        }
        cSrc /= count;
        cDst /= count;

        // Kabsch algorithm for 2D rotation
        float sxx = 0f, sxy = 0f, syx = 0f, syy = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector2 ps = src[i] - cSrc;
            Vector2 pd = dst[i] - cDst;
            sxx += ps.X * pd.X;
            sxy += ps.X * pd.Y;
            syx += ps.Y * pd.X;
            syy += ps.Y * pd.Y;
        }

        float theta = MathF.Atan2(sxy - syx, sxx + syy);
        float cosT = MathF.Cos(theta);
        float sinT = MathF.Sin(theta);

        float tx = cDst.X - (cosT * cSrc.X - sinT * cSrc.Y);
        float ty = cDst.Y - (sinT * cSrc.X + cosT * cSrc.Y);

        return new float[] { cosT, -sinT, tx, sinT, cosT, ty };
    }

    private static float[] FallbackTranslation(IReadOnlyList<StarCandidate> refStars, IReadOnlyList<StarCandidate> tgtStars)
    {
        float dx = refStars[0].X - tgtStars[0].X;
        float dy = refStars[0].Y - tgtStars[0].Y;
        return new float[] { 1f, 0f, dx, 0f, 1f, dy };
    }

    private static float Distance(StarCandidate a, StarCandidate b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
