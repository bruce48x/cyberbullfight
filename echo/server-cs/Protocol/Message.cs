namespace ServerCs.Protocol;

public static class MessageType
{
    public const int Request = 0;
    public const int Notify = 1;
    public const int Response = 2;
    public const int Push = 3;
}

public class Message
{
    public int Id { get; set; }
    public int Type { get; set; }
    public bool CompressRoute { get; set; }
    public string Route { get; set; } = string.Empty;
    public byte[] Body { get; set; } = Array.Empty<byte>();
    public bool CompressGzip { get; set; }

    public static byte[] Encode(int id, int msgType, bool compressRoute, string route, byte[]? body)
    {
        var result = new List<byte>();

        // Encode flag: type(3 bits) << 1 | compressRoute(1 bit)
        byte flag = (byte)(msgType << 1);
        if (compressRoute)
        {
            flag |= 1;
        }

        result.Add(flag);

        // Encode id (base128, only for REQUEST/RESPONSE)
        if (msgType == MessageType.Request || msgType == MessageType.Response)
        {
            int idVal = id;
            do
            {
                int tmp = idVal % 128;
                int next = idVal / 128;
                if (next != 0)
                {
                    tmp += 128;
                }

                result.Add((byte)tmp);
                idVal = next;
            } while (idVal != 0);
        }

        // Encode route (only for REQUEST/NOTIFY/PUSH)
        if (msgType == MessageType.Request || msgType == MessageType.Notify || msgType == MessageType.Push)
        {
            if (compressRoute)
            {
                // Compressed route: 2 bytes (big-endian)
                int routeNum = 0;
                result.Add((byte)((routeNum >> 8) & 0xFF));
                result.Add((byte)(routeNum & 0xFF));
            }
            else
            {
                // Full route string: 1 byte length + route string
                byte[] routeBytes = System.Text.Encoding.UTF8.GetBytes(route);
                result.Add((byte)routeBytes.Length);
                result.AddRange(routeBytes);
            }
        }

        // Encode body
        if (body != null)
        {
            result.AddRange(body);
        }

        return result.ToArray();
    }

    public static Message? Decode(byte[] data)
    {
        if (data.Length < 1)
            return null;

        int offset = 0;

        // Parse flag (1 byte)
        byte flag = data[offset];
        offset++;

        bool compressRoute = (flag & 0x1) == 1;
        int msgType = (flag >> 1) & 0x7;
        bool compressGzip = ((flag >> 4) & 0x1) == 1;

        // Parse id (base128 encoded, only for REQUEST/RESPONSE)
        int id = 0;
        if (msgType == MessageType.Request || msgType == MessageType.Response)
        {
            int i = 0;
            while (true)
            {
                if (offset >= data.Length)
                    return null;

                byte m = data[offset];
                id += (m & 0x7F) << (7 * i);
                offset++;
                i++;
                if (m < 128)
                    break;
            }
        }

        // Parse route (only for REQUEST/NOTIFY/PUSH)
        string route = string.Empty;
        if (msgType == MessageType.Request || msgType == MessageType.Notify || msgType == MessageType.Push)
        {
            if (compressRoute)
            {
                // Compressed route: 2 bytes (big-endian)
                if (offset + 2 > data.Length)
                    return null;
                offset += 2;
            }
            else
            {
                // Full route string: 1 byte length + route string
                if (offset >= data.Length)
                    return null;

                int routeLen = data[offset];
                offset++;

                if (routeLen > 0)
                {
                    if (offset + routeLen > data.Length)
                        return null;

                    route = System.Text.Encoding.UTF8.GetString(data, offset, routeLen);
                    offset += routeLen;
                }
            }
        }

        // Read body (remaining bytes)
        byte[] body = Array.Empty<byte>();
        if (offset < data.Length)
        {
            body = new byte[data.Length - offset];
            Buffer.BlockCopy(data, offset, body, 0, body.Length);
        }

        return new Message
        {
            Id = id,
            Type = msgType,
            CompressRoute = compressRoute,
            Route = route,
            Body = body,
            CompressGzip = compressGzip
        };
    }
}