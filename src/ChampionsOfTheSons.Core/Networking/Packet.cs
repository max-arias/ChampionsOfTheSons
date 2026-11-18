using System.Text;

namespace ChampionsOfTheSons.Core.Networking;

/// <summary>Little-endian binary writer for net messages. Pure byte buffer, no transport.</summary>
public sealed class PacketWriter
{
    private byte[] _buffer = new byte[64];

    public int Length { get; private set; }

    private void Ensure(int extra)
    {
        if (Length + extra <= _buffer.Length) return;
        Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, Length + extra));
    }

    public PacketWriter Byte(byte v)
    {
        Ensure(1);
        _buffer[Length++] = v;
        return this;
    }

    public PacketWriter Bool(bool v) => Byte(v ? (byte)1 : (byte)0);

    /// <summary>Unsigned LEB128 varint.</summary>
    public PacketWriter VarUInt(uint v)
    {
        while (v >= 0x80)
        {
            Byte((byte)(v | 0x80));
            v >>= 7;
        }
        return Byte((byte)v);
    }

    /// <summary>Zig-zag signed varint.</summary>
    public PacketWriter VarInt(int v) => VarUInt((uint)((v << 1) ^ (v >> 31)));

    public PacketWriter UInt64(ulong v)
    {
        Ensure(8);
        for (int i = 0; i < 8; i++) _buffer[Length++] = (byte)(v >> (8 * i));
        return this;
    }

    public PacketWriter Float(float v)
    {
        int bits = BitConverter.SingleToInt32Bits(v);
        Ensure(4);
        for (int i = 0; i < 4; i++) _buffer[Length++] = (byte)(bits >> (8 * i));
        return this;
    }

    public PacketWriter String(string? v)
    {
        if (v == null) return VarUInt(0);
        var bytes = Encoding.UTF8.GetBytes(v);
        VarUInt((uint)bytes.Length + 1);
        Ensure(bytes.Length);
        Buffer.BlockCopy(bytes, 0, _buffer, Length, bytes.Length);
        Length += bytes.Length;
        return this;
    }

    public byte[] ToArray()
    {
        var result = new byte[Length];
        Buffer.BlockCopy(_buffer, 0, result, 0, Length);
        return result;
    }
}

/// <summary>Reader matching <see cref="PacketWriter"/>. Throws <see cref="PacketException"/> on truncated or malformed data.</summary>
public sealed class PacketReader
{
    private readonly byte[] _data;
    private int _pos;

    public PacketReader(byte[] data) => _data = data;

    public bool AtEnd => _pos >= _data.Length;

    public byte Byte()
    {
        if (_pos >= _data.Length) throw new PacketException("Unexpected end of packet");
        return _data[_pos++];
    }

    public bool Bool() => Byte() != 0;

    public uint VarUInt()
    {
        uint result = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            byte b = Byte();
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
        }
        throw new PacketException("Varint too long");
    }

    public int VarInt()
    {
        uint v = VarUInt();
        return (int)(v >> 1) ^ -(int)(v & 1);
    }

    public ulong UInt64()
    {
        ulong v = 0;
        for (int i = 0; i < 8; i++) v |= (ulong)Byte() << (8 * i);
        return v;
    }

    public float Float()
    {
        int bits = 0;
        for (int i = 0; i < 4; i++) bits |= Byte() << (8 * i);
        return BitConverter.Int32BitsToSingle(bits);
    }

    public string? String()
    {
        uint len = VarUInt();
        if (len == 0) return null;
        int count = (int)len - 1;
        if (count > _data.Length - _pos) throw new PacketException("String runs past end of packet");
        var s = Encoding.UTF8.GetString(_data, _pos, count);
        _pos += count;
        return s;
    }
}

public sealed class PacketException : Exception
{
    public PacketException(string message) : base(message) { }
}
