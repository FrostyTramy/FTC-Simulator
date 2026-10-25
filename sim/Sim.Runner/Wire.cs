using System.Buffers.Binary;
using System.Text;

namespace Sim.Runner;

/// <summary>Builds one frame of docs/ipc_protocol.md: int32 length + payload starting with the type byte, big-endian.</summary>
public sealed class FrameWriter
{
    private byte[] _buffer = new byte[256];
    private int _length = 4;   // room for the length prefix

    public FrameWriter(byte type)
    {
        U8(type);
    }

    public FrameWriter U8(int value)
    {
        Ensure(1);
        _buffer[_length++] = (byte)value;
        return this;
    }

    public FrameWriter Bool(bool value) => U8(value ? 1 : 0);

    public FrameWriter I32(int value)
    {
        Ensure(4);
        BinaryPrimitives.WriteInt32BigEndian(_buffer.AsSpan(_length), value);
        _length += 4;
        return this;
    }

    public FrameWriter I64(long value)
    {
        Ensure(8);
        BinaryPrimitives.WriteInt64BigEndian(_buffer.AsSpan(_length), value);
        _length += 8;
        return this;
    }

    public FrameWriter F32(float value)
    {
        Ensure(4);
        BinaryPrimitives.WriteSingleBigEndian(_buffer.AsSpan(_length), value);
        _length += 4;
        return this;
    }

    public FrameWriter F64(double value)
    {
        Ensure(8);
        BinaryPrimitives.WriteDoubleBigEndian(_buffer.AsSpan(_length), value);
        _length += 8;
        return this;
    }

    public FrameWriter Str(string? value)
    {
        if (value is null)
        {
            return I32(-1);
        }
        var count = Encoding.UTF8.GetByteCount(value);
        I32(count);
        Ensure(count);
        _length += Encoding.UTF8.GetBytes(value, _buffer.AsSpan(_length));
        return this;
    }

    /// <summary>The complete frame including the length prefix.</summary>
    public ReadOnlySpan<byte> Frame
    {
        get
        {
            BinaryPrimitives.WriteInt32BigEndian(_buffer, _length - 4);
            return _buffer.AsSpan(0, _length);
        }
    }

    public void Send(Stream stream)
    {
        stream.Write(Frame);
        stream.Flush();
    }

    private void Ensure(int extra)
    {
        if (_length + extra > _buffer.Length)
        {
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + extra));
        }
    }
}

/// <summary>Reads the fields of one received frame in order.</summary>
public sealed class FrameReader
{
    private readonly byte[] _data;
    private int _position;

    public FrameReader(byte[] payload)
    {
        _data = payload;
    }

    /// <summary>Reads one frame; null at a clean end of stream (the peer closed between frames).</summary>
    public static FrameReader? Read(Stream stream)
    {
        Span<byte> header = stackalloc byte[4];
        var got = 0;
        while (got < 4)
        {
            var n = stream.Read(header[got..]);
            if (n == 0)
            {
                return got == 0 ? null : throw new EndOfStreamException("connection closed inside a frame header");
            }
            got += n;
        }
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 1 || length > 64 * 1024 * 1024)
        {
            throw new InvalidDataException($"bad frame length {length}");
        }
        var payload = new byte[length];
        stream.ReadExactly(payload);
        return new FrameReader(payload);
    }

    public int Remaining => _data.Length - _position;

    public byte U8() => _data[_position++];

    public bool Bool() => U8() != 0;

    public int I32()
    {
        var v = BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(_position));
        _position += 4;
        return v;
    }

    public long I64()
    {
        var v = BinaryPrimitives.ReadInt64BigEndian(_data.AsSpan(_position));
        _position += 8;
        return v;
    }

    public float F32()
    {
        var v = BinaryPrimitives.ReadSingleBigEndian(_data.AsSpan(_position));
        _position += 4;
        return v;
    }

    public double F64()
    {
        var v = BinaryPrimitives.ReadDoubleBigEndian(_data.AsSpan(_position));
        _position += 8;
        return v;
    }

    public string? Str()
    {
        var n = I32();
        if (n < 0)
        {
            return null;
        }
        var s = Encoding.UTF8.GetString(_data, _position, n);
        _position += n;
        return s;
    }

    /// <summary>A string field that the protocol never sends as null.</summary>
    public string Text() => Str() ?? throw new InvalidDataException("unexpected null string");
}
