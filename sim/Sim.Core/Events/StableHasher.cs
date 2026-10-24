using System.Text;

namespace Sim.Core.Events;

/// <summary>
/// Order-sensitive 64-bit FNV-1a hash over primitive values, used for determinism checks and
/// golden replays (same inputs + same seed = same hash, bit for bit). Doubles are hashed by their
/// exact bit pattern.
/// </summary>
public sealed class StableHasher
{
    private const ulong Offset = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    public ulong Value { get; private set; } = Offset;

    public StableHasher Add(byte b)
    {
        Value = (Value ^ b) * Prime;
        return this;
    }

    public StableHasher Add(long v)
    {
        for (var i = 0; i < 8; i++)
        {
            Add((byte)(v >> (8 * i)));
        }
        return this;
    }

    public StableHasher Add(int v) => Add((long)v);

    public StableHasher Add(bool v) => Add((byte)(v ? 1 : 0));

    public StableHasher Add(double v) => Add(BitConverter.DoubleToInt64Bits(v));

    public StableHasher Add(string? s)
    {
        if (s is null)
        {
            return Add(-1L);
        }
        var bytes = Encoding.UTF8.GetBytes(s);
        Add(bytes.Length);
        foreach (var b in bytes)
        {
            Add(b);
        }
        return this;
    }

    public override string ToString() => Value.ToString("x16");
}
