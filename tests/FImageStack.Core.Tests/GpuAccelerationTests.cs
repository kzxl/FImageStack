using FImageStack.Core;
using FImageStack.Core.Acceleration;
using FImageStack.Core.Models;
using Xunit;

namespace FImageStack.Core.Tests;

public class GpuAccelerationTests
{
    [Fact]
    public void StandardGpuAccelerationEngine_ShouldDiscoverHardwareBackends()
    {
        var engine = new StandardGpuAccelerationEngine();
        var devices = engine.GetAvailableDevices();

        Assert.NotEmpty(devices);
        Assert.Contains(devices, d => d.Backend == GpuBackendType.DirectCompute);
        Assert.Contains(devices, d => d.Backend == GpuBackendType.DirectML);
        Assert.Contains(devices, d => d.Backend == GpuBackendType.CpuSimd);

        var current = engine.GetCurrentDevice();
        Assert.NotNull(current);
    }

    [Fact]
    public void StandardGpuAccelerationEngine_ShouldSwitchBackendsSeamlessly()
    {
        var engine = new StandardGpuAccelerationEngine();

        engine.SetActiveBackend(GpuBackendType.CpuSimd);
        Assert.Equal(GpuBackendType.CpuSimd, engine.GetCurrentDevice().Backend);

        engine.SetActiveBackend(GpuBackendType.DirectCompute);
        Assert.Equal(GpuBackendType.DirectCompute, engine.GetCurrentDevice().Backend);
    }

    [Fact]
    public void StandardGpuAccelerationEngine_ShouldComputeFocusMeasureAndToneMapping()
    {
        int size = 16;
        var grayBuffer = new ImageBuffer<float>(size, size, 1);
        grayBuffer.AsSpan().Fill(0.5f);
        // Create an edge step
        for (int y = 0; y < size; y++)
        {
            for (int x = 8; x < size; x++)
            {
                grayBuffer.At(x, y) = 1.0f;
            }
        }

        var engine = new StandardGpuAccelerationEngine();

        // 1. Compute Focus Measure
        using var focusMap = engine.ComputeFocusMeasureGpu(grayBuffer, FocusMeasureMethod.ModifiedLaplacian);
        Assert.NotNull(focusMap);
        Assert.Equal(size, focusMap.Width);
        Assert.Equal(size, focusMap.Height);

        // Edge at x=8 should have strong Laplacian sharpness
        Assert.True(focusMap.At(8, 8) > 0f);

        // 2. GPU Downsample & Upsample
        using var down = engine.DownsamplePyramidGpu(grayBuffer);
        Assert.Equal(8, down.Width);
        Assert.Equal(8, down.Height);

        using var up = engine.UpsamplePyramidGpu(down, size, size);
        Assert.Equal(size, up.Width);
        Assert.Equal(size, up.Height);

        // 3. GPU Tone Mapping
        var hdrBuffer = new ImageBuffer<float>(size, size, 3);
        hdrBuffer.AsSpan().Fill(2.5f); // Over-range HDR value
        using var ldr = engine.ApplyToneMappingGpu(hdrBuffer, ToneMappingOperator.ACESFilmic);

        Assert.NotNull(ldr);
        Assert.True(ldr.At(8, 8, 0) <= 1.0f);

        grayBuffer.Dispose();
        hdrBuffer.Dispose();
    }
}
