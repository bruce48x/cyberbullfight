-- Snake game server service
-- Handles game logic: matching, rooms, game loop
local skynet = require "skynet"
local json = require "cjson"
local game = require "snake.game"
local s = require "service"

-- Configuration
local MATCH_SIZE = 2

-- State
---@type table<string, MatchPlayer>
local players = {} -- all connected players: [player_id] = player
local match_queue = game.new_match_queue(MATCH_SIZE)
local next_room_id = 1

-- Match loop: periodically check match queue and create rooms
local function match_loop()
    while true do
        skynet.sleep(10) -- 100ms

        if #match_queue.queue >= MATCH_SIZE then
            -- skynet.error(string.format("Matched %d valid players", #queue))
            -- Create new room
            local room_id = next_room_id
            next_room_id = next_room_id + 1

            -- Add players to room and collect their info
            local roomPlayers = {}
            for i = 1, MATCH_SIZE do
                -- Set player status before adding to room (equivalent to server-cs MatchLoop)
                local player = match_queue.queue[1]
                player.status = game.PLAYER_STATUS.IN_GAME

                -- Store player in players table for later reference
                players[player.player_id] = player
                table.insert(roomPlayers, player)

                table.remove(match_queue.queue, 1)
            end

            -- Create a new room service instance
            local roomService = skynet.newservice("room")
            skynet.send(roomService, "lua", "init", room_id, json.encode(roomPlayers))
            skynet.error(string.format("Room %d started with %d players", room_id, #roomPlayers))
        end
    end
    -- end
end

function s.resp.start()
    -- Ensure match queue is empty on startup (no residual players)
    match_queue.queue = {}
    -- Clear any residual players
    players = {}
    next_room_id = 1

    skynet.fork(match_loop)
    return true -- Return value for skynet.call
end

function s.resp.add_player_to_queue(source, node, player_id, name, fd)
    skynet.error(string.format("[match_loop] add_player_to_queue() source = %s, node = %s, id = %s, name = %s, fd = %s", source, node, player_id, name, fd))
    local player = game.new_match_player(node, source, player_id, name, fd)
    players[player_id] = player
    game.match_queue_enqueue(match_queue, player)
end

function s.resp.remove_player(player_id)
    local player = players[player_id]
    if player ~= nil then
        players[player_id] = nil

        -- Remove from match queue (like server-cs: if player in queue, remove from queue)
        game.match_queue_remove(match_queue, player)

        skynet.error(string.format("Player %s disconnected", player_id))
    end
end

s.start(...)
