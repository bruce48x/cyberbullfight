# echo 测试

测试不同语言实现 pomelo 协议，对比简单 echo 接口的性能

- server-go: go 语言实现，不使用任何现成的框架
- server-pinus: 基于 pinus 框架 (node.js)
- server-skynet: 基于 skynet 框架 ( c + lua )
- server-starnet: c# 模仿 skynet 实现的 actor 模型架构
- server-sunnet: c++ 模仿 skynet 实现的 actor 模型架构

# 测试

## 第1次

客户端是 client-go ，1000 个机器人

```log
```