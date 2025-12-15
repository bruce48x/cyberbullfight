-- Room service
-- Each room service instance manages one room's game loop
-- Equivalent to Room.GameLoopAsync in server-cs

local skynet = require "skynet"
local json = require "cjson"
local protocol = require "snake.protocol"
local game = require "snake.game"

local CMD = {}

-- Room data
local room_id = nil
local matchLoopService = nil

-- Helper function to get room object from match_loop
local function get_room()
    if not matchLoopService then
        matchLoopService = skynet.uniqueservice("match_loop")
    end
    return skynet.call(matchLoopService, "lua", "get_room", room_id)
end

-- Game loop: advance world and broadcast state (equivalent to Room.GameLoopAsync in server-cs)
local function game_loop()
    while true do
        local room = get_room()
        if not room then
            skynet.error(string.format("Room %d not found, stopping game loop", room_id))
            break
        end
        
        local tick_ms = room.tick_ms or 160
        skynet.sleep(math.ceil(tick_ms / 10)) -- Convert ms to centiseconds
        
        -- Get room again (it might have been updated)
        room = get_room()
        if not room then
            break
        end
        
        local state = nil
        do
            -- Check if room still exists and is playing
            if room.status ~= game.ROOM_STATUS.PLAYING then
                break
            end
            
            -- Advance world
            game.room_advance_world(room)
            
            -- Get room again after advance_world (it might have been updated)
            room = get_room()
            if not room then
                break
            end
            
            -- Check game end conditions
            local alive_count = 0
            local alive_players = {}
            for _, player in pairs(room.players) do
                if player.alive then
                    alive_count = alive_count + 1
                    table.insert(alive_players, player)
                end
            end
            
            -- If only one player alive, that player wins
            if alive_count == 1 then
                local winner = alive_players[1]
                skynet.error(string.format("Room %d: Player %d (%s) wins with score %d!", room_id, winner.id, winner.name, winner.score))
                room.status = game.ROOM_STATUS.WAITING
                state = game.room_get_current_state(room)
            -- If all players dead, game over
            elseif alive_count == 0 then
                skynet.error(string.format("Room %d: All players dead, game over.", room_id))
                room.status = game.ROOM_STATUS.WAITING
                state = game.room_get_current_state(room)
            else
                -- Game continues, broadcast normal state
                state = game.room_get_current_state(room)
            end
        end
        
        -- Broadcast state (outside lock, equivalent to server-cs pattern)
        if state then
            if not matchLoopService then
                matchLoopService = skynet.uniqueservice("match_loop")
            end
            skynet.send(matchLoopService, "lua", "broadcast_state", room_id, state)
        end
        
        -- Check room status again
        room = get_room()
        if not room or room.status ~= game.ROOM_STATUS.PLAYING then
            break
        end
    end
end

-- Initialize room service with room data
function CMD.init(room_id_param)
    room_id = room_id_param
    matchLoopService = skynet.uniqueservice("match_loop")
    
    -- Verify room exists
    local room = get_room()
    if not room then
        skynet.error(string.format("Failed to get room %d from match_loop", room_id))
        return
    end
    
    -- Start game loop in a separate coroutine
    skynet.fork(game_loop)
    
    skynet.error(string.format("Room service %d started for room %d", skynet.self(), room_id))
end

-- Handle player move (delegated from match_loop)
function CMD.handle_player_move(player_id, dir)
    local room = get_room()
    if room then
        game.room_handle_player_move(room, player_id, dir)
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

