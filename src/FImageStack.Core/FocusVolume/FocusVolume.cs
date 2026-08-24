using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FImageStack.Core.Models;

namespace FImageStack.Core.FocusVolume;

/// <summary>
/// High-performance 3D Focus Volume Tensor (Width x Height x Slices)
/// with Z-contiguous memory layout for ultra-fast focus profile extraction and sub-frame fitting.
/// </summary>
public unsafe sealed class FocusVolume : IDisposable
{
    private float* _pointer;
    private bool _disposed;

    public int Width { get; }
    public int Height { get; }
    public int Slices { get; }
    public int TotalVoxels => Width * Height * Slices;
    public long ByteSize => (long)TotalVoxels * sizeof(float);

    public FocusVolume(int width, int height, int slices)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (slices <= 0) throw new ArgumentOutOfRangeException(nameof(slices));

        Width = width;
        Height = height;
        Slices = slices;

        nuint byteCount = (nuint)TotalVoxels * (nuint)sizeof(float);
        _pointer = (float*)NativeMemory.AllocZeroed(byteCount);
    }

    public float* DataPointer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ThrowIfDisposed();
            return _pointer;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref float At(int x, int y, int z)
    {
        ThrowIfDisposed();
        return ref _pointer[(y * Width + x) * Slices + z];
    }

    /// <summary>
    /// Returns a Span containing the sharpness profile across all frames for a given pixel (x, y).
    /// Contiguous in memory (Zero-Copy).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<float> GetProfile(int x, int y)
    {
        ThrowIfDisposed();
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            throw new ArgumentOutOfRangeException($"Coordinates ({x}, {y}) out of bounds ({Width}x{Height}).");

        return new ReadOnlySpan<float>(_pointer + (y * Width + x) * Slices, Slices);
    }

    /// <summary>
    /// Copies the focus curve of pixel (x, y) into a destination array or span.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CopyProfile(int x, int y, Span<float> destination)
    {
        var profile = GetProfile(x, y);
        profile.CopyTo(destination);
    }

    /// <summary>
    /// Ingests a 2D focus map slice into the 3D volume at slice index z.
    /// </summary>
    public void SetSlice(int z, ImageBuffer<float> focusMap)
    {
        ThrowIfDisposed();
        if (z < 0 || z >= Slices) throw new ArgumentOutOfRangeException(nameof(z));
        if (focusMap.Width != Width || focusMap.Height != Height)
            throw new ArgumentException("Focus map dimensions do not match volume dimensions.");

        float* src = focusMap.DataPointer;
        int w = Width;
        int h = Height;
        int s = Slices;

        Parallel.For(0, h, y =>
        {
            int rowOffset = y * w;
            for (int x = 0; x < w; x++)
            {
                _pointer[(rowOffset + x) * s + z] = src[rowOffset + x];
            }
        });
    }

    /// <summary>
    /// Extracts a 2D slice for frame z into an output ImageBuffer.
    /// </summary>
    public void ExtractSlice(int z, ImageBuffer<float> outputBuffer)
    {
        ThrowIfDisposed();
        if (z < 0 || z >= Slices) throw new ArgumentOutOfRangeException(nameof(z));
        if (outputBuffer.Width != Width || outputBuffer.Height != Height)
            throw new ArgumentException("Output buffer dimensions do not match volume dimensions.");

        float* dst = outputBuffer.DataPointer;
        int w = Width;
        int h = Height;
        int s = Slices;

        Parallel.For(0, h, y =>
        {
            int rowOffset = y * w;
            for (int x = 0; x < w; x++)
            {
                dst[rowOffset + x] = _pointer[(rowOffset + x) * s + z];
            }
        });
    }

    public FocusVolume Clone()
    {
        ThrowIfDisposed();
        var copy = new FocusVolume(Width, Height, Slices);
        Buffer.MemoryCopy(_pointer, copy._pointer, copy.ByteSize, ByteSize);
        return copy;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FocusVolume));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_pointer != null)
            {
                NativeMemory.Free(_pointer);
                _pointer = null;
            }
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    ~FocusVolume()
    {
        Dispose();
    }
}
