local skynet = require "skynet"
local socket = require "skynet.socket"
local s = require "service"
---@type PomeloProtocol
local protocol = require "pomelo_protocol"
---@type HandshakeHandler
local handshake = require "pomelo_handshake"
---@type HeartbeatHandler
local heartbeat = require "pomelo_heartbeat"
local json = require "cjson"

local mynode = skynet.getenv("node")

local runconfig = require "runconfig"
local moveHandler = require "handlers.move"

local handlers = {}
handlers[moveHandler.route] = moveHandler.handler

---@type table<number, Session>
local sessionMap = {}

local function recv_loop(fd)
    socket.start(fd)

    ---@type Session
    local session = {}
    sessionMap[fd] = session;
    session.fd = fd
    session.heartbeatTimerSeq = 0
    session.reqId = 0
    local player_id = mynode .. "_" .. fd
    local player_name = "User_" .. player_id

    -- Store player_id in session for later use (e.g., disconnect handling)
    session.player_id = player_id

    local handler = protocol.createHandler({
        session = session,
        sendCallback = function(data)
            socket.write(fd, data)
        end,
        handshakeHandler = function(session_param, body, callback)
            -- Parse handshake data to get player name
            local ok, handshake_data = pcall(json.decode, body or "{}")

            -- Prepare user data for handshake response
            local user_data = {
                id = fd,
                width = 32,
                height = 18
            }

            -- Call handshake handler with user data
            callback(user_data)

        end,
        handshakeAckHandler = function(session_param)
            -- Add player to match queue after handshake ack
            skynet.error(string.format("[session] handshakeAckHandler called, player_id=%s", tostring(player_id)))
            local game_loop_service = skynet.uniqueservice("match_loop")
            skynet.send(game_loop_service, "lua", "add_player_to_queue", mynode, player_id, player_name, fd)
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
                                 (body and (type(body) == "string" and body or json.encode(body)) or "nil"))
            end
        end,
        closeCallback = function()
            skynet.error("[main] Connection closed: fd=" .. fd)
            -- Notify match_loop to remove player (like server-cs HandleClient finally block)
            if session.player_id then
                local game_loop_service = skynet.uniqueservice("match_loop")
                skynet.send(game_loop_service, "lua", "remove_player", session.player_id)
            end
            sessionMap[fd] = nil
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
            -- Notify match_loop to remove player before closing
            if session.player_id then
                local game_loop_service = skynet.uniqueservice("match_loop")
                skynet.send(game_loop_service, "lua", "remove_player", session.player_id)
            end
            handler:close()
            sessionMap[fd] = nil
            break
        elseif type(readdata) == "string" then
            -- Only process string data
            if #readdata > 0 then
                skynet.error(string.format("[session] Received %d bytes on fd=%d", #readdata, fd))
                local result = handler:feed(readdata)
                if result == -1 then
                    -- Invalid data, close connection
                    skynet.error("[main] Invalid protocol data, closing connection: fd=" .. fd)
                    -- Notify match_loop to remove player before closing
                    if session.player_id then
                        local game_loop_service = skynet.uniqueservice("match_loop")
                        skynet.send(game_loop_service, "lua", "remove_player", session.player_id)
                    end
                    handler:close()
                    sessionMap[fd] = nil
                    break
                end
            end
        elseif readdata == nil then
            -- nil means no data and connection closed
            skynet.error("[main] Socket read nil, closing connection: fd=" .. fd)
            -- Notify match_loop to remove player before closing
            if session.player_id then
                local game_loop_service = skynet.uniqueservice("match_loop")
                skynet.send(game_loop_service, "lua", "remove_player", session.player_id)
            end
            handler:close()
            sessionMap[fd] = nil
            break
        else
            -- Unexpected type
            skynet.error("[main] Socket read unexpected type: " .. type(readdata) .. ", value: " .. tostring(readdata))
            -- Notify match_loop to remove player before closing
            if session.player_id then
                local game_loop_service = skynet.uniqueservice("match_loop")
                skynet.send(game_loop_service, "lua", "remove_player", session.player_id)
            end
            handler:close()
            sessionMap[fd] = nil
            break
        end
    end
end

function s.init()
    local mynode = skynet.getenv("node")
    local nodeCnf = runconfig[mynode]
    local port = nodeCnf.gateway[s.id].port

    local listenfd = socket.listen("0.0.0.0", port)
    skynet.error("listen on port :" .. port .. ", fd: " .. listenfd)

    socket.start(listenfd, function(fd, addr)
        skynet.error("client connected. fd: " .. fd .. ", addr: " .. addr)
        skynet.fork(recv_loop, fd)
    end)

    return true -- Return value for skynet.call
end

function s.resp.on_join_room(source, fd, roomNode, roomId)
    skynet.error(string.format("[session] on_join_room called, source=%s, fd=%s, roomNode=%s, roomId=%s", source, fd,
        roomNode, roomId))
    local sess = sessionMap[fd]
    if sess == nil then
        return
    end

    sess.roomNode = roomNode
    sess.roomService = source
    sess.roomId = roomId
end

function s.resp.on_leave_room(source, fd)
    local sess = sessionMap[fd]
    if sess == nil then
        return
    end

    sess.roomNode = nil
    sess.roomService = nil
    sess.roomId = nil
end

function s.resp.push_to_client(source, fd, msg)
    local sess = sessionMap[fd]
    if sess == nil then
        return
    end

    socket.write(fd, msg)
end

s.start(...)
