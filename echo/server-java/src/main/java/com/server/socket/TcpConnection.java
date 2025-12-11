package com.server.socket;

import java.io.IOException;
import java.net.SocketAddress;
import java.nio.ByteBuffer;
import java.nio.channels.AsynchronousCloseException;
import java.nio.channels.SocketChannel;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public class TcpConnection implements IConnection {
    private final SocketChannel socket;
    private final ExecutorService executor;
    private volatile boolean closed = false;

    public TcpConnection(SocketChannel socket) {
        this.socket = socket;
        this.executor = Executors.newSingleThreadExecutor(r -> {
            Thread t = new Thread(r, "TcpConnection-" + socket.socket().getRemoteSocketAddress());
            t.setDaemon(true);
            return t;
        });
    }

    @Override
    public CompletableFuture<Void> sendAsync(byte[] data) {
        if (closed) {
            return CompletableFuture.completedFuture(null);
        }

        return CompletableFuture.runAsync(() -> {
            try {
                ByteBuffer buffer = ByteBuffer.wrap(data);
                while (buffer.hasRemaining() && !closed) {
                    socket.write(buffer);
                }
            } catch (IOException e) {
                if (!(e instanceof AsynchronousCloseException)) {
                    // Ignore errors during send
                }
            }
        }, executor);
    }

    @Override
    public CompletableFuture<ByteBuffer> readAsync() {
        if (closed) {
            return CompletableFuture.completedFuture(null);
        }

        return CompletableFuture.supplyAsync(() -> {
            try {
                ByteBuffer buffer = ByteBuffer.allocate(8192);
                int bytesRead = socket.read(buffer);
                if (bytesRead == -1 || bytesRead == 0) {
                    return null;
                }
                buffer.flip();
                ByteBuffer result = ByteBuffer.allocate(bytesRead);
                result.put(buffer);
                result.flip();
                return result;
            } catch (IOException e) {
                if (e instanceof AsynchronousCloseException) {
                    return null;
                }
                return null;
            }
        }, executor);
    }

    @Override
    public SocketAddress getRemoteAddress() {
        try {
            return socket.getRemoteAddress();
        } catch (IOException e) {
            return null;
        }
    }

    @Override
    public void close() {
        if (closed) {
            return;
        }
        closed = true;

        try {
            socket.shutdownInput();
        } catch (IOException e) {
            // Ignore
        }

        try {
            socket.shutdownOutput();
        } catch (IOException e) {
            // Ignore
        }

        try {
            socket.close();
        } catch (IOException e) {
            // Ignore
        }

        executor.shutdown();
    }

    @Override
    public boolean isClosed() {
        return closed || !socket.isOpen();
    }
}

