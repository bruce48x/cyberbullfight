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

# 运行 go 客户端
cd echo/client-go
go run main.go

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
-d 80 > benchmark.log 2>&1 &

# 客户端
nohup ./benchmark.sh -n client-go \
-i bruce48li/cyberbullfight-client-go \
-e "SERVER_HOST=172.20.3.57" \
-e "COUNT=100" \
-d 60 > benchmark.log 2>&1 &
```

跑一次 echo 的结果（客户端是 client-go ，1000个机器人）

```log
分析监控结果...

=== 性能测试结果汇总 ===


server-pinus_stat:
  CPU：平均 7.9%，峰值 42.1%，P99 33.0%
  内存：平均 101.0 MB，峰值 118.0 MB
  磁盘：读 0.0 KB/s，写 0.0 KB/s
  网络：收 96.3 KB/s，发 142.2 KB/s
  上下文切换：平均 925.8 次/s，峰值 944.0 次/s


server-skynet_stat:
  CPU：平均 17.2%，峰值 88.1%，P99 37.6%
  内存：平均 199.3 MB，峰值 212.0 MB
  磁盘：读 0.0 KB/s，写 0.0 KB/s
  网络：收 77.0 KB/s，发 126.9 KB/s
  上下文切换：平均 117543.0 次/s，峰值 197105.0 次/s


server-go_stat:
  CPU：平均 5.9%，峰值 21.7%，P99 14.3%
  内存：平均 21.9 MB，峰值 25.0 MB
  磁盘：读 0.0 KB/s，写 0.0 KB/s
  网络：收 94.9 KB/s，发 156.5 KB/s
  上下文切换：平均 85442.4 次/s，峰值 157148.0 次/s


=== 结论（自动推断） ===
CPU 利用率最低：server-go_stat（平均 5.9%）
内存占用最低：server-go_stat（平均 21.9 MB）
锁竞争最少（ctx 切换最低）：server-pinus_stat（平均 925.8 次/s）

分析完毕。
完成!
```
