using System;
using System.Collections.Generic;
using Blasphemous.Multiplayer.Common.Enums;

namespace Blasphemous.Multiplayer.Common;

/// <summary>Reassembles length/type/body frames from a TCP byte stream.</summary>
public sealed class PacketBuffer
{
    private readonly List<byte> _bytes = new List<byte>();
    private int _offset;

    public void Append(byte[] bytes)
    {
        if (bytes == null) throw new ArgumentNullException(nameof(bytes));
        _bytes.AddRange(bytes);
    }

    public bool TryRead(out NetworkType type, out byte[] body)
    {
        type = default(NetworkType);
        body = new byte[0];
        int available = _bytes.Count - _offset;
        if (available < 3) return false;
        int length = _bytes[_offset] | (_bytes[_offset + 1] << 8);
        if (available < length + 3) return false;

        type = (NetworkType)_bytes[_offset + 2];
        body = _bytes.GetRange(_offset + 3, length).ToArray();
        _offset += length + 3;
        if (_offset == _bytes.Count)
            Clear();
        else if (_offset >= 65536)
        {
            _bytes.RemoveRange(0, _offset);
            _offset = 0;
        }
        return true;
    }

    public void Clear()
    {
        _bytes.Clear();
        _offset = 0;
    }
}
