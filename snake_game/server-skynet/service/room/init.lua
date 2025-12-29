-- Room service
-- Each room service instance manages one room's game loop
-- Equivalent to Room.GameLoopAsync in server-cs
local skynet = require "skynet"
local json = require "cjson"
local game = require "snake.game"
local s = require "service"
local message = require "pomelo_message"
local package = require "pomelo_package"

-- Configuration
local WIDTH = 32
local HEIGHT = 18
local TICK_MS = 160

---@type Room
local room = nil

---@param room Room
local function broadcast_state(state)
    -- Encode state as JSON
    if room == nil then
        return
    end

    local state_json = json.encode(state)
    -- Create push message
    local push_msg = message.encode(0, message.TYPE_PUSH, false, "snake.state", state_json)
    local data_pkg = package.encode(package.TYPE_DATA, push_msg)

    for _, player in pairs(room.players) do
        s.send(player.node, player.address, "lua", "push_to_client", player.fd, data_pkg)
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
        local alive_count = 0
        ---@type Player[]
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
            skynet.error(string.format("Room %d: Player %s (%s) wins with score %d!", room.room_id, winner.player_id,
                winner.name, (winner.score or 0)))
            room.status = game.ROOM_STATUS.WAITING
        elseif alive_count == 0 then
            skynet.error(string.format("Room %d: All players dead, game over.", room.room_id))
            room.status = game.ROOM_STATUS.WAITING
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

        broadcast_state(state)

        -- Check room status again
        if room.status ~= game.ROOM_STATUS.PLAYING then
            break
        end
    end
end

-- Initialize room service with room data
---@param room_id integer
---@param players string
function s.resp.init(source, room_id, players)
    skynet.error("[room] init() room_id = " .. room_id .. ", room = " .. players)
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
            skynet.error(string.format("Player %s (%s) joined room %d", player.player_id, player.name, room_id))
        else
            skynet.error(string.format("Failed to add player %s to room %d", player.player_id, room_id))
        end
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
