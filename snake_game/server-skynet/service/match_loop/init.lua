-- Snake game server service
-- Handles game logic: matching, rooms, game loop
local skynet = require "skynet"
local json = require "cjson"
local protocol = require "snake.protocol"
local game = require "snake.game"
local s = require "service"

-- Configuration
local WIDTH = 32
local HEIGHT = 18
local TICK_MS = 160
local MATCH_SIZE = 2

-- State
local players = {} -- all connected players: [player_id] = player
---@type table<integer, Room>
local rooms = {} -- all rooms: [room_id] = room
local match_queue = game.new_match_queue(MATCH_SIZE)
---@type Player[]
local queue = {}
local next_player_id = 1
local next_room_id = 1

-- Match loop: periodically check match queue and create rooms
local function match_loop()
    skynet.error("match loop started")
    while true do
        skynet.sleep(10) -- 100ms

        if #match_queue.queue >= MATCH_SIZE then
            -- skynet.error(string.format("Matched %d valid players", #queue))
            -- Create new room
            local room_id = next_room_id
            next_room_id = next_room_id + 1
            local room = game.new_room(room_id, WIDTH, HEIGHT, TICK_MS)
            rooms[room_id] = room

            -- Add players to room and collect their info
            -- for _, player in ipairs(valid_players) do
            for i = 1, MATCH_SIZE do
                -- Set player status before adding to room (equivalent to server-cs MatchLoop)
                local player = match_queue.queue[1]
                player.status = game.PLAYER_STATUS.IN_GAME
                
                -- Store player in players table for later reference
                players[player.player_id] = player
                player.room_id = room_id
                
                if game.room_add_player(room, player) then
                    skynet.error(string.format("Player %d (%s) joined room %d", player.player_id, player.name, room_id))
                else
                    skynet.error(string.format("Failed to add player %d to room %d", player.player_id, room_id))
                end
                table.remove(match_queue.queue, 1)
            end

            -- Create a new room service instance for this room
            -- Room service will create its own room object and manage game logic
            local roomService = skynet.newservice("room")
            -- Store room service reference for later use
            skynet.send(roomService, "lua", "init", room_id, skynet.self(), room)
        end
    end
    -- end
end

-- Room cleanup loop: check rooms that should be closed
local function room_cleanup_loop()
    while true do
        skynet.sleep(2) -- 200ms

        local rooms_to_close = {}
        local players_to_rematch = {}

        for room_id, room in pairs(rooms) do
            -- Check if room should be closed
            -- Room can be closed if:
            -- 1. No players in the room (all players disconnected)
            -- 2. Room status is WAITING (game ended)
            local has_players = false
            for _, player in pairs(players) do
                if player.room_id == room_id then
                    has_players = true
                    break
                end
            end

            if not has_players or room.status == game.ROOM_STATUS.WAITING then
                -- Collect players in this room
                for _, player in pairs(players) do
                    if player.room_id == room_id then
                        -- Only re-add players that have valid network connections (fd and gate_service)
                        -- This ensures we don't create games with disconnected players
                        if player.fd and player.gate_service then
                            table.insert(players_to_rematch, player)
                            player.room_id = nil
                            player.status = game.PLAYER_STATUS.MATCHING
                            player.alive = true -- Reset state
                        else
                            -- Player has no valid connection, remove them completely
                            skynet.error(string.format(
                                "Player %d (%s) has no valid connection, removing from players table", player.player_id,
                                player.name))
                            players[player.id] = nil
                        end
                    end
                end
                table.insert(rooms_to_close, room_id)
            end
        end

        -- Close rooms
        for _, room_id in ipairs(rooms_to_close) do
            rooms[room_id] = nil
            skynet.error(string.format("Room %d closed", room_id))
        end

        -- Re-add players to match queue (only those with valid connections)
        for _, player in ipairs(players_to_rematch) do
            -- Double-check that player still has valid connection before re-adding
            if player.fd and player.gate_service and players[player.id] == player then
                game.match_queue_enqueue(match_queue, player)
                skynet.error(string.format("Player %d (%s) returned to match queue", player.id, player.name))
            else
                skynet.error(string.format("Player %d (%s) lost connection, not re-adding to queue", player.id,
                    player.name))
                players[player.id] = nil
            end
        end
    end
end

function s.resp.start()
    -- Ensure match queue is empty on startup (no residual players)
    match_queue.queue = {}
    queue = {}
    -- Clear any residual players
    players = {}
    next_player_id = 1
    next_room_id = 1

    skynet.fork(match_loop)
    skynet.fork(room_cleanup_loop)
    return true -- Return value for skynet.call
end

function s.resp.add_player_to_queue(player_id, name, fd)
    skynet.error("[match_loop] add_player_to_queue() id = " .. player_id .. ", name = " .. name .. ", fd = " .. fd)
    local player = game.new_player(player_id, name, fd)
    game.match_queue_enqueue(match_queue, player)
end

function s.resp.remove_player(player_id)
    local player = players[player_id]
    if player then
        players[player_id] = nil

        -- Remove from room
        if player.room_id then
            local room = rooms[player.room_id]
            if room then
                game.room_remove_player(room, player_id)
            end
        else
            -- Remove from match queue
            game.match_queue_remove(match_queue, player)
        end

        skynet.error(string.format("Player %d disconnected", player_id))
    end
end

function s.resp.handle_player_move(player_id, dir)
    local player = players[player_id]
    if not player or not player.room_id then
        return
    end

    local room = rooms[player.room_id]
    if room and room.room_service then
        -- Delegate to room service
        skynet.send(room.room_service, "lua", "handle_player_move", player_id, dir)
    end
end

function s.resp.get_room(room_id)
    -- Return nil to avoid serialization issues with deep nested tables
    -- Room service should use command interface instead
    return nil
end

-- Notify that room game ended (called by room service)
function s.resp.room_game_ended(room_id_param)
    local room = rooms[room_id_param]
    if room then
        room.status = game.ROOM_STATUS.WAITING
    end
end

function s.resp.broadcast_state(room_id, state)
    -- Encode state as JSON
    local state_json = json.encode(state)
    local state_bytes = state_json

    -- Debug: log state JSON (first 200 chars)
    local preview = string.sub(state_json, 1, 200)
    skynet.error(string.format("[match_loop] Broadcasting state JSON (preview): %s...", preview))

    -- Create push message
    local push_msg = protocol.encode_message(0, protocol.MESSAGE_TYPE.PUSH, false, "snake.state", state_bytes)
    local data_pkg = protocol.encode_package(protocol.PACKAGE_TYPE.DATA, push_msg)

    skynet.error(string.format("[match_loop] Created data package: size=%d bytes", #data_pkg))

    -- Find all players in this room from players table
    local failed = {}
    local sent_count = 0
    for player_id, player in pairs(players) do
        if player.room_id == room_id then
            if player.fd and player.gate_service then
                skynet.error(string.format("[match_loop] Sending to player %d: gate=%s, fd=%d (type: %s)", player_id,
                    tostring(player.gate_service), player.fd, type(player.fd)))
                -- Debug: check player object
                skynet.error(string.format("[match_loop] Player %d object: id=%s, fd=%s, gate=%s", player_id,
                    tostring(player.id), tostring(player.fd), tostring(player.gate_service)))
                skynet.send(player.gate_service, "lua", "send", player.fd, data_pkg)
                sent_count = sent_count + 1
            else
                table.insert(failed, player_id)
            end
        end
    end
    skynet.error(string.format("[match_loop] Broadcast state to room %d: sent to %d players, failed: %d", room_id,
        sent_count, #failed))

    -- Remove failed players (notify room service to remove them)
    local room = rooms[room_id]
    if #failed > 0 and room and room.room_service then
        for _, player_id in ipairs(failed) do
            -- Room service will handle player removal
            skynet.send(room.room_service, "lua", "remove_player", player_id)
        end
    end
end

-- skynet.start(function()
--     skynet.dispatch("lua", function(session, source, cmd, ...)
--         local f = assert(CMD[cmd], cmd)
--         local result = f(...)
--         -- Only return response if there's a session (called via skynet.call)
--         if session ~= 0 then
--             skynet.ret(skynet.pack(result))
--         end
--     end)
-- end)
s.start(...)
