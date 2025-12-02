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
-e "SERVER_HOST=172.20.3.228" \
-e "COUNT=1000" \
-d 60 > benchmark.log 2>&1 &
```

跑一次 echo 的结果（客户端是 client-go ，1000 个机器人）

```log
==========================================
运行环境信息
==========================================
CPU: 2 核心, Intel(R) Xeon(R) Platinum 8163 CPU @ 2.50GHz
     频率: 2500 MHz
内存: 总计 3.6 GB, 可用 3.0 GB
==========================================
分析监控结果...

=== 性能测试结果汇总 ===


server-pinus_stat:
  CPU：平均 7.0%，峰值 22.9%，P99 22.9%
  内存：平均 106.6 MB，峰值 119.0 MB
  磁盘：读 0.0 KB/s，写 0.0 KB/s
  网络：收 82.8 KB/s，发 124.3 KB/s
  上下文切换：平均 1016.9 次/s，峰值 1041.0 次/s


server-cs_stat:
  CPU：平均 20.8%，峰值 61.5%，P99 61.5%
  内存：平均 47.4 MB，峰值 53.0 MB
  磁盘：读 0.0 KB/s，写 0.0 KB/s
  网络：收 101.1 KB/s，发 160.7 KB/s
  上下文切换：平均 438178.8 次/s，峰值 780322.0 次/s


server-skynet_stat:
  CPU：平均 18.0%，峰值 75.2%，P99 75.2%
  内存：平均 184.3 MB，峰值 191.0 MB
  磁盘：读 0.0 KB/s，写 0.0 KB/s
  网络：收 91.3 KB/s，发 149.9 KB/s
  上下文切换：平均 95215.4 次/s，峰值 159916.0 次/s


server-go_stat:
  CPU：平均 6.4%，峰值 22.0%，P99 22.0%
  内存：平均 23.6 MB，峰值 27.0 MB
  磁盘：读 0.0 KB/s，写 0.0 KB/s
  网络：收 111.2 KB/s，发 181.4 KB/s
  上下文切换：平均 31236.8 次/s，峰值 56693.0 次/s


server-cpp_stat:
  CPU：平均 9.8%，峰值 71.3%，P99 71.3%
  内存：平均 89.9 MB，峰值 99.0 MB
  磁盘：读 0.0 KB/s，写 0.0 KB/s
  网络：收 113.4 KB/s，发 189.3 KB/s
  上下文切换：平均 22377.4 次/s，峰值 59244.0 次/s


=== 结论（自动推断） ===
CPU 利用率最低：server-go_stat（平均 6.4%）
内存占用最低：server-go_stat（平均 23.6 MB）
锁竞争最少（ctx 切换最低）：server-pinus_stat（平均 1016.9 次/s）

分析完毕。
```
