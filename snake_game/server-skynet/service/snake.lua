-- Snake game server service
-- Handles game logic: matching, rooms, game loop

local skynet = require "skynet"
local json = require "snake.json"
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
    while true do
        skynet.sleep(1) -- 100ms (skynet.sleep uses centiseconds)
        
        local matched_players = game.match_queue_try_match(match_queue)
        if matched_players and #matched_players > 0 then
            -- Create new room
            local room_id = next_room_id
            next_room_id = next_room_id + 1
            local room = game.new_room(room_id, WIDTH, HEIGHT, TICK_MS)
            rooms[room_id] = room
            
            -- Add players to room
            for _, player in ipairs(matched_players) do
                local stored_player = players[player.id]
                if stored_player then
                    stored_player.status = game.PLAYER_STATUS.IN_GAME
                    game.room_add_player(room, stored_player)
                    skynet.error(string.format("Player %d (%s) joined room %d", stored_player.id, stored_player.name, room_id))
                end
            end
            
            -- Start game
            room.status = game.ROOM_STATUS.PLAYING
            local initialState = game.room_get_current_state(room)
            skynet.send(skynet.self(), "lua", "broadcast_state", room_id, initialState)
            
            -- Start game loop
            skynet.fork(function()
                game_loop(room_id)
            end)
            
            skynet.error(string.format("Room %d started with %d players", room_id, #matched_players))
        end
    end
end

-- Game loop: advance world and broadcast state
local function game_loop(room_id)
    local room = rooms[room_id]
    if not room then
        return
    end
    
    while true do
        skynet.sleep(math.ceil(room.tick_ms / 10)) -- Convert ms to centiseconds
        
        if room.status ~= game.ROOM_STATUS.PLAYING then
            break
        end
        
        -- Advance world
        game.room_advance_world(room)
        
        -- Check game end conditions
        local alive_count = 0
        local alive_players = {}
        for _, player in pairs(room.players) do
            if player.alive then
                alive_count = alive_count + 1
                table.insert(alive_players, player)
            end
        end
        
        if alive_count == 1 then
            -- One player wins
            local winner = alive_players[1]
            skynet.error(string.format("Room %d: Player %d (%s) wins with score %d!", room_id, winner.id, winner.name, winner.score))
            room.status = game.ROOM_STATUS.WAITING
        elseif alive_count == 0 then
            -- All players dead
            skynet.error(string.format("Room %d: All players dead, game over.", room_id))
            room.status = game.ROOM_STATUS.WAITING
        end
        
        -- Broadcast state
        local state = game.room_get_current_state(room)
        skynet.send(skynet.self(), "lua", "broadcast_state", room_id, state)
        
        if room.status ~= game.ROOM_STATUS.PLAYING then
            break
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
end

function CMD.add_player(player)
    players[player.id] = player
    game.match_queue_enqueue(match_queue, player)
    skynet.error(string.format("Player %d (%s) connected, joining match queue", player.id, player.name))
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
    if room then
        game.room_handle_player_move(room, player_id, dir)
    end
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
    skynet.dispatch("lua", function(_, _, cmd, ...)
        local f = assert(CMD[cmd], cmd)
        skynet.ret(skynet.pack(f(...)))
    end)
end)

