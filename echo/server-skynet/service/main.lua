local skynet = require "skynet"
local socket = require "skynet.socket"
local protocol = require "pomelo_protocol"
local cjson = require "cjson"

skynet.start(function()
    skynet.error("[main] start")

    -- 处理网络请求
    local gateway = skynet.uniqueservice("gateway")
    skynet.send(gateway, "lua", "start")
end)
