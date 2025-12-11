package com.server;

import com.server.session.Session;
import com.server.socket.IConnection;
import com.server.socket.TcpListenerTransport;

import java.net.InetSocketAddress;
import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.CompletableFuture;

public class Main {
    private static final int PORT = 3010;

    public static void main(String[] args) {
        try {
            InetSocketAddress endpoint = new InetSocketAddress("0.0.0.0", PORT);

            TcpListenerTransport listener = new TcpListenerTransport(endpoint, conn -> {
                Session sess = new Session(conn);
                return sess.startAsync();
            });

            System.out.println("[main] Server listening on port " + PORT);

            // Register handlers
            Session.registerHandler("connector.entryHandler.hello", (s, body) -> {
                s.setReqId(s.getReqId() + 1);
                if (body == null) {
                    body = new HashMap<>();
                }
                body.put("serverReqId", s.getReqId());
                Map<String, Object> response = new HashMap<>();
                response.put("code", 0);
                response.put("msg", body);
                return response;
            });

            // Handle graceful shutdown
            Runtime.getRuntime().addShutdownHook(new Thread(() -> {
                System.out.println("[main] Shutting down server...");
                try {
                    listener.close();
                } catch (Exception e) {
                    System.err.println("Error closing listener: " + e.getMessage());
                }
            }));

            // Start server
            listener.startAsync().join();
        } catch (Exception e) {
            System.err.println("Error starting server: " + e.getMessage());
            e.printStackTrace();
            System.exit(1);
        }
    }
}

