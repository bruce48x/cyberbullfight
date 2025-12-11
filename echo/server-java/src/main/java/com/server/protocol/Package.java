package com.server.protocol;

public class Package {
    private byte type;
    private int length;
    private byte[] body;

    public Package() {
        this.body = new byte[0];
    }

    public byte getType() {
        return type;
    }

    public void setType(byte type) {
        this.type = type;
    }

    public int getLength() {
        return length;
    }

    public void setLength(int length) {
        this.length = length;
    }

    public byte[] getBody() {
        return body;
    }

    public void setBody(byte[] body) {
        this.body = body != null ? body : new byte[0];
    }

    public static byte[] encode(byte pkgType, byte[] body) {
        int bodyLen = body != null ? body.length : 0;
        byte[] result = new byte[4 + bodyLen];

        result[0] = pkgType;
        result[1] = (byte) ((bodyLen >> 16) & 0xFF);
        result[2] = (byte) ((bodyLen >> 8) & 0xFF);
        result[3] = (byte) (bodyLen & 0xFF);

        if (bodyLen > 0 && body != null) {
            System.arraycopy(body, 0, result, 4, bodyLen);
        }

        return result;
    }

    public static Package decode(byte[] data) {
        if (data == null || data.length < 4) {
            return null;
        }

        byte pkgType = data[0];
        int length = ((data[1] & 0xFF) << 16) | ((data[2] & 0xFF) << 8) | (data[3] & 0xFF);

        if (data.length < 4 + length) {
            return null;
        }

        byte[] body = new byte[length];
        if (length > 0) {
            System.arraycopy(data, 4, body, 0, length);
        }

        Package pkg = new Package();
        pkg.setType(pkgType);
        pkg.setLength(length);
        pkg.setBody(body);
        return pkg;
    }
}

