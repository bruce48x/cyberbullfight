package com.server.session;

import com.fasterxml.jackson.core.type.TypeReference;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.server.protocol.Message;
import com.server.protocol.MessageType;
import com.server.protocol.Package;
import com.server.protocol.PackageType;
import com.server.socket.IConnection;

import java.nio.ByteBuffer;
import java.time.Duration;
import java.time.Instant;
import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.function.BiFunction;

public class Session {
    private static final ConcurrentHashMap<String, BiFunction<Session, Map<String, Object>, Map<String, Object>>> handlers = new ConcurrentHashMap<>();
    private static final ObjectMapper objectMapper = new ObjectMapper();

    public static void registerHandler(String route, BiFunction<Session, Map<String, Object>, Map<String, Object>> handler) {
        handlers.put(route, handler);
    }

    private final IConnection conn;
    private ConnectionState state = ConnectionState.INITED;
    private Duration heartbeatInterval;
    private Duration heartbeatTimeout;
    private Instant lastHeartbeat;
    private volatile boolean cancelled = false;
    private final Object lock = new Object();
    private int reqId = 0;
    private ByteBuffer readBuffer = ByteBuffer.allocate(65536);
    private final ScheduledExecutorService scheduler = Executors.newScheduledThreadPool(1, r -> {
        Thread t = new Thread(r, "Session-Scheduler");
        t.setDaemon(true);
        return t;
    });

    public Session(IConnection conn) {
        this.conn = conn;
        readBuffer.flip(); // Start with empty buffer
    }

    public int getReqId() {
        return reqId;
    }

    public void setReqId(int reqId) {
        this.reqId = reqId;
    }

    public CompletableFuture<Void> startAsync() {
        System.out.println("TCP connected: " + conn.getRemoteAddress());

        return CompletableFuture.runAsync(() -> {
            try {
                while (!cancelled && !conn.isClosed()) {
                    CompletableFuture<ByteBuffer> readFuture = conn.readAsync();
                    ByteBuffer data = readFuture.get();

                    if (data == null) {
                        break;
                    }
                    
                    if (data.remaining() == 0) {
                        continue;
                    }

                    // Append to read buffer
                    synchronized (lock) {
                        // Compact if needed
                        if (readBuffer.position() > 0 && readBuffer.remaining() < data.remaining()) {
                            readBuffer.compact();
                            readBuffer.flip();
                        }
                        
                        // Expand buffer if needed
                        if (readBuffer.remaining() < data.remaining()) {
                            int newSize = Math.max(readBuffer.capacity() * 2, readBuffer.limit() + data.remaining());
                            ByteBuffer newBuffer = ByteBuffer.allocate(newSize);
                            newBuffer.put(readBuffer);
                            readBuffer = newBuffer;
                        }
                        
                        readBuffer.put(data);
                        readBuffer.flip();
                    }

                    // Process packages
                    while (tryReadPackage()) {
                        // Package processed
                    }
                }
            } catch (Exception e) {
                if (!cancelled) {
                    System.err.println("Error in session: " + e.getMessage());
                }
            } finally {
                conn.close();
                System.out.println("TCP disconnected: " + conn.getRemoteAddress());
            }
        });
    }

    private boolean tryReadPackage() {
        synchronized (lock) {
            if (readBuffer.remaining() < 4) {
                return false;
            }

            int position = readBuffer.position();
            byte type = readBuffer.get();
            int bodyLen = ((readBuffer.get() & 0xFF) << 16) |
                         ((readBuffer.get() & 0xFF) << 8) |
                         (readBuffer.get() & 0xFF);

            if (readBuffer.remaining() < bodyLen) {
                readBuffer.position(position);
                return false;
            }

            byte[] body = new byte[bodyLen];
            if (bodyLen > 0) {
                readBuffer.get(body);
            }

            Package pkg = new Package();
            pkg.setType(type);
            pkg.setLength(bodyLen);
            pkg.setBody(body);

            processPackageAsync(pkg);
            return true;
        }
    }

    private void processPackageAsync(Package pkg) {
        switch (pkg.getType()) {
            case PackageType.HANDSHAKE:
                handleHandshakeAsync(pkg.getBody());
                break;
            case PackageType.HANDSHAKE_ACK:
                handleHandshakeAckAsync();
                break;
            case PackageType.HEARTBEAT:
                handleHeartbeatAsync();
                break;
            case PackageType.DATA:
                handleDataAsync(pkg.getBody());
                break;
            case PackageType.KICK:
                close();
                break;
        }
    }

    private void handleHandshakeAsync(byte[] body) {
        Map<String, Object> response = new HashMap<>();
        response.put("code", 200);
        
        Map<String, Object> sys = new HashMap<>();
        sys.put("heartbeat", 10);
        sys.put("dict", new HashMap<>());
        
        Map<String, Object> protos = new HashMap<>();
        protos.put("client", new HashMap<>());
        protos.put("server", new HashMap<>());
        sys.put("protos", protos);
        
        response.put("sys", sys);
        response.put("user", new HashMap<>());

        try {
            byte[] responseBody = objectMapper.writeValueAsBytes(response);
            byte[] responsePkg = Package.encode(PackageType.HANDSHAKE, responseBody);
            sendAsync(responsePkg);
        } catch (Exception e) {
            System.err.println("Error encoding handshake response: " + e.getMessage());
        }

        synchronized (lock) {
            state = ConnectionState.WAIT_ACK;
            heartbeatInterval = Duration.ofSeconds(10);
            heartbeatTimeout = Duration.ofSeconds(20);
        }
    }

    private void handleHandshakeAckAsync() {
        synchronized (lock) {
            state = ConnectionState.WORKING;
            lastHeartbeat = Instant.now();
        }

        // Start heartbeat
        heartbeatLoopAsync();
    }

    private void handleHeartbeatAsync() {
        synchronized (lock) {
            lastHeartbeat = Instant.now();
        }

        // Send heartbeat response
        byte[] heartbeatPkg = Package.encode(PackageType.HEARTBEAT, null);
        sendAsync(heartbeatPkg);
    }

    private void handleDataAsync(byte[] body) {
        synchronized (lock) {
            lastHeartbeat = Instant.now();
        }

        Message msg = Message.decode(body);
        if (msg == null) {
            System.out.println("[session] Failed to decode message");
            return;
        }

        Map<String, Object> msgBody = null;
        if (msg.getBody().length > 0) {
            try {
                msgBody = objectMapper.readValue(msg.getBody(), new TypeReference<Map<String, Object>>() {});
            } catch (Exception e) {
                // Ignore
            }
        }

        if (msg.getType() == MessageType.REQUEST) {
            handleRequestAsync(msg.getId(), msg.getRoute(), msgBody);
        } else if (msg.getType() == MessageType.NOTIFY) {
            try {
                System.out.println("[session] Notify received: route=" + msg.getRoute() + ", body=" + objectMapper.writeValueAsString(msgBody));
            } catch (Exception e) {
                System.out.println("[session] Notify received: route=" + msg.getRoute());
            }
        }
    }

    private void handleRequestAsync(int id, String route, Map<String, Object> body) {
        Map<String, Object> responseBody;

        BiFunction<Session, Map<String, Object>, Map<String, Object>> handler = handlers.get(route);
        if (handler != null) {
            responseBody = handler.apply(this, body);
        } else {
            System.out.println("[session] Unknown route: " + route);
            responseBody = new HashMap<>();
            responseBody.put("code", 404);
            responseBody.put("msg", "Route not found: " + route);
        }

        try {
            byte[] responseBodyBytes = objectMapper.writeValueAsBytes(responseBody);
            byte[] responseMsg = Message.encode(id, MessageType.RESPONSE, false, "", responseBodyBytes);
            byte[] responsePkg = Package.encode(PackageType.DATA, responseMsg);
            sendAsync(responsePkg);
        } catch (Exception e) {
            System.err.println("Error encoding response: " + e.getMessage());
        }
    }

    private void heartbeatLoopAsync() {
        scheduler.scheduleAtFixedRate(() -> {
            if (cancelled) {
                return;
            }

            ConnectionState currentState;
            Instant lastHB;
            Duration timeout;

            synchronized (lock) {
                currentState = state;
                lastHB = lastHeartbeat;
                timeout = heartbeatTimeout;
            }

            if (currentState != ConnectionState.WORKING) {
                return;
            }

            // Check timeout
            if (lastHB != null && timeout != null && Instant.now().minus(timeout).isAfter(lastHB)) {
                System.out.println("[session] Heartbeat timeout");
                close();
                return;
            }

            // Send heartbeat
            byte[] heartbeatPkg = Package.encode(PackageType.HEARTBEAT, null);
            sendAsync(heartbeatPkg);
        }, heartbeatInterval.toMillis(), heartbeatInterval.toMillis(), TimeUnit.MILLISECONDS);
    }

    private void sendAsync(byte[] data) {
        try {
            conn.sendAsync(data);
        } catch (Exception e) {
            // Ignore
        }
    }

    public void close() {
        synchronized (lock) {
            if (state == ConnectionState.CLOSED) {
                return;
            }
            state = ConnectionState.CLOSED;
        }

        cancelled = true;
        conn.close();
        scheduler.shutdown();
        System.out.println("[session] Connection closed");
    }
}

