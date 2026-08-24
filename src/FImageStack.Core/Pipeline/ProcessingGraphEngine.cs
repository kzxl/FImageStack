using System.Diagnostics;
using FImageStack.Core.FocusMeasure;
using FImageStack.Core.Models;
using CoreStackFrame = FImageStack.Core.Models.StackFrame;

namespace FImageStack.Core.Pipeline;

public interface IProcessingGraphEngine
{
    void BuildGraph(IReadOnlyList<CoreStackFrame> initialFrames);
    void SetFrameEnabled(int frameIndex, bool enabled);
    void InvalidateNode(string nodeId);
    GraphExecutionResult Execute(bool forceRecomputeAll = false);
    IReadOnlyDictionary<string, ProcessingNode> Nodes { get; }
}

public sealed class ProcessingGraphEngine : IProcessingGraphEngine
{
    private readonly Dictionary<string, ProcessingNode> _nodes = new();
    private readonly List<CoreStackFrame> _frames = new();
    private readonly IFocusMeasureEngine _focusMeasure = new ModifiedLaplacianFocusMeasure();

    public IReadOnlyDictionary<string, ProcessingNode> Nodes => _nodes;

    public void BuildGraph(IReadOnlyList<CoreStackFrame> initialFrames)
    {
        if (initialFrames == null || initialFrames.Count == 0)
            throw new ArgumentException("Initial frames cannot be empty.", nameof(initialFrames));

        _nodes.Clear();
        _frames.Clear();
        _frames.AddRange(initialFrames);

        int frameCount = _frames.Count;

        // 1. Create per-frame processing pipeline nodes
        for (int i = 0; i < frameCount; i++)
        {
            string rawId = $"raw_{i}";
            string lensId = $"lens_{i}";
            string alignId = $"align_{i}";
            string focusId = $"focus_{i}";

            var rawNode = new ProcessingNode
            {
                Id = rawId,
                Type = ProcessingNodeType.RawFrame,
                FrameIndex = i,
                State = NodeState.Dirty
            };
            rawNode.OutputNodeIds.Add(lensId);

            var lensNode = new ProcessingNode
            {
                Id = lensId,
                Type = ProcessingNodeType.LensCorrection,
                FrameIndex = i,
                State = NodeState.Dirty
            };
            lensNode.InputNodeIds.Add(rawId);
            lensNode.OutputNodeIds.Add(alignId);

            var alignNode = new ProcessingNode
            {
                Id = alignId,
                Type = ProcessingNodeType.Alignment,
                FrameIndex = i,
                State = NodeState.Dirty
            };
            alignNode.InputNodeIds.Add(lensId);
            alignNode.OutputNodeIds.Add(focusId);
            alignNode.OutputNodeIds.Add("fusion");

            var focusNode = new ProcessingNode
            {
                Id = focusId,
                Type = ProcessingNodeType.FocusMeasure,
                FrameIndex = i,
                State = NodeState.Dirty
            };
            focusNode.InputNodeIds.Add(alignId);
            focusNode.OutputNodeIds.Add("depth");

            _nodes[rawId] = rawNode;
            _nodes[lensId] = lensNode;
            _nodes[alignId] = alignNode;
            _nodes[focusId] = focusNode;
        }

        // 2. Create Global Aggregator Nodes
        var depthNode = new ProcessingNode
        {
            Id = "depth",
            Type = ProcessingNodeType.DepthMap,
            State = NodeState.Dirty
        };
        for (int i = 0; i < frameCount; i++) depthNode.InputNodeIds.Add($"focus_{i}");
        depthNode.OutputNodeIds.Add("fusion");

        var fusionNode = new ProcessingNode
        {
            Id = "fusion",
            Type = ProcessingNodeType.Fusion,
            State = NodeState.Dirty
        };
        fusionNode.InputNodeIds.Add("depth");
        for (int i = 0; i < frameCount; i++) fusionNode.InputNodeIds.Add($"align_{i}");
        fusionNode.OutputNodeIds.Add("repair");

        var repairNode = new ProcessingNode
        {
            Id = "repair",
            Type = ProcessingNodeType.ArtifactRepair,
            State = NodeState.Dirty
        };
        repairNode.InputNodeIds.Add("fusion");
        repairNode.OutputNodeIds.Add("output");

        var outputNode = new ProcessingNode
        {
            Id = "output",
            Type = ProcessingNodeType.Output,
            State = NodeState.Dirty
        };
        outputNode.InputNodeIds.Add("repair");

        _nodes["depth"] = depthNode;
        _nodes["fusion"] = fusionNode;
        _nodes["repair"] = repairNode;
        _nodes["output"] = outputNode;
    }

    public void SetFrameEnabled(int frameIndex, bool enabled)
    {
        string[] frameNodeIds = { $"raw_{frameIndex}", $"lens_{frameIndex}", $"align_{frameIndex}", $"focus_{frameIndex}" };
        foreach (var id in frameNodeIds)
        {
            if (_nodes.TryGetValue(id, out var node))
            {
                node.IsEnabled = enabled;
            }
        }

        // Downstream aggregation nodes are invalidated
        InvalidateNode("depth");
    }

    public void InvalidateNode(string nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var rootNode)) return;

        var queue = new Queue<ProcessingNode>();
        queue.Enqueue(rootNode);

        while (queue.Count > 0)
        {
            var curr = queue.Dequeue();
            curr.State = NodeState.Dirty;

            foreach (var outId in curr.OutputNodeIds)
            {
                if (_nodes.TryGetValue(outId, out var child) && child.State != NodeState.Dirty)
                {
                    queue.Enqueue(child);
                }
            }
        }
    }

    public unsafe GraphExecutionResult Execute(bool forceRecomputeAll = false)
    {
        var sw = Stopwatch.StartNew();
        int executedCount = 0;
        int cachedCount = 0;

        if (forceRecomputeAll)
        {
            foreach (var n in _nodes.Values) n.State = NodeState.Dirty;
        }

        int frameCount = _frames.Count;

        // 1. Execute per-frame nodes
        for (int i = 0; i < frameCount; i++)
        {
            var rawNode = _nodes[$"raw_{i}"];
            if (!rawNode.IsEnabled) continue;

            // Raw
            if (rawNode.State == NodeState.Dirty)
            {
                rawNode.CachedOutput = _frames[i].GrayBuffer;
                rawNode.State = NodeState.Clean;
                executedCount++;
            }
            else cachedCount++;

            // Lens
            var lensNode = _nodes[$"lens_{i}"];
            if (lensNode.State == NodeState.Dirty)
            {
                lensNode.CachedOutput = rawNode.CachedOutput;
                lensNode.State = NodeState.Clean;
                executedCount++;
            }
            else cachedCount++;

            // Align
            var alignNode = _nodes[$"align_{i}"];
            if (alignNode.State == NodeState.Dirty)
            {
                alignNode.CachedOutput = lensNode.CachedOutput;
                alignNode.State = NodeState.Clean;
                executedCount++;
            }
            else cachedCount++;

            // Focus
            var focusNode = _nodes[$"focus_{i}"];
            if (focusNode.State == NodeState.Dirty)
            {
                var grayBuf = (ImageBuffer<float>)alignNode.CachedOutput!;
                if (_frames[i].FocusMap == null)
                {
                    _frames[i].FocusMap = new ImageBuffer<float>(grayBuf.Width, grayBuf.Height);
                    _focusMeasure.ComputeFocusMap(grayBuf, _frames[i].FocusMap);
                }
                focusNode.CachedOutput = _frames[i].FocusMap;
                focusNode.State = NodeState.Clean;
                executedCount++;
            }
            else cachedCount++;
        }

        // 2. Execute Global Aggregators
        var depthNode = _nodes["depth"];
        if (depthNode.State == NodeState.Dirty)
        {
            // Synthesize virtual depth map from active focus nodes
            var activeFocusMaps = _nodes.Values
                .Where(n => n.Type == ProcessingNodeType.FocusMeasure && n.IsEnabled && n.CachedOutput != null)
                .Select(n => (ImageBuffer<float>)n.CachedOutput!)
                .ToList();

            depthNode.CachedOutput = activeFocusMaps.Count > 0 ? activeFocusMaps[0] : null;
            depthNode.State = NodeState.Clean;
            executedCount++;
        }
        else cachedCount++;

        var fusionNode = _nodes["fusion"];
        if (fusionNode.State == NodeState.Dirty)
        {
            var activeAlignNodes = _nodes.Values
                .Where(n => n.Type == ProcessingNodeType.Alignment && n.IsEnabled && n.CachedOutput != null)
                .Select(n => (ImageBuffer<float>)n.CachedOutput!)
                .ToList();

            ImageBuffer<float> fused;
            if (activeAlignNodes.Count > 0)
            {
                int w = activeAlignNodes[0].Width;
                int h = activeAlignNodes[0].Height;
                fused = new ImageBuffer<float>(w, h);
                float* dstPtr = fused.DataPointer;
                int total = w * h;

                for (int p = 0; p < total; p++)
                {
                    float sum = 0f;
                    for (int k = 0; k < activeAlignNodes.Count; k++)
                    {
                        sum += activeAlignNodes[k].DataPointer[p];
                    }
                    dstPtr[p] = sum / activeAlignNodes.Count;
                }
            }
            else
            {
                fused = new ImageBuffer<float>(16, 16);
            }

            fusionNode.CachedOutput = fused;
            fusionNode.State = NodeState.Clean;
            executedCount++;
        }
        else cachedCount++;

        var repairNode = _nodes["repair"];
        if (repairNode.State == NodeState.Dirty)
        {
            repairNode.CachedOutput = fusionNode.CachedOutput;
            repairNode.State = NodeState.Clean;
            executedCount++;
        }
        else cachedCount++;

        var outputNode = _nodes["output"];
        if (outputNode.State == NodeState.Dirty)
        {
            outputNode.CachedOutput = repairNode.CachedOutput;
            outputNode.State = NodeState.Clean;
            executedCount++;
        }
        else cachedCount++;

        sw.Stop();

        return new GraphExecutionResult
        {
            TotalNodes = _nodes.Count,
            ExecutedNodesCount = executedCount,
            CachedReusedNodesCount = cachedCount,
            TotalExecutionTime = sw.Elapsed,
            OutputImage = (ImageBuffer<float>?)outputNode.CachedOutput,
            Summary = $"Graph executed in {sw.ElapsedMilliseconds}ms. Executed: {executedCount} nodes | Reused from Cache: {cachedCount} nodes."
        };
    }
}
