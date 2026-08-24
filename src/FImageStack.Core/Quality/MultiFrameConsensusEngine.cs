using System.Runtime.CompilerServices;
using FImageStack.Core.FocusVolume;

namespace FImageStack.Core.Quality;

public interface IMultiFrameConsensusEngine
{
    void ApplyConsensusFilter(FocusVolume.FocusVolume volume, float spikeTolerance = 1.8f);
    float ComputeConsensusScore(ReadOnlySpan<float> profile, int frameIndex, float spikeTolerance = 1.8f);
}

public sealed class MultiFrameConsensusEngine : IMultiFrameConsensusEngine
{
    public unsafe void ApplyConsensusFilter(FocusVolume.FocusVolume volume, float spikeTolerance = 1.8f)
    {
        int width = volume.Width;
        int height = volume.Height;
        int slices = volume.Slices;

        // Consensus filter requires at least 3 frames
        if (slices < 3) return;

        Parallel.For(0, height, y =>
        {
            Span<float> profile = stackalloc float[slices];

            for (int x = 0; x < width; x++)
            {
                volume.CopyProfile(x, y, profile);
                bool modified = false;

                for (int z = 1; z < slices - 1; z++)
                {
                    float curr = profile[z];
                    float prev = profile[z - 1];
                    float next = profile[z + 1];
                    float neighborMean = (prev + next) * 0.5f;

                    // If current frame sharpness is an anomalous spike far above its neighbors
                    if (curr > 0.05f && curr > neighborMean * spikeTolerance)
                    {
                        float consensusScore = (2f * neighborMean) / (curr + neighborMean + 1e-5f);
                        // Attenuate spike to blend naturally with optical defocus continuity
                        profile[z] = curr * Math.Clamp(consensusScore, 0.1f, 1.0f);
                        modified = true;
                    }
                }

                if (modified)
                {
                    for (int z = 0; z < slices; z++)
                    {
                        volume.At(x, y, z) = profile[z];
                    }
                }
            }
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ComputeConsensusScore(ReadOnlySpan<float> profile, int frameIndex, float spikeTolerance = 1.8f)
    {
        int count = profile.Length;
        if (count < 3 || frameIndex < 0 || frameIndex >= count)
            return 1.0f;

        float curr = profile[frameIndex];
        if (curr < 0.02f) return 1.0f;

        float neighborMean;
        if (frameIndex == 0)
        {
            neighborMean = profile[1];
        }
        else if (frameIndex == count - 1)
        {
            neighborMean = profile[count - 2];
        }
        else
        {
            neighborMean = (profile[frameIndex - 1] + profile[frameIndex + 1]) * 0.5f;
        }

        if (curr > neighborMean * spikeTolerance)
        {
            return Math.Clamp((2f * neighborMean) / (curr + neighborMean + 1e-5f), 0.05f, 1.0f);
        }

        return 1.0f;
    }
}
