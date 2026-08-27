using System.Numerics;
using FImageStack.Core.Models;

namespace FImageStack.Core.Depth3D;

public sealed class DepthMeshOptions
{
    /// <summary>
    /// Z-Axis geometric scale / depth extrusion factor (in mm or world units).
    /// </summary>
    public float ZScale { get; set; } = 100.0f;

    /// <summary>
    /// Subsampling step across X/Y grid (1 = 100% full pixel resolution, 2 = 50%, 4 = 25%).
    /// </summary>
    public int DecimationStep { get; set; } = 1;

    /// <summary>
    /// Min depth cutoff [0.0 - 1.0].
    /// </summary>
    public float DepthMinCutoff { get; set; } = 0.0f;

    /// <summary>
    /// Max depth cutoff [0.0 - 1.0].
    /// </summary>
    public float DepthMaxCutoff { get; set; } = 1.0f;

    /// <summary>
    /// Invert Z coordinates (near = high Z or far = high Z).
    /// </summary>
    public bool InvertZ { get; set; } = false;

    /// <summary>
    /// Calculate smooth vertex surface normals.
    /// </summary>
    public bool ComputeNormals { get; set; } = true;

    /// <summary>
    /// Export format (PLY point cloud, OBJ surface mesh, Normal Map).
    /// </summary>
    public MeshExportFormat Format { get; set; } = MeshExportFormat.PlyPointCloud;
}

public sealed class Mesh3DData
{
    public List<Vector3> Vertices { get; } = new();
    public List<Vector3> Normals { get; } = new();
    public List<Vector3> Colors { get; } = new();
    public List<Vector2> Uvs { get; } = new();
    public List<int> Indices { get; } = new();
}
