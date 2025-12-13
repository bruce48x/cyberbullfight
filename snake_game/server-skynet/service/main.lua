-- Main entry point for snake game server
-- Starts the snake_gate service

local skynet = require "skynet"

skynet.start(function()
    local snake_gate = skynet.newservice("snake_gate")
    skynet.call(snake_gate, "lua", "start")
end)

