using System.Globalization;

namespace Sim.Core.Config;

/// <summary>
/// One number (or list/map of values) from a config file with its unit and source reference
/// (manual section, table, figure or rule; "SIM" or "ASSUMED" for our own values).
/// Scalars are kept as invariant-culture strings exactly as written in YAML and parsed on demand.
/// </summary>
public sealed class Quantity
{
    public const double MetresPerInch = 0.0254;

    public Quantity(string path, object? value, string unit, string source)
    {
        Path = path;
        Value = value;
        Unit = unit;
        Source = source;
    }

    /// <summary>Dotted config path, e.g. "timing.auto".</summary>
    public string Path { get; }

    /// <summary>string scalar, IReadOnlyList of object?, IReadOnlyDictionary of string to object?, or null.</summary>
    public object? Value { get; }

    public string Unit { get; }
    public string Source { get; }

    public bool IsNull => Value is null;

    public double AsDouble() => ParseDouble(Value, Path);

    public int AsInt()
    {
        var d = AsDouble();
        var i = (int)Math.Round(d);
        if (Math.Abs(d - i) > 1e-9)
        {
            throw new ConfigException($"{Path}: {d} is not an integer");
        }
        return i;
    }

    public string AsString() => Value as string ?? throw new ConfigException($"{Path}: expected a scalar value");

    public IReadOnlyList<object?> AsList() =>
        Value as IReadOnlyList<object?> ?? throw new ConfigException($"{Path}: expected a list");

    public IReadOnlyDictionary<string, object?> AsMap() =>
        Value as IReadOnlyDictionary<string, object?> ?? throw new ConfigException($"{Path}: expected a map");

    public double[] AsDoubleArray() => AsList().Select(v => ParseDouble(v, Path)).ToArray();

    public string[] AsStringArray() =>
        AsList().Select(v => v as string ?? throw new ConfigException($"{Path}: expected scalars")).ToArray();

    /// <summary>Value converted to metres (unit "in", "mm" or "m").</summary>
    public double Metres() => AsDouble() * MetresFactor();

    public double[] MetresArray()
    {
        var f = MetresFactor();
        return AsDoubleArray().Select(v => v * f).ToArray();
    }

    /// <summary>Value in seconds (unit "s").</summary>
    public double Seconds()
    {
        if (Unit != "s")
        {
            throw new ConfigException($"{Path}: unit '{Unit}' is not seconds");
        }
        return AsDouble();
    }

    private double MetresFactor() => Unit switch
    {
        "in" => MetresPerInch,
        "m" => 1.0,
        "mm" => 0.001,
        _ => throw new ConfigException($"{Path}: unit '{Unit}' is not a length"),
    };

    /// <summary>Parses an invariant-culture number; returns null for null or non-numeric scalars such as "TBA".</summary>
    public static double? TryParseDouble(object? value) =>
        value is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static double ParseDouble(object? value, string path) =>
        TryParseDouble(value) ?? throw new ConfigException($"{path}: '{value}' is not a number");

    public override string ToString() => $"{Path} = {Value} {Unit} ({Source})";
}

public sealed class ConfigException(string message) : Exception(message);
