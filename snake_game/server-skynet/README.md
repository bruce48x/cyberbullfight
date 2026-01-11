# 多人联机贪吃蛇（Skynet 版）

基于 Skynet 框架的多人联机贪吃蛇游戏服务器，与 C# 客户端兼容。

## 架构

- **服务端** (`server-skynet/`): 使用 Skynet 框架，管理游戏状态和玩家连接
- **客户端** (`client-cs/`): C# 控制台客户端，通过 WASD 控制贪吃蛇移动

## 运行

### 1. 编译

```bash
# 编译 skynet
cd snake_game/server-skynet/skynet
make linux

# 编译 cjson
cd snake_game/server-skynet
make
```

### 2. 启动服务端

```bash
cd snake_game/server-skynet
./skynet/skynet etc/config.node1
./skynet/skynet etc/config.node2
```

服务端默认监听 `0.0.0.0:5000`

### 3. 启动客户端

在另一个终端窗口：

```bash
cd snake_game/client-cs
dotnet run [服务器地址] [端口] [玩家名称]
```

示例：
```bash
# 连接到本地服务器，使用默认端口
dotnet run

# 连接到指定服务器和端口
dotnet run 192.168.1.100 5000

# 指定玩家名称
dotnet run 127.0.0.1 5000 "我的名字"
```

## 技术特点

- **Skynet 框架**: 基于 Actor 模型的并发框架
- **协议兼容**: 与 C# 服务器和客户端完全兼容
- **权威服务器**: 所有游戏逻辑在服务端执行，防止作弊
- **实时同步**: 服务端定期广播游戏状态给所有客户端
- **多玩家支持**: 支持多个玩家同时在线游戏

## 服务说明

- `gateway`: 处理 TCP 连接和协议编解码
- `session`: 代表客户端连接
- `match_loop`: 处理匹配逻辑
- `room`: 处理游戏逻辑（房间、游戏循环）

## 协议

使用 pomelo 协议，与 C# 版本完全兼容。
