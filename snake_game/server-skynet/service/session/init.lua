local skynet = require "skynet"
local socket = require "skynet.socket"
local cjson = require "cjson"
---@type PomeloProtocol
local protocol = require "pomelo_protocol"
---@type HandshakeHandler
local handshake = require "pomelo_handshake"
---@type HeartbeatHandler
local heartbeat = require "pomelo_heartbeat"
local s = require "service"

local moveHandler = require "handlers.move"

local handlers = {}
handlers[moveHandler.route] = moveHandler.handler

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

-- Store fd per session service instance
local session_fd = nil

local function process(fd)
    socket.start(fd)

    session_fd = fd
    session.fd = fd
    session.heartbeatTimerSeq = 0
    session.reqId = 0
    local player_id = skynet.getenv("node") .. fd -- Use fd as player_id (match_loop uses fd as player_id)
    local player_name = "User_" .. player_id

    local game_loop_service = skynet.uniqueservice("match_loop")

    local handler = protocol.createHandler({
        session = session,
        sendCallback = function(data)
            socket.write(fd, data)
        end,
        handshakeHandler = function(session_param, body, callback)
            -- Parse handshake data to get player name
            local ok, handshake_data = pcall(cjson.decode, body or "{}")

            -- Prepare user data for handshake response
            local user_data = {
                id = player_id,
                width = 32,
                height = 18
            }

            -- Call handshake handler with user data
            callback(user_data)

            -- Store player_id in session for later use
            session.player_id = player_id
        end,
        handshakeAckHandler = function(session_param)
            -- Add player to match queue after handshake ack
            skynet.error(string.format("[session] handshakeAckHandler called, player_id=%s", tostring(player_id)))
            skynet.send(game_loop_service, "lua", "add_player_to_queue", player_id, player_name, fd)
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
            local handler = handlers[route]
            if handler then
                handler(session, body)
            else
                skynet.error("[main] Notify received: route=" .. route .. ", body=" ..
                    (body and (type(body) == "string" and body or cjson.encode(body)) or "nil"))
            end
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
                skynet.error(string.format("[session] Received %d bytes on fd=%d", #readdata, fd))
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

function session.handleTimeout()
    skynet.error("[main] Heartbeat timeout, closing connection: fd=" .. session.fd)
    socket.close(session.fd)
end

function s.resp.start(source, fd)
    skynet.error("session service started")
    skynet.fork(process, fd)
end

function s.resp.send(source, fd_param, data)
    -- Send data to client via socket
    -- Note: source is the caller's address, fd_param is the socket fd, data is the data to send
    if not data then
        -- If data is nil, then fd_param is actually the data and fd_param is missing
        -- This means the call was: skynet.send(..., "send", fd, data)
        -- But we received: (source, fd_param) where fd_param is actually data
        skynet.error(string.format("[session] CMD.send: missing fd_param, source=%s, fd_param type=%s",
            tostring(source), type(fd_param)))
        return
    end

    skynet.error(string.format("[session] CMD.send called: session_fd=%s, fd_param=%s, data_len=%d",
        tostring(session_fd), tostring(fd_param), type(data) == "string" and #data or 0))
    if session_fd and session_fd == fd_param then
        if type(data) == "string" then
            skynet.error(string.format("[session] Sending %d bytes to fd=%d", #data, fd_param))
            socket.write(session_fd, data)
        else
            skynet.error(string.format("[session] send failed: data is not a string, type=%s", type(data)))
        end
    else
        skynet.error(string.format("[session] send failed: fd mismatch (session_fd=%s, param=%s)",
            tostring(session_fd), tostring(fd_param)))
    end
end

s.start(...)
