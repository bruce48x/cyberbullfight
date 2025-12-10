#!/bin/bash
set -e

apt update
apt upgrade -y
apt install -y zsh
apt install -y vim
apt install -y wget
apt install -y curl
apt install -y git
apt autoremove -y

passwd -d ecs-user

echo "ssh 设置"
sed -i "s/^#.*ClientAliveInterval.*/ClientAliveInterval 600/" /etc/ssh/sshd_config
sed -i "s/^#.*ClientAliveCountMax.*/ClientAliveCountMax 3/" /etc/ssh/sshd_config
sed -i "s/^#.*PubkeyAuthentication.*/PubkeyAuthentication yes/" /etc/ssh/sshd_config
sed -i "s/.*PasswordAuthentication.*/PasswordAuthentication no/" /etc/ssh/sshd_config
sed -i "s/.*PermitRootLogin.*/PermitRootLogin no/" /etc/ssh/sshd_config
systemctl reload sshd

echo "安装 oh-my-zsh"
git clone --depth 1 https://github.com/ohmyzsh/ohmyzsh.git /home/ecs-user/.oh-my-zsh
cp /home/ecs-user/.oh-my-zsh/templates/zshrc.zsh-template /home/ecs-user/.zshrc
sed -i -E 's|^# (export PATH).*|\1=$HOME/bin:/usr/local/bin:$PATH|' /home/ecs-user/.zshrc
sed -i -E 's|^(ZSH_THEME).*|\1="ys"|' /home/ecs-user/.zshrc

git clone --depth 1 https://github.com/zsh-users/zsh-completions.git /home/ecs-user/.oh-my-zsh/custom/plugins/zsh-completions
git clone --depth 1 https://github.com/zsh-users/zsh-autosuggestions.git /home/ecs-user/.oh-my-zsh/custom/plugins/zsh-autosuggestions
git clone --depth 1 https://github.com/zsh-users/zsh-syntax-highlighting.git /home/ecs-user/.oh-my-zsh/custom/plugins/zsh-syntax-highlighting
sed -i "s/plugins=(git)/plugins=(git zsh-completions zsh-autosuggestions zsh-syntax-highlighting)/" /home/ecs-user/.zshrc

chown -R ecs-user:ecs-user /home/ecs-user/.oh-my-zsh
chown ecs-user:ecs-user /home/ecs-user/.zshrc

echo "安装 docker"
curl -fsSL https://get.docker.com | sh
usermod -aG docker ecs-user

echo '设置 zsh 为默认 shell'
su ecs-user -c "chsh -s $(which zsh)"

# 提前拉取镜像
docker pull "${DOCKER_MIRROR}"

echo "完成"
