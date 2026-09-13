using System.Runtime.CompilerServices;
using FImageStack.Core.Alignment;
using FImageStack.Core.Models;

namespace FImageStack.Core.Stitching;

public interface IStitchRegistrationEngine
{
    List<float[]> ComputeGlobalHomographies(
        IReadOnlyList<StackFrame> frames,
        StitchSettings settings,
        int referenceIndex,
        IProgress<StackProgress>? progress = null);
}

public sealed class StitchRegistrationEngine : IStitchRegistrationEngine
{
    private readonly IHomographyEstimator _homographyEstimator;

    public StitchRegistrationEngine(IHomographyEstimator? homographyEstimator = null)
    {
        _homographyEstimator = homographyEstimator ?? new HomographyEstimator();
    }

    public List<float[]> ComputeGlobalHomographies(
        IReadOnlyList<StackFrame> frames,
        StitchSettings settings,
        int referenceIndex,
        IProgress<StackProgress>? progress = null)
    {
        int count = frames.Count;
        var globalMatrices = new List<float[]>(count);
        for (int i = 0; i < count; i++)
        {
            // Default 3x3 identity
            globalMatrices.Add(new float[9] { 1, 0, 0, 0, 1, 0, 0, 0, 1 });
        }

        if (count <= 1) return globalMatrices;

        referenceIndex = Math.Clamp(referenceIndex, 0, count - 1);

        // Track pairwise relative transforms (from i to j)
        // Store: (from, to, H, confidence)
        var edges = new List<(int from, int to, float[] H, double confidence)>();

        int totalPairs = (count * (count - 1)) / 2;
        int processedPairs = 0;

        // In sequential panorama or grid scanning, adjacent indices (i, i+1) are most likely to overlap.
        // We evaluate adjacent pairs first, then pairs within distance 2..N
        for (int step = 1; step < count; step++)
        {
            for (int i = 0; i < count - step; i++)
            {
                int j = i + step;

                // Attempt to register frame i to frame j
                var (h, conf) = RegisterPair(frames[i], frames[j], settings);
                processedPairs++;

                if (conf > 0.15)
                {
                    edges.Add((i, j, h, conf));
                    // Also store inverse: from j to i
                    float[] invH = _homographyEstimator.InvertHomography(h);
                    edges.Add((j, i, invH, conf));
                }

                progress?.Report(new StackProgress(
                    "Stitch Registration",
                    (double)processedPairs / totalPairs * 50.0,
                    $"Matching pair #{i + 1} & #{j + 1} (conf: {conf:F2})"));
            }
        }

        // Build transformation tree rooted at referenceIndex using BFS with priority on highest confidence
        var visited = new bool[count];
        var queue = new Queue<int>();

        visited[referenceIndex] = true;
        queue.Enqueue(referenceIndex);

        while (queue.Count > 0)
        {
            int curr = queue.Dequeue();
            float[] currGlobal = globalMatrices[curr];

            // Find all outgoing edges from curr to unvisited neighbors
            var neighbors = edges.Where(e => e.from == curr && !visited[e.to])
                                 .OrderByDescending(e => e.confidence)
                                 .ToList();

            foreach (var edge in neighbors)
            {
                int neighbor = edge.to;
                if (visited[neighbor]) continue;

                // global(neighbor) = currGlobal * H(curr -> neighbor)
                // Actually edge.H maps points in curr to neighbor: x_neighbor = H * x_curr
                // Therefore x_curr = inv(H) * x_neighbor.
                // We want to map points in neighbor back to the reference coordinate system (currGlobal maps curr to ref).
                // H(neighbor -> curr) is Invert(edge.H)
                float[] hNeighborToCurr = _homographyEstimator.InvertHomography(edge.H);
                globalMatrices[neighbor] = MultiplyHomographies(currGlobal, hNeighborToCurr);

                visited[neighbor] = true;
                queue.Enqueue(neighbor);
            }
        }

        // Handle any unvisited / disconnected frames (fallback to simple tile offset estimation)
        for (int i = 0; i < count; i++)
        {
            if (!visited[i])
            {
                // Fallback: place sequentially next to nearest visited neighbor
                int nearest = referenceIndex;
                int minDist = int.MaxValue;
                for (int v = 0; v < count; v++)
                {
                    if (visited[v] && Math.Abs(v - i) < minDist)
                    {
                        nearest = v;
                        minDist = Math.Abs(v - i);
                    }
                }

                float shiftX = (i - nearest) * frames[0].Width * (1.0f - settings.SearchOverlapRatio);
                float[] approxH = new float[9] { 1, 0, shiftX, 0, 1, 0, 0, 0, 1 };
                globalMatrices[i] = MultiplyHomographies(globalMatrices[nearest], approxH);
                visited[i] = true;
            }
        }

        progress?.Report(new StackProgress("Stitch Registration", 50.0, $"Resolved global alignment for {count} tiles"));
        return globalMatrices;
    }

    private unsafe (float[] H, double confidence) RegisterPair(
        StackFrame frameA,
        StackFrame frameB,
        StitchSettings settings)
    {
        int w = frameA.Width;
        int h = frameA.Height;

        if (frameA.GrayBuffer == null || frameB.GrayBuffer == null)
        {
            return (new float[9] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, 0.0);
        }

        float* grayA = frameA.GrayBuffer.DataPointer;
        float* grayB = frameB.GrayBuffer.DataPointer;

        int patchSize = Math.Clamp(Math.Min(w, h) / 6, 12, 32);
        int halfPatch = patchSize / 2;
        float maxOverlap = Math.Clamp(settings.SearchOverlapRatio * 1.5f, 0.15f, 0.75f);
        int overlapPxW = (int)(w * maxOverlap);
        int overlapPxH = (int)(h * maxOverlap);

        // 4 Candidate overlap orientations:
        // 0: A is Left, B is Right  (A.Right overlaps B.Left)
        // 1: A is Right, B is Left  (A.Left overlaps B.Right)
        // 2: A is Top, B is Bottom  (A.Bottom overlaps B.Top)
        // 3: A is Bottom, B is Top  (A.Top overlaps B.Bottom)
        
        float[] bestH = new float[9] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        double bestQuality = 0.0;
        double bestConf = 0.0;

        for (int dir = 0; dir < 4; dir++)
        {
            var sampleCenters = new List<(int cx, int cy)>();
            int minBx, maxBx, minBy, maxBy;

            if (dir == 0) // A on Left, B on Right
            {
                int sampleX = w - Math.Max(halfPatch + 2, overlapPxW / 2);
                for (int y = halfPatch; y < h - halfPatch; y += Math.Max(halfPatch, h / 10))
                    sampleCenters.Add((sampleX, y));

                minBx = 0; maxBx = overlapPxW;
                minBy = 0; maxBy = h - 1;
            }
            else if (dir == 1) // A on Right, B on Left
            {
                int sampleX = Math.Max(halfPatch + 2, overlapPxW / 2);
                for (int y = halfPatch; y < h - halfPatch; y += Math.Max(halfPatch, h / 10))
                    sampleCenters.Add((sampleX, y));

                minBx = w - overlapPxW; maxBx = w - 1;
                minBy = 0; maxBy = h - 1;
            }
            else if (dir == 2) // A on Top, B on Bottom
            {
                int sampleY = h - Math.Max(halfPatch + 2, overlapPxH / 2);
                for (int x = halfPatch; x < w - halfPatch; x += Math.Max(halfPatch, w / 10))
                    sampleCenters.Add((x, sampleY));

                minBx = 0; maxBx = w - 1;
                minBy = 0; maxBy = overlapPxH;
            }
            else // A on Bottom, B on Top
            {
                int sampleY = Math.Max(halfPatch + 2, overlapPxH / 2);
                for (int x = halfPatch; x < w - halfPatch; x += Math.Max(halfPatch, w / 10))
                    sampleCenters.Add((x, sampleY));

                minBx = 0; maxBx = w - 1;
                minBy = h - overlapPxH; maxBy = h - 1;
            }

            var matchedPairs = new List<(float srcX, float srcY, float dstX, float dstY, float score)>();
            int searchStepX = Math.Max(1, (maxBx - minBx) / 40);
            int searchStepY = Math.Max(1, (maxBy - minBy) / 40);

            foreach (var (ax, ay) in sampleCenters)
            {
                ComputePatchStats(grayA, w, h, ax, ay, halfPatch, out float meanA, out float varA);
                if (varA < 0.0005f) continue;

                float bestNcc = -1f;
                int bestBx = -1, bestBy = -1;

                for (int by = minBy; by <= maxBy; by += searchStepY)
                {
                    for (int bx = minBx; bx <= maxBx; bx += searchStepX)
                    {
                        float ncc = ComputeNormalizedCrossCorrelation(grayA, grayB, w, h, ax, ay, bx, by, halfPatch, meanA, varA);
                        if (ncc > bestNcc)
                        {
                            bestNcc = ncc;
                            bestBx = bx;
                            bestBy = by;
                        }
                    }
                }

                // Subpixel / local refinement
                if (bestNcc > 0.30f && bestBx >= 0 && bestBy >= 0)
                {
                    int rX = Math.Max(searchStepX * 2, 2);
                    int rY = Math.Max(searchStepY * 2, 2);
                    for (int ry = Math.Max(0, bestBy - rY); ry <= Math.Min(h - 1, bestBy + rY); ry++)
                    {
                        for (int rx = Math.Max(0, bestBx - rX); rx <= Math.Min(w - 1, bestBx + rX); rx++)
                        {
                            float ncc = ComputeNormalizedCrossCorrelation(grayA, grayB, w, h, ax, ay, rx, ry, halfPatch, meanA, varA);
                            if (ncc > bestNcc)
                            {
                                bestNcc = ncc;
                                bestBx = rx;
                                bestBy = ry;
                            }
                        }
                    }

                    if (bestNcc > 0.45f)
                    {
                        matchedPairs.Add((ax, ay, bestBx, bestBy, bestNcc));
                    }
                }
            }

            if (matchedPairs.Count < 2) continue;

            var dxList = matchedPairs.Select(p => p.dstX - p.srcX).OrderBy(d => d).ToList();
            var dyList = matchedPairs.Select(p => p.dstY - p.srcY).OrderBy(d => d).ToList();

            float medianDx = dxList[dxList.Count / 2];
            float medianDy = dyList[dyList.Count / 2];

            float inlierThreshold = 16f;
            var inliers = matchedPairs
                .Where(p => MathF.Abs((p.dstX - p.srcX) - medianDx) <= inlierThreshold &&
                            MathF.Abs((p.dstY - p.srcY) - medianDy) <= inlierThreshold)
                .ToList();

            if (inliers.Count < 2) continue;

            float avgScore = inliers.Average(p => p.score);
            double conf = (double)inliers.Count / Math.Max(1, sampleCenters.Count);
            double quality = conf * (avgScore * avgScore);

            if (avgScore > 0.40f && quality > bestQuality)
            {
                bestQuality = quality;
                bestConf = conf;
                if (inliers.Count >= 4)
                {
                    var pointList = inliers.Select(p => (p.srcX, p.srcY, p.dstX, p.dstY)).ToList();
                    float[] homography = _homographyEstimator.EstimateHomography(pointList);

                    float det = homography[0] * (homography[4] * homography[8] - homography[5] * homography[7]) -
                                homography[1] * (homography[3] * homography[8] - homography[5] * homography[6]) +
                                homography[2] * (homography[3] * homography[7] - homography[4] * homography[6]);

                    if (float.IsNaN(det) || MathF.Abs(det) < 0.1f || MathF.Abs(det) > 10.0f)
                    {
                        homography = new float[9] { 1, 0, medianDx, 0, 1, medianDy, 0, 0, 1 };
                    }
                    bestH = homography;
                }
                else
                {
                    bestH = new float[9] { 1, 0, medianDx, 0, 1, medianDy, 0, 0, 1 };
                }
            }
        }

        return (bestH, bestConf);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ComputePatchStats(
        float* gray, int w, int h, int cx, int cy, int halfPatch,
        out float mean, out float variance)
    {
        float sum = 0f;
        float sumSq = 0f;
        int count = 0;

        for (int dy = -halfPatch; dy <= halfPatch; dy += 2)
        {
            int y = Math.Clamp(cy + dy, 0, h - 1);
            int rowOffset = y * w;
            for (int dx = -halfPatch; dx <= halfPatch; dx += 2)
            {
                int x = Math.Clamp(cx + dx, 0, w - 1);
                float val = gray[rowOffset + x];
                sum += val;
                sumSq += val * val;
                count++;
            }
        }

        mean = sum / count;
        variance = MathF.Max(0f, (sumSq / count) - (mean * mean));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe float ComputeNormalizedCrossCorrelation(
        float* grayA, float* grayB, int w, int h,
        int ax, int ay, int bx, int by,
        int halfPatch, float meanA, float varA)
    {
        float sumB = 0f;
        float sumSqB = 0f;
        float crossSum = 0f;
        int count = 0;

        for (int dy = -halfPatch; dy <= halfPatch; dy += 2)
        {
            int ya = Math.Clamp(ay + dy, 0, h - 1);
            int yb = Math.Clamp(by + dy, 0, h - 1);
            int rowA = ya * w;
            int rowB = yb * w;

            for (int dx = -halfPatch; dx <= halfPatch; dx += 2)
            {
                int xa = Math.Clamp(ax + dx, 0, w - 1);
                int xb = Math.Clamp(bx + dx, 0, w - 1);
                float va = grayA[rowA + xa];
                float vb = grayB[rowB + xb];

                sumB += vb;
                sumSqB += vb * vb;
                crossSum += (va - meanA) * vb;
                count++;
            }
        }

        float meanB = sumB / count;
        float varB = MathF.Max(0f, (sumSqB / count) - (meanB * meanB));

        float denom = MathF.Sqrt(varA * varB) * count;
        if (denom < 1e-6f) return -1f;

        float ncc = crossSum / denom;
        return Math.Clamp(ncc, -1f, 1f);
    }

    public static float[] MultiplyHomographies(float[] a, float[] b)
    {
        float[] c = new float[9];
        for (int r = 0; r < 3; r++)
        {
            for (int col = 0; col < 3; col++)
            {
                c[r * 3 + col] =
                    a[r * 3 + 0] * b[0 * 3 + col] +
                    a[r * 3 + 1] * b[1 * 3 + col] +
                    a[r * 3 + 2] * b[2 * 3 + col];
            }
        }
        return c;
    }
}
