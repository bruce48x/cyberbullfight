package com.server.socket;

import java.net.SocketAddress;
import java.nio.ByteBuffer;
import java.util.concurrent.CompletableFuture;

public interface IConnection {
    CompletableFuture<Void> sendAsync(byte[] data);
    CompletableFuture<ByteBuffer> readAsync();
    SocketAddress getRemoteAddress();
    void close();
    boolean isClosed();
}

