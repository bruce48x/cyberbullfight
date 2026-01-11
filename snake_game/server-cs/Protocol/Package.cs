using System.Buffers;

namespace SnakeGame.Server.Protocol;

public static class PackageType
{
    public const byte Handshake = 1;
    public const byte HandshakeAck = 2;
    public const byte Heartbeat = 3;
    public const byte Data = 4;
    public const byte Kick = 5;
}

public class Package
{
    public byte Type { get; set; }
    public int Length { get; set; }
    public byte[] Body { get; set; } = Array.Empty<byte>();

    public static byte[] Encode(byte pkgType, byte[]? body)
    {
        int bodyLen = body?.Length ?? 0;
        byte[] result = new byte[4 + bodyLen];

        result[0] = pkgType;
        result[1] = (byte)((bodyLen >> 16) & 0xFF);
        result[2] = (byte)((bodyLen >> 8) & 0xFF);
        result[3] = (byte)(bodyLen & 0xFF);

        if (bodyLen > 0 && body != null)
        {
            Buffer.BlockCopy(body, 0, result, 4, bodyLen);
        }

        return result;
    }

    public static Package? Decode(byte[] data)
    {
        if (data.Length < 4)
            return null;

        byte pkgType = data[0];
        int length = (data[1] << 16) | (data[2] << 8) | data[3];

        if (data.Length < 4 + length)
            return null;

        byte[] body = new byte[length];
        if (length > 0)
        {
            Buffer.BlockCopy(data, 4, body, 0, length);
        }

        return new Package
        {
            Type = pkgType,
            Length = length,
            Body = body
        };
    }

    // 使用 System.IO.Pipelines 优化的解码方法
    public static Package? Decode(ref ReadOnlySequence<byte> buffer)
    {
        if (buffer.Length < 4)
            return null;

        var reader = new SequenceReader<byte>(buffer);
        
        if (!reader.TryRead(out var pkgType))
            return null;
        
        if (!reader.TryRead(out var b1) || !reader.TryRead(out var b2) || !reader.TryRead(out var b3))
            return null;

        int length = (b1 << 16) | (b2 << 8) | b3;

        if (buffer.Length < 4 + length)
            return null;

        byte[] body = Array.Empty<byte>();
        if (length > 0)
        {
            body = new byte[length];
            var bodySequence = buffer.Slice(4, length);
            bodySequence.CopyTo(body);
        }

        // 消费已读取的数据
        buffer = buffer.Slice(4 + length);

        return new Package
        {
            Type = pkgType,
            Length = length,
            Body = body
        };
    }
}

