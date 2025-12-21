-- Main entry point for snake game server
-- Starts the snake_gate service

local skynet = require "skynet"

skynet.start(function()
    -- 网关
    skynet.newservice("gateway", "gateway", 1)
    skynet.newservice("gateway", "gateway", 2)
    -- 匹配
    local matchLoop = skynet.uniqueservice("match_loop")
    skynet.call(matchLoop, "lua", "start")
end)
