using YamlDotNet.RepresentationModel;

namespace Sim.Core.Config;

/// <summary>Foul definition from the rules config (fouls list): rule id, base penalty, sim handling.</summary>
public sealed record FoulConfig(
    string Rule,
    string Penalty,
    string? Strategic,
    string Sim,
    string Source,
    IReadOnlyDictionary<string, Quantity> Parameters);

/// <summary>
/// Loader for config/rules_biobuzz.yaml. Every number is a {value, unit, source} quantity
/// (see config/schemas/rules_biobuzz.schema.json); accessors keep the source for UI and foul records.
/// </summary>
public sealed class RulesConfig
{
    private readonly Dictionary<string, Quantity> _quantities;

    private RulesConfig(string path, string manualVersion, IReadOnlyList<FoulConfig> fouls, Dictionary<string, Quantity> quantities)
    {
        FilePath = path;
        ManualVersion = manualVersion;
        Fouls = fouls;
        _quantities = quantities;
    }

    public string FilePath { get; }
    public string ManualVersion { get; }
    public IReadOnlyList<FoulConfig> Fouls { get; }

    /// <summary>All quantities, sorted by path for deterministic iteration.</summary>
    public IEnumerable<Quantity> All => _quantities.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value);

    public Quantity Get(string dottedPath) =>
        _quantities.TryGetValue(dottedPath, out var q) ? q : throw new ConfigException($"missing rules value '{dottedPath}'");

    public bool TryGet(string dottedPath, out Quantity? quantity) => _quantities.TryGetValue(dottedPath, out quantity);

    public FoulConfig Foul(string rule) =>
        Fouls.FirstOrDefault(f => f.Rule == rule) ?? throw new ConfigException($"rule {rule} not in the fouls list");

    /// <summary>Source reference for a rule id (e.g. "G410 p109") or a dotted quantity path.</summary>
    public string SourceOf(string ruleOrPath)
    {
        var foul = Fouls.FirstOrDefault(f => f.Rule == ruleOrPath);
        return foul is not null ? foul.Source : Get(ruleOrPath).Source;
    }

    public static RulesConfig Load(string path)
    {
        var stream = new YamlStream();
        using (var reader = new StreamReader(path))
        {
            stream.Load(reader);
        }
        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new ConfigException($"{path}: expected one YAML mapping document");
        }

        var quantities = new Dictionary<string, Quantity>(StringComparer.Ordinal);
        var fouls = new List<FoulConfig>();
        var manualVersion = "";
        foreach (var (keyNode, valueNode) in root.Children)
        {
            var key = Scalar(keyNode);
            if (key == "manual")
            {
                if (ToObject(valueNode) is IReadOnlyDictionary<string, object?> m && m.TryGetValue("version", out var v))
                {
                    manualVersion = v as string ?? "";
                }
            }
            else if (key == "fouls")
            {
                var seq = valueNode as YamlSequenceNode ?? throw new ConfigException($"{path}: fouls must be a list");
                for (var i = 0; i < seq.Children.Count; i++)
                {
                    var node = seq.Children[i] as YamlMappingNode ?? throw new ConfigException($"{path}: fouls[{i}] must be a map");
                    fouls.Add(ParseFoul(node, $"fouls[{i}]"));
                }
            }
            else
            {
                CollectQuantities(valueNode, key, quantities);
            }
        }
        return new RulesConfig(path, manualVersion, fouls, quantities);
    }

    private static FoulConfig ParseFoul(YamlMappingNode node, string path)
    {
        string? rule = null, penalty = null, strategic = null, sim = null, source = null;
        var parameters = new Dictionary<string, Quantity>(StringComparer.Ordinal);
        foreach (var (k, v) in node.Children)
        {
            var key = Scalar(k);
            switch (key)
            {
                case "rule": rule = Scalar(v); break;
                case "penalty": penalty = Scalar(v); break;
                case "strategic": strategic = Scalar(v); break;
                case "sim": sim = Scalar(v); break;
                case "source": source = Scalar(v); break;
                default:
                    var map = v as YamlMappingNode ?? throw new ConfigException($"{path}.{key}: expected a quantity");
                    parameters[key] = ToQuantity(map, $"{path}.{key}") ?? throw new ConfigException($"{path}.{key}: expected a quantity");
                    break;
            }
        }
        if (rule is null || penalty is null || sim is null || source is null)
        {
            throw new ConfigException($"{path}: rule, penalty, sim and source are required");
        }
        return new FoulConfig(rule, penalty, strategic, sim, source, parameters);
    }

    private static void CollectQuantities(YamlNode node, string path, Dictionary<string, Quantity> into)
    {
        if (node is not YamlMappingNode map)
        {
            throw new ConfigException($"{path}: value without a source (expected value, unit, source)");
        }
        var q = ToQuantity(map, path);
        if (q is not null)
        {
            into[path] = q;
            return;
        }
        foreach (var (k, v) in map.Children)
        {
            CollectQuantities(v, $"{path}.{Scalar(k)}", into);
        }
    }

    private static Quantity? ToQuantity(YamlMappingNode map, string path)
    {
        var keys = map.Children.Keys.Select(Scalar).ToHashSet(StringComparer.Ordinal);
        if (!keys.Contains("value") || !keys.Contains("unit") || !keys.Contains("source"))
        {
            return null;
        }
        object? value = null;
        string unit = "", source = "";
        foreach (var (k, v) in map.Children)
        {
            switch (Scalar(k))
            {
                case "value": value = ToObject(v); break;
                case "unit": unit = Scalar(v); break;
                case "source": source = Scalar(v); break;
            }
        }
        return new Quantity(path, value, unit, source);
    }

    private static string Scalar(YamlNode node) =>
        node is YamlScalarNode s ? s.Value ?? "" : throw new ConfigException($"expected a scalar at {node.Start}");

    /// <summary>Plain object tree: string scalars (null for YAML null), read-only lists and maps.</summary>
    private static object? ToObject(YamlNode node) => node switch
    {
        YamlScalarNode s when s.Style == YamlDotNet.Core.ScalarStyle.Plain && (s.Value is null || s.Value is "null" or "~" or "") => null,
        YamlScalarNode s => s.Value,
        YamlSequenceNode seq => seq.Children.Select(ToObject).ToList(),
        YamlMappingNode map => map.Children.ToDictionary(kv => Scalar(kv.Key), kv => ToObject(kv.Value), StringComparer.Ordinal),
        _ => throw new ConfigException($"unsupported YAML node at {node.Start}"),
    };
}
