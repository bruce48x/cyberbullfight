-- Snake game gate service
-- Handles TCP connections and protocol

local skynet = require "skynet"
local socket = require "skynet.socket"
local json = require "snake.json"
local protocol = require "snake.protocol"
local game = require "snake.game"

local CMD = {}

local snake_service
local gate
local connections = {} -- [fd] = {player_id, buffer, state, player}
local next_player_id = 1

-- Connection states
local STATE_HANDSHAKE = 1
local STATE_HANDSHAKE_ACK = 2
local STATE_NORMAL = 3

function CMD.start()
    snake_service = skynet.newservice("snake")
    skynet.call(snake_service, "lua", "start")
    
    gate = skynet.newservice("gate")
    skynet.call(gate, "lua", "open", {
        watchdog = skynet.self(),
        address = "0.0.0.0",
        port = 5000,
        maxclient = 1024,
        nodelay = true,
    })
    
    skynet.error("Snake server listening on 0.0.0.0:5000")
end

function CMD.socket_open(fd, addr)
    connections[fd] = {
        player_id = nil,
        buffer = "",
        state = STATE_HANDSHAKE,
        player = nil,
    }
    skynet.call(gate, "lua", "accept", skynet.self(), fd)
end

function CMD.socket_close(fd)
    local conn = connections[fd]
    if conn and conn.player_id then
        skynet.send(snake_service, "lua", "remove_player", conn.player_id)
    end
    connections[fd] = nil
end

function CMD.socket_data(fd, data)
    local conn = connections[fd]
    if not conn then
        return
    end
    
    conn.buffer = conn.buffer .. data
    
    while true do
        local pkg, new_offset = protocol.decode_package(conn.buffer)
        if not pkg then
            break
        end
        
        conn.buffer = string.sub(conn.buffer, new_offset)
        
        if conn.state == STATE_HANDSHAKE then
            handle_handshake(fd, conn, pkg)
        elseif conn.state == STATE_HANDSHAKE_ACK then
            handle_handshake_ack(fd, conn, pkg)
        elseif conn.state == STATE_NORMAL then
            handle_normal(fd, conn, pkg)
        end
    end
end

function CMD.socket_error(fd, msg)
    CMD.socket_close(fd)
end

function CMD.send(fd, data)
    socket.write(fd, data)
end

local function handle_handshake(fd, conn, pkg)
    if pkg.type ~= protocol.PACKAGE_TYPE.HANDSHAKE then
        skynet.error(string.format("Unexpected package type during handshake: %d", pkg.type))
        skynet.call(gate, "lua", "kick", skynet.self(), fd)
        return
    end
    
    -- Parse handshake data
    local ok, handshake_data = pcall(function()
        local cjson = require "cjson"
        return cjson.decode(pkg.body)
    end)
    if not ok then
        -- Fallback: simple parsing
        handshake_data = {}
        local name_match = string.match(pkg.body, '"name"%s*:%s*"([^"]*)"')
        if name_match then
            handshake_data.name = name_match
        end
    end
    local player_name = handshake_data and handshake_data.name or nil
    
    -- Create player
    local player_id = next_player_id
    next_player_id = next_player_id + 1
    
    local player = game.new_player(player_id, player_name, fd)
    player.gate_service = skynet.self()
    conn.player_id = player_id
    conn.player = player
    conn.state = STATE_HANDSHAKE_ACK
    
    -- Send handshake response
    local handshake_response = {
        code = 200,
        sys = {
            heartbeat = 10,
            dict = {},
            protos = {
                client = {},
                server = {},
            },
        },
        user = {
            id = player_id,
            width = 32,
            height = 18,
        },
    }
    
    local response_body = json.encode(handshake_response)
    local response_pkg = protocol.encode_package(protocol.PACKAGE_TYPE.HANDSHAKE, response_body)
    socket.write(fd, response_pkg)
    
    -- Add player to snake service
    skynet.send(snake_service, "lua", "add_player", player)
end

local function handle_handshake_ack(fd, conn, pkg)
    if pkg.type ~= protocol.PACKAGE_TYPE.HANDSHAKE_ACK then
        skynet.error(string.format("Unexpected package type: %d", pkg.type))
        skynet.call(gate, "lua", "kick", skynet.self(), fd)
        return
    end
    
    conn.state = STATE_NORMAL
end

local function handle_normal(fd, conn, pkg)
    if pkg.type == protocol.PACKAGE_TYPE.HEARTBEAT then
        -- Send heartbeat response
        local heartbeat_pkg = protocol.encode_package(protocol.PACKAGE_TYPE.HEARTBEAT, nil)
        socket.write(fd, heartbeat_pkg)
    elseif pkg.type == protocol.PACKAGE_TYPE.DATA then
        local msg = protocol.decode_message(pkg.body)
        if not msg then
            return
        end
        
        if msg.type == protocol.MESSAGE_TYPE.NOTIFY and msg.route == "snake.move" then
            -- Parse move direction
            local ok, body_data = pcall(function()
                local cjson = require "cjson"
                return cjson.decode(msg.body)
            end)
            if not ok then
                -- Fallback: simple parsing
                body_data = {}
                local dir_match = string.match(msg.body, '"dir"%s*:%s*"([^"]*)"')
                if dir_match then
                    body_data.dir = dir_match
                end
            end
            if body_data and body_data.dir then
                local dir = body_data.dir
                skynet.send(snake_service, "lua", "handle_player_move", conn.player_id, dir)
            end
        end
    end
end

skynet.start(function()
    skynet.dispatch("lua", function(_, _, cmd, ...)
        local f = assert(CMD[cmd], cmd)
        skynet.ret(skynet.pack(f(...)))
    end)
    
    -- Handle socket events from gate service
    skynet.dispatch("socket", function(_, _, event, ...)
        if event == "open" then
            CMD.socket_open(...)
        elseif event == "close" then
            CMD.socket_close(...)
        elseif event == "data" then
            CMD.socket_data(...)
        elseif event == "error" then
            CMD.socket_error(...)
        end
    end)
end)

