-- Room service
-- Each room service instance manages one room's game loop
-- Equivalent to Room.GameLoopAsync in server-cs
local skynet = require "skynet"
local json = require "cjson"
local protocol = require "snake.protocol"
local game = require "snake.game"
local s = require "service"

-- Room data
local room = nil
local matchLoopService = nil

-- Game loop: advance world and broadcast state (equivalent to Room.GameLoopAsync in server-cs)
local function game_loop()
    while true do
        if not room then
            break
        end

        -- Get tick_ms and sleep first (like server-cs: await Task.Delay(_tick))
        local tick_ms = room.tick_ms or 160
        skynet.sleep(math.ceil(tick_ms / 10)) -- Convert ms to centiseconds

        -- Check room status after sleep
        if room.status ~= game.ROOM_STATUS.PLAYING then
            break
        end

        -- Advance world
        game.room_advance_world(room)

        -- Check game end conditions after advancing world
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
            skynet.error(string.format("Room %d: Player %d (%s) wins with score %d!", room.room_id, winner.id,
                winner.name, winner.score))
            room.status = game.ROOM_STATUS.WAITING
            -- Notify match_loop that game ended
            skynet.send(matchLoopService, "lua", "room_game_ended", room.room_id)
            -- If all players dead, game over
        elseif alive_count == 0 then
            skynet.error(string.format("Room %d: All players dead, game over.", room.room_id))
            room.status = game.ROOM_STATUS.WAITING
            -- Notify match_loop that game ended
            skynet.send(matchLoopService, "lua", "room_game_ended", room.room_id)
        end

        -- Get current state
        local state = game.room_get_current_state(room)
        if not state then
            break
        end

        -- Debug: log state info
        local alive_count = 0
        for _, player in pairs(room.players) do
            if player.alive then
                alive_count = alive_count + 1
            end
        end
        skynet.error(string.format("Room %d: Broadcasting state - alive players: %d, state players count: %d",
            room.room_id, alive_count, #state.players))

        -- Broadcast state (delegate to match_loop for player socket access)
        skynet.send(matchLoopService, "lua", "broadcast_state", room.room_id, state)

        -- Check room status again
        if room.status ~= game.ROOM_STATUS.PLAYING then
            break
        end
    end
end

-- Initialize room service with room data
---@param room_id_param integer
---@param match_loop_service string
---@param room Room
function s.resp.init(room_id_param, match_loop_service, room)
    skynet.error("[room] init() room_id = " .. room_id_param .. ", matchLoopService = " .. match_loop_service ..
                     ", room = " .. room)
    matchLoopService = match_loop_service or skynet.uniqueservice("match_loop")

    -- Register room service with match_loop (it will send room config via _init_room)
    -- skynet.send(matchLoopService, "lua", "register_room_service", room_id_param, skynet.self())

    -- Wait a bit for room config to be set
    -- skynet.sleep(1) -- 100ms

    if not room then
        skynet.error(string.format("Room service %d failed to get room config", skynet.self()))
        return
    end

    -- Start game loop in a separate coroutine
    room.status = game.ROOM_STATUS.PLAYING
    game.room_ensure_food(room)
    skynet.fork(game_loop)

    skynet.error(string.format("Room service %d started for room %d", skynet.self(), room.room_id))
end

-- Handle player move (delegated from match_loop)
function s.resp.handle_player_move(player_id, dir)
    if room then
        game.room_handle_player_move(room, player_id, dir)
    end
end

-- Remove player from room
function s.resp.remove_player(player_id)
    if room then
        game.room_remove_player(room, player_id)
    end
end

s.start(...)
