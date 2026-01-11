local skynet = require "skynet"
local socket = require "skynet.socket"
---@type PomeloProtocol
local protocol = require "pomelo_protocol"
---@type HandshakeHandler
local handshake = require "pomelo_handshake"
---@type HeartbeatHandler
local heartbeat = require "pomelo_heartbeat"

local helloHandler = require "handlers.hello"

local handlers = {}
handlers[helloHandler.route] = helloHandler.handler

---@class Session
---@field fd number
---@field connState ConnectionState
---@field lastHeartbeatTime number 上次心跳时间
---@field heartbeatTimerSeq number 心跳定时器序列
---@field heartbeatInterval number 心跳间隔
---@field heartbeatTimeout number 心跳超时时间，单位：秒
---@field handler ProtocolHandler
---@field sendCallback function
---@field reqId number 记录总共收到多少次请求
local session = {}

local function process(fd)
    socket.start(fd)

    session.fd = fd
    session.heartbeatTimerSeq = 0
    session.reqId = 0

    local handler = protocol.createHandler({
        session = session,
        sendCallback = function(data)
            socket.write(fd, data)
        end,
        routeHandler = function(route, body)
            local handler = handlers[route]
            if handler then
                return handler(session, body)
            else
                skynet.error("[main] Unknown route: " .. route)
                return {
                    code = 404,
                    msg = "Route not found: " .. route
                }
            end
        end,
        notifyHandler = function(route, body)
            skynet.error("[main] Notify received: route=" .. route .. ", body=" ..
                             (body and cjson.encode(body) or "nil"))
        end,
        closeCallback = function()
            skynet.error("[main] Connection closed: fd=" .. fd)
            socket.close(fd)
        end
    })
    session.handler = handler

    while true do
        local readdata, remain = socket.read(fd)
        if readdata == false then
            -- false means connection closed, remain is remaining data
            if remain and #remain > 0 then
                -- Process remaining data before closing
                handler:feed(remain)
            end
            skynet.error("[main] Socket read false, closing connection: fd=" .. fd)
            handler:close()
            break
        elseif type(readdata) == "string" then
            -- Only process string data
            if #readdata > 0 then
                local result = handler:feed(readdata)
                if result == -1 then
                    -- Invalid data, close connection
                    skynet.error("[main] Invalid protocol data, closing connection: fd=" .. fd)
                    handler:close()
                    break
                end
            end
        elseif readdata == nil then
            -- nil means no data and connection closed
            skynet.error("[main] Socket read nil, closing connection: fd=" .. fd)
            handler:close()
            break
        else
            -- Unexpected type
            skynet.error("[main] Socket read unexpected type: " .. type(readdata) .. ", value: " ..
                             tostring(readdata))
            handler:close()
            break
        end
    end
end

function session.sendCallback(data)
    socket.write(session.fd, data)
end

function  session.handleTimeout()
    skynet.error("[main] Heartbeat timeout, closing connection: fd=" .. session.fd)
    socket.close(session.fd)
end

local CMD = {}

function CMD.start(source, fd)
    skynet.error("session service started")
    skynet.fork(process, fd)
end

skynet.start(function()
    skynet.dispatch("lua", function(session, source, cmd, ...)
        local f = assert(CMD[cmd])
        f(source, ...)
    end)
end)