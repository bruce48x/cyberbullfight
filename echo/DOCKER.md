# Docker 部署指南

本文档介绍如何使用 Docker 构建和部署各个组件。

## 快速开始

### 使用 docker-compose（推荐）

一键启动所有服务：

```bash
docker-compose up -d
```

查看日志：

```bash
docker-compose logs -f
```

停止所有服务：

```bash
docker-compose down
```

### 单独构建镜像

使用构建脚本：

```bash
chmod +x build-docker.sh
./build-docker.sh
```

或手动构建：

```bash
# 构建 server-pinus
docker build -t cyberbullfight-server-pinus:latest ./server-pinus

# 构建 server-skynet
docker build -t cyberbullfight-server-skynet:latest ./server-skynet

# 构建 client
docker build -t cyberbullfight-client:latest ./client

# 构建 client-go
docker build -t cyberbullfight-client-go:latest ./client-go
```

## 单独运行容器

### server-pinus

```bash
docker run -d \
  --name cyberbullfight-server-pinus \
  -p 3010:3010 \
  cyberbullfight-server-pinus:latest
```

### server-skynet

```bash
docker run -d \
  --name cyberbullfight-server-skynet \
  -p 3011:3010 \
  cyberbullfight-server-skynet:latest
```

### client

```bash
docker run -d \
  --name cyberbullfight-client \
  -e HOST=server-pinus \
  -e PORT=3010 \
  --link cyberbullfight-server-pinus:server-pinus \
  cyberbullfight-client:latest
```

### client-go

```bash
docker run -d \
  --name cyberbullfight-client-go \
  -e HOST=server-pinus \
  -e PORT=3010 \
  --link cyberbullfight-server-pinus:server-pinus \
  cyberbullfight-client-go:latest
```

## 环境变量

### client-go

- `HOST`: 服务器地址（默认: 127.0.0.1）
- `PORT`: 服务器端口（默认: 3010）

### client

- `HOST`: 服务器地址（默认: 127.0.0.1）
- `PORT`: 服务器端口（默认: 3010）

## 部署到远程服务器

### 方法 1: 使用 Docker Hub

1. 构建并标记镜像：

```bash
docker build -t your-dockerhub-username/cyberbullfight-server-pinus:latest ./server-pinus
docker build -t your-dockerhub-username/cyberbullfight-server-skynet:latest ./server-skynet
docker build -t your-dockerhub-username/cyberbullfight-client:latest ./client
docker build -t your-dockerhub-username/cyberbullfight-client-go:latest ./client-go
```

2. 推送到 Docker Hub：

```bash
docker push your-dockerhub-username/cyberbullfight-server-pinus:latest
docker push your-dockerhub-username/cyberbullfight-server-skynet:latest
docker push your-dockerhub-username/cyberbullfight-client:latest
docker push your-dockerhub-username/cyberbullfight-client-go:latest
```

3. 在远程服务器上拉取并运行：

```bash
docker pull your-dockerhub-username/cyberbullfight-server-pinus:latest
docker run -d -p 3010:3010 your-dockerhub-username/cyberbullfight-server-pinus:latest
```

### 方法 2: 使用 docker save/load

1. 在本地构建镜像并保存：

```bash
docker build -t cyberbullfight-server-pinus:latest ./server-pinus
docker save cyberbullfight-server-pinus:latest | gzip > server-pinus.tar.gz
```

2. 传输到远程服务器：

```bash
scp server-pinus.tar.gz user@remote-server:/path/to/destination
```

3. 在远程服务器上加载：

```bash
docker load < server-pinus.tar.gz
docker run -d -p 3010:3010 cyberbullfight-server-pinus:latest
```

### 方法 3: 在远程服务器上直接构建

1. 将代码传输到远程服务器：

```bash
rsync -avz --exclude 'node_modules' --exclude '.git' ./ user@remote-server:/path/to/destination
```

2. 在远程服务器上构建：

```bash
cd /path/to/destination
./build-docker.sh
docker-compose up -d
```

## 查看日志

```bash
# 查看所有服务日志
docker-compose logs -f

# 查看特定服务日志
docker-compose logs -f server-pinus
docker logs -f cyberbullfight-server-pinus
```

## 端口说明

- `server-pinus`: 3010
- `server-skynet`: 3011 (映射到容器内的 3010)
- `client`: 无对外端口
- `client-go`: 无对外端口

## 故障排查

1. 检查容器状态：

```bash
docker ps -a
```

2. 查看容器日志：

```bash
docker logs <container-name>
```

3. 进入容器调试：

```bash
docker exec -it <container-name> /bin/sh
```

4. 检查网络连接：

```bash
docker network ls
docker network inspect echo_cyberbullfight-network
```

