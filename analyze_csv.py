#!/usr/bin/env python3
import csv
import sys
import glob
from statistics import mean, median

def read_csv(path):
    rows = []
    with open(path) as f:
        reader = csv.DictReader(f)
        for r in reader:
            # 转成 float
            for k in r:
                try:
                    r[k] = float(r[k])
                except:
                    pass
            rows.append(r)
    return rows

def percentile(arr, p):
    if not arr:
        return 0
    arr = sorted(arr)
    k = int(len(arr) * p)
    return arr[min(k, len(arr)-1)]

def analyze(filepath):
    rows = read_csv(filepath)
    cpu = [r["cpu%"] for r in rows]
    mem = [r["mem_mb"] for r in rows]
    readio = [r["read_kb/s"] for r in rows]
    writeio = [r["write_kb/s"] for r in rows]
    recv = [r["recv_kb/s"] for r in rows]
    send = [r["send_kb/s"] for r in rows]
    ctx = [r["ctx_switch"] for r in rows]
    threads = [r.get("threads", 0) for r in rows]

    result = {
        "cpu_avg": mean(cpu),
        "cpu_max": max(cpu),
        "cpu_p99": percentile(cpu, 0.99),

        "mem_avg": mean(mem),
        "mem_max": max(mem),

        "read_avg": mean(readio),
        "write_avg": mean(writeio),

        "recv_avg": mean(recv),
        "send_avg": mean(send),

        "ctx_avg": mean(ctx),
        "ctx_max": max(ctx),

        "threads_avg": mean(threads) if threads else 0,
        "threads_max": max(threads) if threads else 0,
        "threads_min": min(threads) if threads else 0,
    }
    return result

def summarize(name, stat):
    return f"""
{name}:
  CPU：平均 {stat['cpu_avg']:.1f}%，峰值 {stat['cpu_max']:.1f}%，P99 {stat['cpu_p99']:.1f}%
  内存：平均 {stat['mem_avg']:.1f} MB，峰值 {stat['mem_max']:.1f} MB
  磁盘：读 {stat['read_avg']:.1f} KB/s，写 {stat['write_avg']:.1f} KB/s
  网络：收 {stat['recv_avg']:.1f} KB/s，发 {stat['send_avg']:.1f} KB/s
  上下文切换：平均 {stat['ctx_avg']:.1f} 次/s，峰值 {stat['ctx_max']:.1f} 次/s
  线程数：平均 {stat['threads_avg']:.1f}，最小 {stat['threads_min']:.0f}，最大 {stat['threads_max']:.0f}
"""

def main():
    files = glob.glob("monitor_*.csv")
    if not files:
        print("没有找到 monitor_*.csv 文件")
        return

    all_stats = {}
    for f in files:
        name = f.replace("monitor_", "").replace(".csv", "")
        stats = analyze(f)
        all_stats[name] = stats

    # 打印简洁对比报告
    print("\n=== 性能测试结果汇总 ===\n")
    for name, stat in all_stats.items():
        print(summarize(name, stat))

    # 自动生成结论（选择最优）
    print("\n=== 结论（自动推断） ===")
    # 选 CPU 最省
    best_cpu = min(all_stats.items(), key=lambda x: x[1]["cpu_avg"])
    # 选 内存最省
    best_mem = min(all_stats.items(), key=lambda x: x[1]["mem_avg"])
    # 选 上下文切换最少（通常代表锁竞争低）
    best_ctx = min(all_stats.items(), key=lambda x: x[1]["ctx_avg"])

    print(f"CPU 利用率最低：{best_cpu[0]}（平均 {best_cpu[1]['cpu_avg']:.1f}%）")
    print(f"内存占用最低：{best_mem[0]}（平均 {best_mem[1]['mem_avg']:.1f} MB）")
    print(f"锁竞争最少（ctx 切换最低）：{best_ctx[0]}（平均 {best_ctx[1]['ctx_avg']:.1f} 次/s）")

    print("\n分析完毕。")


if __name__ == "__main__":
    main()
