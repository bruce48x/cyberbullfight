# Pinus TCP Client (Go)

Go 实现的 Pinus TCP 客户端，功能与 Node.js 版本相同，但体积更小，适合 Docker 部署。

## 功能特性

- TCP 连接管理
- Pinus 协议支持（Package 和 Message）
- 握手（Handshake）
- 心跳（Heartbeat）
- 消息编码/解码（支持 Protobuf 和 JSON）
- 路由压缩/解压
- 请求/响应机制
- 通知机制

## 构建

```bash
go build -o client-go .
```

## 运行

```bash
./client-go
```

或使用环境变量：

```bash
HOST=127.0.0.1 PORT=3010 ./client-go
```

## Docker 构建

```bash
docker build -t client-go .
docker run client-go
```

## 与 Node.js 版本的对比

- **体积**: Go 版本编译后的二进制文件通常只有几 MB，而 Node.js 版本需要完整的 Node.js 运行时和 node_modules
- **启动速度**: Go 版本启动更快
- **资源占用**: Go 版本内存占用更少
- **功能**: 功能完全一致

