using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace Sim.Core.Config;

/// <summary>
/// Minimal YAML → plain object tree (string scalars, null, List of object?, Dictionary of string to object?)
/// plus typed accessors that throw a <see cref="ConfigException"/> naming the path on bad data.
/// </summary>
public static class YamlObjects
{
    public static object? Load(string path)
    {
        var stream = new YamlStream();
        using (var reader = new StreamReader(path))
        {
            stream.Load(reader);
        }
        if (stream.Documents.Count != 1)
        {
            throw new ConfigException($"{path}: expected one YAML document");
        }
        return ToObject(stream.Documents[0].RootNode);
    }

    public static object? ToObject(YamlNode node) => node switch
    {
        YamlScalarNode s when s.Style == YamlDotNet.Core.ScalarStyle.Plain && (s.Value is null || s.Value is "null" or "~" or "") => null,
        YamlScalarNode s => s.Value,
        YamlSequenceNode seq => seq.Children.Select(ToObject).ToList(),
        YamlMappingNode map => map.Children.ToDictionary(
            kv => (kv.Key as YamlScalarNode)?.Value ?? throw new ConfigException($"non-scalar key at {kv.Key.Start}"),
            kv => ToObject(kv.Value),
            StringComparer.Ordinal),
        _ => throw new ConfigException($"unsupported YAML node at {node.Start}"),
    };

    public static IReadOnlyDictionary<string, object?> Map(object? node, string path) =>
        node as IReadOnlyDictionary<string, object?> ?? throw new ConfigException($"{path}: expected a map");

    public static IReadOnlyList<object?> List(object? node, string path) =>
        node as IReadOnlyList<object?> ?? throw new ConfigException($"{path}: expected a list");

    public static object? Child(object? node, string key, string path)
    {
        var map = Map(node, path);
        return map.TryGetValue(key, out var v) ? v : throw new ConfigException($"{path}.{key}: missing");
    }

    public static string String(object? node, string path) =>
        node as string ?? throw new ConfigException($"{path}: expected a scalar");

    public static double Double(object? node, string path) =>
        node is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d
            : throw new ConfigException($"{path}: '{node}' is not a number");

    public static double[] Doubles(object? node, string path) =>
        List(node, path).Select((v, i) => Double(v, $"{path}[{i}]")).ToArray();

    /// <summary>The "value" of a {value, unit, source} quantity map, checking the unit when given.</summary>
    public static object? QuantityValue(object? node, string path, string? expectedUnit = null)
    {
        var map = Map(node, path);
        if (expectedUnit is not null && (!map.TryGetValue("unit", out var unit) || unit as string != expectedUnit))
        {
            throw new ConfigException($"{path}: expected unit '{expectedUnit}'");
        }
        if (!map.ContainsKey("source"))
        {
            throw new ConfigException($"{path}: value without a source");
        }
        return map.TryGetValue("value", out var v) ? v : throw new ConfigException($"{path}: missing value");
    }
}
