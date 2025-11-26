# echo 对比

## 运行

运行 pinus 服务端

```sh
cd server-pinus/dist
node app
```

运行 nodejs 客户端

```sh
cd client-js
node dist/app
```

运行 skynet 服务端

```sh
cd server-skynet
./skynet/skynet ./etc/config
```

运行 go 客户端

```sh
cd client-go
go run main.go
```

## Docker 部署

### 构建镜像

```bash
./build-docker.sh
```

### 运行容器

#### server-pinus

```bash
docker run -d \
  --name cyberbullfight-server-pinus \
  -e TZ=Asia/Shanghai
  -p 3010:3010 \
  cyberbullfight-server-pinus:latest
```

#### server-skynet

```bash
docker run -d \
  --name cyberbullfight-server-skynet \
  -e TZ=Asia/Shanghai
  -p 3011:3010 \
  cyberbullfight-server-skynet:latest
```

#### client-js

```bash
docker run -d \
  --name cyberbullfight-client-js \
  -e TZ=Asia/Shanghai
  -e SERVER_HOST=server-pinus \
  cyberbullfight-client-js:latest
```

#### client-go

```bash
docker run -d \
  --name cyberbullfight-client-go \
  -e TZ=Asia/Shanghai
  -e HOST=server-pinus \
  cyberbullfight-client-go:latest
```

### 环境变量

- `SERVER_HOST`: 服务器地址（默认: 127.0.0.1）
- `SERVER_PORT`: 服务器端口（默认: 3010）

### 端口说明

- `server-pinus`: 3010
- `server-skynet`: 3011 (映射到容器内的 3010)

### 查看日志

```bash
docker logs -f <container-name>
```