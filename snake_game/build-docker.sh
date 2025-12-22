#!/bin/bash

# Docker 构建脚本
# 用于构建所有组件的 Docker 镜像

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# 颜色输出
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${BLUE}开始构建 Docker 镜像...${NC}\n"

# 构建函数
build_image() {
    local component=$1
    local dockerfile=$2
    local tag="snake-game-${component}:latest"
    
    echo -e "${YELLOW}构建 ${component}...${NC}"
    if [ -f "$dockerfile" ]; then
        docker build -f "$dockerfile" -t "bruce48li/$tag" "./${component}"
        echo -e "${GREEN}✓ ${component} 构建完成${NC}\n"
        docker push "bruce48li/$tag"
        echo -e "${GREEN}✓ ${component} 推送到 docker 仓库成功${NC}\n"
    else
        echo -e "${YELLOW}⚠ ${component} 的 Dockerfile 不存在，跳过${NC}\n"
    fi
}

# 构建所有组件
build_image "server-cs" "./server-cs/Dockerfile"
build_image "client-cs" "./client-cs/Dockerfile"

echo -e "${GREEN}所有镜像构建完成！${NC}"
echo -e "\n${BLUE}使用以下命令查看镜像：${NC}"
echo "docker images | grep snake-game"
echo -e "\n${BLUE}使用以下命令运行服务器：${NC}"
echo "docker run -p 5000:5000 bruce48li/snake-game-server-cs"