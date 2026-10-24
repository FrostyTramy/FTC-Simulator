using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sim.Field;

/// <summary>layout.json written by tools/cad_pipeline/run.py (one per generated CAD source).</summary>
public sealed class FieldLayout
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("up_axis")] public string UpAxis { get; set; } = "Y";
    [JsonPropertyName("groups")] public List<LayoutGroup> Groups { get; set; } = new();

    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
}

public sealed class LayoutGroup
{
    /// <summary>static: merged static geometry; rigid: one moving body; element: one body per instance.</summary>
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "static";
    [JsonPropertyName("mesh")] public string? Mesh { get; set; }
    [JsonPropertyName("colliders")] public string? Colliders { get; set; }
    [JsonPropertyName("surfaces")] public int? Surfaces { get; set; }
    [JsonPropertyName("instances")] public List<LayoutInstance>? Instances { get; set; }
}

public sealed class LayoutInstance
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    /// <summary>Row-major 4x4 pose, metres.</summary>
    [JsonPropertyName("transform")] public double[] Transform { get; set; } = System.Array.Empty<double>();
}

/// <summary>One entry of a &lt;group&gt;.colliders.json file.</summary>
public sealed class ColliderSpec
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("transform")] public double[]? Transform { get; set; }
    [JsonPropertyName("size")] public double[]? Size { get; set; }
    [JsonPropertyName("center")] public double[]? Center { get; set; }
    [JsonPropertyName("radius")] public double? Radius { get; set; }
    [JsonPropertyName("height")] public double? Height { get; set; }
    [JsonPropertyName("points")] public double[][]? Points { get; set; }
}
