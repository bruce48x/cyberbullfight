#!/bin/bash

# =============================================
# Docker 容器性能基准测试脚本
# 启动容器 -> 监控 -> 等待 -> 停止 -> 分析
# =============================================

set -e

# 默认参数
NAME="benchmark_container"
IMAGE_NAME=""
PORT="3010"
DURATION="60"
INTERVAL="1"
ENV_VARS=()

usage() {
    echo "Usage: $0 [options]"
    echo "Options:"
    echo "  -n, --name NAME         容器名称 (default: benchmark_container)"
    echo "  -i, --image IMAGE       镜像名称 (required)"
    echo "  -p, --port PORT         端口映射 (default: 3010)"
    echo "  -d, --duration SECONDS  监控持续时间 (default: 60)"
    echo "  -e, --env KEY=VALUE     环境变量 (可多次使用)"
    echo "  -h, --help              显示帮助"
    exit 1
}

# 解析参数
while [[ $# -gt 0 ]]; do
    case $1 in
        -n|--name) NAME="$2"; shift 2 ;;
        -i|--image) IMAGE_NAME="$2"; shift 2 ;;
        -p|--port) PORT="$2"; shift 2 ;;
        -d|--duration) DURATION="$2"; shift 2 ;;
        -e|--env) ENV_VARS+=("-e" "$2"); shift 2 ;;
        -h|--help) usage ;;
        *) echo "Unknown option: $1"; usage ;;
    esac
done

if [ -z "$IMAGE_NAME" ]; then
    echo "Error: 请指定镜像名称 (-i IMAGE)"
    usage
fi

STAT_NAME="${NAME}_stat"
OUTFILE="monitor_${STAT_NAME}.csv"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

echo "=========================================="
echo "容器名称: $NAME"
echo "镜像: $IMAGE_NAME"
echo "端口: $PORT"
echo "监控时长: ${DURATION}s"
echo "=========================================="

# =============================================
# 监控函数
# =============================================
start_monitor() {
    local CID=$1
    
    # 查容器完整ID和PID
    FULL_CID=$(docker inspect --format '{{.Id}}' $CID 2>/dev/null)
    PID=$(docker inspect --format '{{.State.Pid}}' $CID 2>/dev/null)

    if [ -z "$PID" ] || [ "$PID" = "<no value>" ]; then
        echo "Error: container not found: $CID"
        return 1
    fi

    echo "Container PID = $PID"
    echo "Container Full ID = $FULL_CID"

    # 查 veth 网络接口
    VETH=""
    for iface in /sys/class/net/veth*; do
        if [ -d "$iface" ]; then
            VETH=$(basename "$iface")
            break
        fi
    done
    if [ -z "$VETH" ]; then
        VETH=$(docker exec $CID cat /sys/class/net/eth0/iflink 2>/dev/null | xargs -I {} grep -l {} /sys/class/net/veth*/ifindex 2>/dev/null | head -1 | xargs dirname 2>/dev/null | xargs basename 2>/dev/null)
    fi
    if [ -z "$VETH" ]; then
        VETH="docker0"
    fi
    echo "Network interface = $VETH"

    # CSV 表头
    echo "timestamp,cpu%,mem_mb,read_kb/s,write_kb/s,recv_kb/s,send_kb/s,ctx_switch,load1,threads" > $OUTFILE
    echo "Output => $OUTFILE"

    # 初始 IO 统计
    OLD_READ=$(awk '/^read_bytes:/ {print $2}' /proc/$PID/io 2>/dev/null | tr -d '\n' || true)
    OLD_WRITE=$(awk '/^write_bytes:/ {print $2}' /proc/$PID/io 2>/dev/null | tr -d '\n' || true)
    : ${OLD_READ:=0}
    : ${OLD_WRITE:=0}

    # 初始网络统计
    NET_OLD_RX=$(cat /sys/class/net/$VETH/statistics/rx_bytes 2>/dev/null || echo 0)
    NET_OLD_TX=$(cat /sys/class/net/$VETH/statistics/tx_bytes 2>/dev/null || echo 0)

    # 初始 CPU
    OLD_CPU=$(cat /sys/fs/cgroup/system.slice/docker-${FULL_CID}.scope/cpu.stat 2>/dev/null | awk '/usage_usec/ {print $2}')
    OLD_CPU=${OLD_CPU:-0}

    local elapsed=0
    while [ $elapsed -lt $DURATION ] && [ -d /proc/$PID ]; do
        TS=$(date +%s)

        # CPU
        NEW_CPU=$(cat /sys/fs/cgroup/system.slice/docker-${FULL_CID}.scope/cpu.stat 2>/dev/null | awk '/usage_usec/ {print $2}')
        NEW_CPU=${NEW_CPU:-0}
        CPU_DELTA=$((NEW_CPU - OLD_CPU))
        CPU_PERCENT=$(awk "BEGIN {printf \"%.2f\", $CPU_DELTA / ($INTERVAL * 10000)}")
        OLD_CPU=$NEW_CPU

        # MEM
        MEM=$(cat /sys/fs/cgroup/system.slice/docker-${FULL_CID}.scope/memory.current 2>/dev/null || echo 0)
        MEM_MB=$((MEM / 1024 / 1024))

        # IO
        READ=$(awk '/^read_bytes:/ {print $2}' /proc/$PID/io 2>/dev/null | tr -d '\n' || true)
        WRITE=$(awk '/^write_bytes:/ {print $2}' /proc/$PID/io 2>/dev/null | tr -d '\n' || true)
        : ${READ:=0}
        : ${WRITE:=0}
        READ_KB=$(( (READ - OLD_READ) / 1024 / INTERVAL ))
        WRITE_KB=$(( (WRITE - OLD_WRITE) / 1024 / INTERVAL ))
        OLD_READ=$READ
        OLD_WRITE=$WRITE

        # 网络
        RX=$(cat /sys/class/net/$VETH/statistics/rx_bytes 2>/dev/null || echo 0)
        TX=$(cat /sys/class/net/$VETH/statistics/tx_bytes 2>/dev/null || echo 0)
        RECV_KB=$(( (RX - NET_OLD_RX) / 1024 / INTERVAL ))
        SEND_KB=$(( (TX - NET_OLD_TX) / 1024 / INTERVAL ))
        NET_OLD_RX=$RX
        NET_OLD_TX=$TX

        # 上下文切换
        CTX=$(grep ctxt /proc/$PID/task/*/status 2>/dev/null | awk '{sum += $2} END {print sum}')
        CTX=${CTX:-0}

        # 系统负载
        LOAD1=$(uptime | awk -F'load average:' '{print $2}' | cut -d',' -f1 | tr -d ' ')

        # 线程数量
        THREADS=$(grep "^Threads:" /proc/$PID/status 2>/dev/null | awk '{print $2}' || echo 0)
        THREADS=${THREADS:-0}

        # 写入 CSV
        echo "$TS,$CPU_PERCENT,$MEM_MB,$READ_KB,$WRITE_KB,$RECV_KB,$SEND_KB,$CTX,$LOAD1,$THREADS" >> $OUTFILE

        sleep $INTERVAL
        elapsed=$((elapsed + INTERVAL))
    done

    echo "监控完成，共 ${elapsed}s"
}

cleanup() {
    echo "清理中..."
    docker stop "$NAME" 2>/dev/null || true
    docker rm "$NAME" 2>/dev/null || true
    echo "容器已停止并移除"
}

# =============================================
# 打印系统信息
# =============================================
print_system_info() {
    echo "=========================================="
    echo "运行环境信息"
    echo "=========================================="
    
    # CPU 信息
    CPU_CORES=$(nproc 2>/dev/null || echo "N/A")
    CPU_MODEL=$(grep -m1 "model name" /proc/cpuinfo 2>/dev/null | cut -d: -f2 | sed 's/^[ \t]*//' || echo "N/A")
    CPU_FREQ=$(grep -m1 "cpu MHz" /proc/cpuinfo 2>/dev/null | cut -d: -f2 | awk '{printf "%.0f", $1}' || echo "N/A")
    echo "CPU: ${CPU_CORES} 核心, ${CPU_MODEL}"
    if [ "$CPU_FREQ" != "N/A" ]; then
        echo "     频率: ${CPU_FREQ} MHz"
    fi
    
    # 内存信息
    MEM_TOTAL=$(grep MemTotal /proc/meminfo 2>/dev/null | awk '{printf "%.1f", $2/1024/1024}' || echo "N/A")
    MEM_AVAIL=$(grep MemAvailable /proc/meminfo 2>/dev/null | awk '{printf "%.1f", $2/1024/1024}' || echo "N/A")
    if [ "$MEM_TOTAL" != "N/A" ]; then
        echo "内存: 总计 ${MEM_TOTAL} GB, 可用 ${MEM_AVAIL} GB"
    else
        echo "内存: N/A"
    fi
    
    echo "=========================================="
}

trap cleanup EXIT

# 1. 启动容器
echo "[1/4] 启动容器..."
docker run -d --name "${NAME}" -p "${PORT}:${PORT}" "${ENV_VARS[@]}" "${IMAGE_NAME}"
sleep 2

# 2-3. 启动监控并等待
echo "[2/4] 启动监控..."
echo "[3/4] 监控中，持续 ${DURATION} 秒..."
start_monitor "${NAME}"

# 4. 停止容器
echo "[4/4] 停止容器..."
docker stop "$NAME"
docker logs -n 100 "$NAME"
docker rm "$NAME"
trap - EXIT

# 5. 打印系统信息
print_system_info

# 6. 分析结果
if [ -f "$OUTFILE" ]; then
    echo "分析监控结果..."
    if [ -f "${SCRIPT_DIR}/analyze_csv.py" ]; then
        python3 "${SCRIPT_DIR}/analyze_csv.py" "$OUTFILE"
    else
        echo "警告: 未找到 analyze_csv.py，跳过分析"
        echo "监控数据已保存到: $OUTFILE"
    fi
else
    echo "警告: 未找到监控文件 $OUTFILE"
fi

echo "完成!"
