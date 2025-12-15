-- Snake game server service
-- Handles game logic: matching, rooms, game loop

local skynet = require "skynet"
local json = require "cjson"
local protocol = require "snake.protocol"
local game = require "snake.game"

local CMD = {}

-- Configuration
local WIDTH = 32
local HEIGHT = 18
local TICK_MS = 160
local MATCH_SIZE = 2

-- State
local players = {} -- all connected players: [player_id] = player
local rooms = {} -- all rooms: [room_id] = room
local match_queue = game.new_match_queue(MATCH_SIZE)
local next_player_id = 1
local next_room_id = 1

-- Match loop: periodically check match queue and create rooms
local function match_loop()
    skynet.error("Match loop started")
    while true do
        skynet.sleep(1) -- 100ms (skynet.sleep uses centiseconds)
        
        -- Debug: check queue size (access internal queue for debugging)
        local queue_size = match_queue.queue and #match_queue.queue or 0
        if queue_size > 0 then
            skynet.error(string.format("Match queue size: %d, waiting for %d players", queue_size, MATCH_SIZE))
        end
        
        local matched_players = game.match_queue_try_match(match_queue)
        if matched_players and #matched_players > 0 then
            skynet.error(string.format("Matched %d players", #matched_players))
            
            -- Create new room
            local room_id = next_room_id
            next_room_id = next_room_id + 1
            local room = game.new_room(room_id, WIDTH, HEIGHT, TICK_MS)
            rooms[room_id] = room
            
            -- Add players to room
            -- matched_players contains references to players in the players table
            for _, player in ipairs(matched_players) do
                -- player is already a reference to players[player.id], no need to look it up again
                if player and players[player.id] == player then
                    -- Set player status before adding to room (equivalent to server-cs MatchLoop)
                    player.status = game.PLAYER_STATUS.IN_GAME
                    if game.room_add_player(room, player) then
                        skynet.error(string.format("Player %d (%s) joined room %d", player.id, player.name, room_id))
                    else
                        skynet.error(string.format("Failed to add player %d to room %d", player.id, room_id))
                    end
                else
                    skynet.error(string.format("Player %d not found or mismatch in players table", player and player.id or "nil"))
                end
            end
            
            -- Start game (equivalent to room.StartGameAsync in server-cs)
            -- Check room status and player count before starting
            if room.status == game.ROOM_STATUS.WAITING and next(room.players) then
                room.status = game.ROOM_STATUS.PLAYING
                -- Ensure food is available (room_add_player already calls EnsureFood, but we ensure it again)
                game.room_ensure_food(room)
                local initialState = game.room_get_current_state(room)
                skynet.send(skynet.self(), "lua", "broadcast_state", room_id, initialState)
                
                -- Create a new room service instance for this room
                -- Pass room_id only, room service will fetch room object via get_room
                local roomService = skynet.newservice("room")
                skynet.send(roomService, "lua", "init", room_id)
                room.room_service = roomService -- Store service reference
                
                skynet.error(string.format("Room %d started with %d players", room_id, #matched_players))
            else
                skynet.error(string.format("Failed to start room %d: status=%s, player_count=%d", 
                    room_id, room.status, room.players and (function()
                        local count = 0
                        for _ in pairs(room.players) do count = count + 1 end
                        return count
                    end)() or 0))
            end
        end
    end
end

-- Room cleanup loop: check rooms that should be closed
local function room_cleanup_loop()
    while true do
        skynet.sleep(2) -- 200ms
        
        local rooms_to_close = {}
        local players_to_rematch = {}
        
        for room_id, room in pairs(rooms) do
            if game.room_can_close(room) then
                -- Get players in room
                local player_ids = game.room_get_player_ids(room)
                for _, player_id in ipairs(player_ids) do
                    local player = players[player_id]
                    if player then
                        table.insert(players_to_rematch, player)
                        player.room_id = nil
                        player.status = game.PLAYER_STATUS.MATCHING
                        player.alive = true -- Reset state
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
        
        -- Re-add players to match queue
        for _, player in ipairs(players_to_rematch) do
            game.match_queue_enqueue(match_queue, player)
            skynet.error(string.format("Player %d (%s) returned to match queue", player.id, player.name))
        end
    end
end

function CMD.start()
    skynet.fork(match_loop)
    skynet.fork(room_cleanup_loop)
    skynet.error("match_loop service started")
    return true -- Return value for skynet.call
end

function CMD.add_player(player_id, player_name, gate_service, fd)
    skynet.error(string.format("CMD.add_player called: id=%d, name=%s, gate=%s, fd=%d", 
        player_id, player_name or "nil", tostring(gate_service), fd or -1))
    -- Create player object (fd and gate_service are stored in snake_gate, we just need reference)
    local player = game.new_player(player_id, player_name, fd)
    player.gate_service = gate_service
    players[player_id] = player
    game.match_queue_enqueue(match_queue, player)
    skynet.error(string.format("Player %d (%s) connected, joining match queue. Queue size: %d", 
        player_id, player_name, match_queue.queue and #match_queue.queue or 0))
end

function CMD.remove_player(player_id)
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

function CMD.handle_player_move(player_id, dir)
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

function CMD.get_room(room_id)
    return rooms[room_id]
end

function CMD.broadcast_state(room_id, state)
    local room = rooms[room_id]
    if not room then
        return
    end
    
    -- Encode state as JSON
    local state_json = json.encode(state)
    local state_bytes = state_json
    
    -- Create push message
    local push_msg = protocol.encode_message(0, protocol.MESSAGE_TYPE.PUSH, false, "snake.state", state_bytes)
    local data_pkg = protocol.encode_package(protocol.PACKAGE_TYPE.DATA, push_msg)
    
    -- Broadcast to all players in room
    local failed = {}
    for _, player in pairs(room.players) do
        if player.fd and player.gate_service then
            skynet.send(player.gate_service, "lua", "send", player.fd, data_pkg)
        else
            table.insert(failed, player.id)
        end
    end
    
    -- Remove failed players
    for _, player_id in ipairs(failed) do
        game.room_remove_player(room, player_id)
    end
end

skynet.start(function()
    skynet.dispatch("lua", function(session, source, cmd, ...)
        local f = assert(CMD[cmd], cmd)
        local result = f(...)
        -- Only return response if there's a session (called via skynet.call)
        if session ~= 0 then
            skynet.ret(skynet.pack(result))
        end
    end)
end)

