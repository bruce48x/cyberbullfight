-- Main entry point for snake game server
-- Starts the snake_gate service

local skynet = require "skynet"

skynet.start(function()
    -- 网关
    local gateway = skynet.newservice("gateway")
    skynet.call(gateway, "lua", "start")
    -- 匹配
    local matchLoop = skynet.uniqueservice("match_loop")
    skynet.call(matchLoop, "lua", "start")
    -- Note: Room services are created dynamically when rooms are created
end)
