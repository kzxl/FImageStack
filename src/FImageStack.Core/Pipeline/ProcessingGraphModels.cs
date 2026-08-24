using FImageStack.Core.Models;

namespace FImageStack.Core.Pipeline;

public enum NodeState
{
    Clean,
    Dirty,
    Executing,
    Failed
}

public enum ProcessingNodeType
{
    RawFrame,
    LensCorrection,
    Alignment,
    FocusMeasure,
    DepthMap,
    Fusion,
    ArtifactRepair,
    Output
}

public sealed class ProcessingNode
{
    public string Id { get; set; } = string.Empty;
    public ProcessingNodeType Type { get; set; }
    public int FrameIndex { get; set; } = -1;
    public NodeState State { get; set; } = NodeState.Dirty;
    public bool IsEnabled { get; set; } = true;
    public List<string> InputNodeIds { get; } = new();
    public List<string> OutputNodeIds { get; } = new();
    public object? CachedOutput { get; set; }
    public TimeSpan LastExecutionTime { get; set; }
}

public sealed class GraphExecutionResult
{
    public int TotalNodes { get; set; }
    public int ExecutedNodesCount { get; set; }
    public int CachedReusedNodesCount { get; set; }
    public TimeSpan TotalExecutionTime { get; set; }
    public ImageBuffer<float>? OutputImage { get; set; }
    public string Summary { get; set; } = string.Empty;
}
