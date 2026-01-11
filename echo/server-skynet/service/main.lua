local skynet = require "skynet"

skynet.start(function()
    skynet.error("[main] start")

    -- 处理网络请求
    local gateway = skynet.uniqueservice("gateway")
    skynet.send(gateway, "lua", "start")
end)
