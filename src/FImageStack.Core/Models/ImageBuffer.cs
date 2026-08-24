using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FImageStack.Core.Models;

/// <summary>
/// High-performance 2D/3D image buffer backed by unmanaged memory (NativeMemory)
/// to avoid Garbage Collection pauses and fragmentation on large image stacks.
/// </summary>
public unsafe sealed class ImageBuffer<T> : IDisposable where T : unmanaged
{
    private T* _pointer;
    private bool _disposed;

    public int Width { get; }
    public int Height { get; }
    public int Channels { get; }
    public int Stride { get; }
    public int TotalElements => Width * Height * Channels;
    public long ByteSize => (long)TotalElements * sizeof(T);
    public PixelFormatType Format { get; }

    public ImageBuffer(int width, int height, int channels = 1, PixelFormatType format = PixelFormatType.GrayFloat32)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));

        Width = width;
        Height = height;
        Channels = channels;
        Format = format;
        Stride = width * channels;

        nuint byteCount = (nuint)TotalElements * (nuint)sizeof(T);
        _pointer = (T*)NativeMemory.AllocZeroed(byteCount);
    }

    public T* DataPointer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ThrowIfDisposed();
            return _pointer;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> AsSpan()
    {
        ThrowIfDisposed();
        return new Span<T>(_pointer, TotalElements);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetRowSpan(int y)
    {
        ThrowIfDisposed();
        if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y));
        return new Span<T>(_pointer + y * Stride, Stride);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T At(int x, int y, int c = 0)
    {
        ThrowIfDisposed();
        return ref _pointer[(y * Width + x) * Channels + c];
    }

    public ImageBuffer<T> Clone()
    {
        ThrowIfDisposed();
        var copy = new ImageBuffer<T>(Width, Height, Channels, Format);
        Buffer.MemoryCopy(_pointer, copy._pointer, copy.ByteSize, ByteSize);
        return copy;
    }

    public void CopyTo(ImageBuffer<T> destination)
    {
        ThrowIfDisposed();
        destination.ThrowIfDisposed();
        if (destination.Width != Width || destination.Height != Height || destination.Channels != Channels)
            throw new ArgumentException("Destination dimensions do not match source.");

        Buffer.MemoryCopy(_pointer, destination._pointer, destination.ByteSize, ByteSize);
    }

    public void Clear()
    {
        ThrowIfDisposed();
        NativeMemory.Clear(_pointer, (nuint)ByteSize);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ImageBuffer<T>));
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
        }
        GC.SuppressFinalize(this);
    }

    ~ImageBuffer()
    {
        Dispose();
    }
}
