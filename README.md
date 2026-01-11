# cyberbullfight
游戏服务端框架的横向对比

## 1. echo 

简单的 request/response 测试

## 2. move ball
对比小球移动的计算性能

# 本地运行

```sh
# 运行 pinus 服务端
cd echo/server-pinus/
yarn install
cd dist/
node app

# 运行 skynet 服务端
cd echo/server-skynet
cd skynet && make linux
cd .. && make
./skynet/skynet ./etc/config

# go 服务端
cd echo/server-go
go run main.go

# 运行 c# 服务端
cd echo/server-cs
dotnet run

# 运行 c++ 服务端
cd echo/server-cpp
cmake -B build && cmake --build build
./build/server-cpp

# 运行 nodejs 客户端
cd echo/client-js
yarn install
node dist/app

# 运行 go 客户端
cd echo/client-go
go run main.go
```

# 以容器运行

## 构建镜像

以 echo 为例

```bash
cd echo/
./build-docker.sh
```

## 运行容器

```bash
# server-pinus
docker run -d \
  --name server-pinus \
  -e TZ=Asia/Shanghai
  -p 3010:3010 \
  cyberbullfight-server-pinus:latest

# server-skynet
docker run -d \
  --name server-skynet \
  -e TZ=Asia/Shanghai
  -p 3011:3010 \
  cyberbullfight-server-skynet:latest

# client-js
docker run -d \
  --name client-js \
  -e TZ=Asia/Shanghai
  -e SERVER_HOST=$ip \
  -e COUNT=100 \
  cyberbullfight-client-js:latest

# client-go
docker run -d \
  --name client-go \
  -e TZ=Asia/Shanghai
  -e SERVER_HOST=$ip \
  -e COUNT=100 \
  cyberbullfight-client-go:latest
```

## 环境变量

- `SERVER_HOST`: 服务器地址（默认: 127.0.0.1）
- `SERVER_PORT`: 服务器端口（默认: 3010）
- `COUNT`: 机器人数量

# 数据采集和分析

## 通过容器启动APP

将 `benchmark.sh` 和 `analyze_csv.py` 上传到服务器后，执行以下命令

```sh
# 服务端
nohup ./benchmark.sh -n server-go \
-i bruce48li/cyberbullfight-server-go \
-d 70 > benchmark.log 2>&1 &

# 客户端
nohup ./benchmark.sh -n client-go \
-i bruce48li/cyberbullfight-client-go \
-e "SERVER_HOST=172.20.3.227" \
-e "COUNT=1000" \
-d 60 > benchmark.log 2>&1 &
```

# 测试

[echo测试](./echo/README.md)