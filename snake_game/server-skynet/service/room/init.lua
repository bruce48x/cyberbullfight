-- Room service
-- Each room service instance manages one room's game loop
-- Equivalent to Room.GameLoopAsync in server-cs
local skynet = require "skynet"
local json = require "cjson"
local game = require "snake.game"
local s = require "service"

local mynode = skynet.getenv("node")

-- Configuration
local WIDTH = 32
local HEIGHT = 18
local TICK_MS = 160

---@type Room
local room

local function broadcast_state(state)
    -- Encode state as JSON
    if room == nil then
        return
    end

    for _, player in pairs(room.players) do
        s.send(player.node, player.address, "push_to_client", player.fd, "snake.state", json.encode(state))
    end
end

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
        ---@type Player[]
        local alive_players = {}
        for _, player in pairs(room.players) do
            if player.alive then
                table.insert(alive_players, player)
            end
        end

        -- If only one player alive, that player wins
        if #alive_players == 1 then
            local winner = alive_players[1]
            skynet.error(string.format("Room %d: Player %s (%s) wins with score %d!", room.room_id, winner.player_id,
                winner.name, (winner.score or 0)))
            room.status = game.ROOM_STATUS.WAITING
        elseif #alive_players == 0 then
            skynet.error(string.format("Room %d: All players dead, game over.", room.room_id))
            room.status = game.ROOM_STATUS.WAITING
        end

        -- Get current state
        local state = game.room_get_current_state(room)
        if not state then
            break
        end
        broadcast_state(state)

        -- Check room status again
        local canClose = false
        if #alive_players == 0 then
            canClose = true
        elseif room.status == game.ROOM_STATUS.WAITING then
            canClose = true
        end
        if canClose then
            for _, player in pairs(room.players) do
                s.send(player.node, player.address, "on_leave_room", player.fd)
            end
            skynet.exit()
            break
        end
    end
end

-- Initialize room service with room data
---@param room_id integer
---@param players string
function s.resp.init(source, room_id, players)
    ---@type MatchPlayer[]
    local matchPlayers = json.decode(players)
    room = game.new_room(room_id, WIDTH, HEIGHT, TICK_MS)

    if not room then
        skynet.error(string.format("Room service %d failed to get room config", skynet.self()))
        return
    end

    for i, mp in ipairs(matchPlayers) do
        local player = game.new_player(mp.node, mp.address, mp.player_id, mp.name, mp.fd)
        if game.room_add_player(room, player) then
            s.send(player.node, player.address, "on_join_room", player.fd, mynode, room_id)
        else
            skynet.error(string.format("Failed to add player %s to room %d", player.player_id, room_id))
        end
    end

    -- Start game (like server-cs Room.StartGameAsync)
    room.status = game.ROOM_STATUS.PLAYING
    game.room_ensure_food(room)

    -- Send initial state (like server-cs StartGameAsync broadcasts initial state)
    local initialState = game.room_get_current_state(room)
    broadcast_state(initialState)

    -- Start game loop in a separate coroutine
    skynet.fork(game_loop)

    skynet.error(string.format("[room] (room_id: %d) (address: %d) started", room.room_id, skynet.self()))
end

function s.resp.handle_player_move(source, player_id, dir)
    if room ~= nil then
        game.room_handle_player_move(room, player_id, dir)
    end
end

-- Remove player from room
function s.resp.remove_player(player_id)
    if room ~= nil then
        room.players[player_id] = nil
    end
end

-- Get room status and player count (for match_loop to check if room can close)
function s.resp.get_status()
    if room ~= nil then
        local player_ids = game.room_get_player_ids(room)
        return {
            status = room.status,
            player_count = player_ids and #player_ids or 0
        }
    end
    return nil
end

-- Get player IDs in this room (like server-cs Room.GetPlayerIds)
function s.resp.get_player_ids()
    if room ~= nil then
        return game.room_get_player_ids(room)
    end
    return {}
end

s.start(...)
