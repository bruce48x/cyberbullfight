package com.server.socket;

import java.io.IOException;
import java.net.InetSocketAddress;
import java.net.SocketAddress;
import java.nio.channels.ServerSocketChannel;
import java.nio.channels.SocketChannel;
import java.util.concurrent.CompletableFuture;
import java.util.function.Function;

public class TcpListenerTransport {
    private final ServerSocketChannel serverChannel;
    private final Function<IConnection, CompletableFuture<Void>> onConnection;

    public TcpListenerTransport(SocketAddress endpoint, Function<IConnection, CompletableFuture<Void>> onConn) throws IOException {
        this.onConnection = onConn;
        this.serverChannel = ServerSocketChannel.open();
        this.serverChannel.bind(endpoint);
        this.serverChannel.configureBlocking(true);
    }

    public CompletableFuture<Void> startAsync() {
        return CompletableFuture.runAsync(() -> {
            try {
                while (serverChannel.isOpen()) {
                    SocketChannel clientChannel = serverChannel.accept();
                    if (clientChannel != null) {
                        configureSocket(clientChannel);
                        IConnection conn = new TcpConnection(clientChannel);
                        // Fire and forget - handle connection in background
                        CompletableFuture.runAsync(() -> {
                            try {
                                onConnection.apply(conn).join();
                            } catch (Exception ex) {
                                System.err.println("Error handling connection: " + ex.getMessage());
                            }
                        });
                    }
                }
            } catch (IOException e) {
                if (serverChannel.isOpen()) {
                    System.err.println("Error accepting connections: " + e.getMessage());
                }
            }
        });
    }

    public void close() throws IOException {
        if (serverChannel.isOpen()) {
            serverChannel.close();
        }
    }

    private void configureSocket(SocketChannel socket) throws IOException {
        socket.socket().setTcpNoDelay(true);
        socket.socket().setKeepAlive(true);
    }
}

