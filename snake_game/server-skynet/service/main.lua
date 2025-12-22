-- Main entry point for snake game server
-- Starts the snake_gate service
local skynet = require "skynet"
local runconfig = require "runconfig"

skynet.start(function()
    local mynode = skynet.getenv("node")
    local nodeCnf = runconfig[mynode]
    for k, v in pairs(nodeCnf) do
        if k == "matchloop" then
            -- 匹配
            local matchLoop = skynet.uniqueservice("match_loop")
            skynet.call(matchLoop, "lua", "start")
        elseif k == "gateway" then
            -- 网关
            for k2, v2 in pairs(v) do
                skynet.newservice("gateway", "gateway", tonumber(k2))
            end
        end
    end
end)
