local skynet = require "skynet"
local socket = require "skynet.socket"
local protocol = require "pomelo_protocol"
local cjson = require "cjson"

local roles = {}

-- Route handlers
local routeHandlers = {}

-- Register route handler
local function registerRoute(route, handler)
    routeHandlers[route] = handler
end

-- Handle route: connector.entryHandler.hello
registerRoute("connector.entryHandler.hello", function(route, body)
    skynet.error("[main] Handle route: " .. route .. ", body: " .. (body and cjson.encode(body) or "nil"))
    -- Echo handler - return the message
    return {
        code = 0,
        msg = body
    }
end)

skynet.start(function()
    skynet.error("[main] start")

    -- 处理网络请求
    local gateway = skynet.uniqueservice("gateway")
    skynet.send(gateway, "lua", "start")
end)
