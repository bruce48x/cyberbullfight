# Java Echo Server

基于 OpenJDK 25 实现的 Echo 服务器，参考 `server-cs` 的实现逻辑。

## 编译和运行

### 前提条件

- JDK 25 (OpenJDK 25 或 Eclipse Temurin 25)
- Maven 3.6+

### 编译项目

```bash
# 进入项目目录
cd server-java

# 编译项目（包含测试）
mvn clean compile

# 或者编译并打包（推荐，会生成可执行 JAR）
mvn clean package
```

编译成功后，会在 `target/` 目录下生成：
- `server-java-1.0.0.jar` - 包含所有依赖的可执行 JAR 文件（fat JAR）

### 运行项目

#### 方式 1: 使用 Maven 直接运行

```bash
mvn exec:java -Dexec.mainClass="com.server.Main"
```

#### 方式 2: 运行打包后的 JAR 文件（推荐）

```bash
# 先打包
mvn clean package

# 运行 JAR
java -jar target/server-java-1.0.0.jar
```

#### 方式 3: 使用 java 命令运行（需要指定 classpath）

```bash
# 编译
mvn clean compile

# 运行（需要手动添加依赖）
java -cp "target/classes:$(mvn dependency:build-classpath -q -Dmdep.outputFile=/dev/stdout)" com.server.Main
```

### 使用 Docker 运行

```bash
# 构建 Docker 镜像
docker build -t server-java .

# 运行容器
docker run -p 3010:3010 server-java
```

## 项目结构

```
server-java/
├── pom.xml                    # Maven 配置文件
├── Dockerfile                 # Docker 构建文件
├── src/main/java/com/server/
│   ├── Main.java              # 主程序入口
│   ├── protocol/              # 协议实现
│   ├── socket/                # Socket 抽象
│   └── session/               # 会话管理
└── target/                    # 编译输出目录
```

## 功能特性

- TCP 服务器监听端口 3010
- 支持握手、心跳、数据处理
- 路由处理器注册（如 `connector.entryHandler.hello`）
- 心跳机制：10 秒间隔，20 秒超时

## 常用 Maven 命令

```bash
# 清理编译文件
mvn clean

# 编译源代码
mvn compile

# 运行测试
mvn test

# 打包项目
mvn package

# 安装到本地仓库
mvn install

# 跳过测试打包
mvn package -DskipTests
```

