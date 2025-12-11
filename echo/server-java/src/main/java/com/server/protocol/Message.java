package com.server.protocol;

import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

public class Message {
    private int id;
    private int type;
    private boolean compressRoute;
    private String route;
    private byte[] body;
    private boolean compressGzip;

    public Message() {
        this.route = "";
        this.body = new byte[0];
    }

    public int getId() {
        return id;
    }

    public void setId(int id) {
        this.id = id;
    }

    public int getType() {
        return type;
    }

    public void setType(int type) {
        this.type = type;
    }

    public boolean isCompressRoute() {
        return compressRoute;
    }

    public void setCompressRoute(boolean compressRoute) {
        this.compressRoute = compressRoute;
    }

    public String getRoute() {
        return route;
    }

    public void setRoute(String route) {
        this.route = route != null ? route : "";
    }

    public byte[] getBody() {
        return body;
    }

    public void setBody(byte[] body) {
        this.body = body != null ? body : new byte[0];
    }

    public boolean isCompressGzip() {
        return compressGzip;
    }

    public void setCompressGzip(boolean compressGzip) {
        this.compressGzip = compressGzip;
    }

    public static byte[] encode(int id, int msgType, boolean compressRoute, String route, byte[] body) {
        List<Byte> result = new ArrayList<>();

        // Encode flag: type(3 bits) << 1 | compressRoute(1 bit)
        byte flag = (byte) (msgType << 1);
        if (compressRoute) {
            flag |= 1;
        }

        result.add(flag);

        // Encode id (base128, only for REQUEST/RESPONSE)
        if (msgType == MessageType.REQUEST || msgType == MessageType.RESPONSE) {
            int idVal = id;
            do {
                int tmp = idVal % 128;
                int next = idVal / 128;
                if (next != 0) {
                    tmp += 128;
                }
                result.add((byte) tmp);
                idVal = next;
            } while (idVal != 0);
        }

        // Encode route (only for REQUEST/NOTIFY/PUSH)
        if (msgType == MessageType.REQUEST || msgType == MessageType.NOTIFY || msgType == MessageType.PUSH) {
            if (compressRoute) {
                // Compressed route: 2 bytes (big-endian)
                int routeNum = 0;
                result.add((byte) ((routeNum >> 8) & 0xFF));
                result.add((byte) (routeNum & 0xFF));
            } else {
                // Full route string: 1 byte length + route string
                byte[] routeBytes = route.getBytes(StandardCharsets.UTF_8);
                result.add((byte) routeBytes.length);
                for (byte b : routeBytes) {
                    result.add(b);
                }
            }
        }

        // Encode body
        if (body != null) {
            for (byte b : body) {
                result.add(b);
            }
        }

        byte[] array = new byte[result.size()];
        for (int i = 0; i < result.size(); i++) {
            array[i] = result.get(i);
        }
        return array;
    }

    public static Message decode(byte[] data) {
        if (data == null || data.length < 1) {
            return null;
        }

        int offset = 0;

        // Parse flag (1 byte)
        byte flag = data[offset];
        offset++;

        boolean compressRoute = (flag & 0x1) == 1;
        int msgType = (flag >> 1) & 0x7;
        boolean compressGzip = ((flag >> 4) & 0x1) == 1;

        // Parse id (base128 encoded, only for REQUEST/RESPONSE)
        int id = 0;
        if (msgType == MessageType.REQUEST || msgType == MessageType.RESPONSE) {
            int i = 0;
            while (true) {
                if (offset >= data.length) {
                    return null;
                }

                byte m = data[offset];
                id += (m & 0x7F) << (7 * i);
                offset++;
                i++;
                if (m < 128) {
                    break;
                }
            }
        }

        // Parse route (only for REQUEST/NOTIFY/PUSH)
        String route = "";
        if (msgType == MessageType.REQUEST || msgType == MessageType.NOTIFY || msgType == MessageType.PUSH) {
            if (compressRoute) {
                // Compressed route: 2 bytes (big-endian)
                if (offset + 2 > data.length) {
                    return null;
                }
                offset += 2;
            } else {
                // Full route string: 1 byte length + route string
                if (offset >= data.length) {
                    return null;
                }

                int routeLen = data[offset] & 0xFF;
                offset++;

                if (routeLen > 0) {
                    if (offset + routeLen > data.length) {
                        return null;
                    }

                    route = new String(data, offset, routeLen, StandardCharsets.UTF_8);
                    offset += routeLen;
                }
            }
        }

        // Read body (remaining bytes)
        byte[] body = new byte[0];
        if (offset < data.length) {
            body = new byte[data.length - offset];
            System.arraycopy(data, offset, body, 0, body.length);
        }

        Message msg = new Message();
        msg.setId(id);
        msg.setType(msgType);
        msg.setCompressRoute(compressRoute);
        msg.setRoute(route);
        msg.setBody(body);
        msg.setCompressGzip(compressGzip);
        return msg;
    }
}

