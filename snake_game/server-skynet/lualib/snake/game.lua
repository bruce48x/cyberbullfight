-- Game logic: Player, Room, MatchQueue
-- Compatible with C# server logic
local skynet = require "skynet"
local M = {}

-- Direction enum
M.DIRECTION = {
    UP = "Up",
    DOWN = "Down",
    LEFT = "Left",
    RIGHT = "Right"
}

-- Player status
M.PLAYER_STATUS = {
    INIT = 'Init',
    MATCHING = "Matching",
    IN_GAME = "InGame"
}

-- Room status
M.ROOM_STATUS = {
    WAITING = "Waiting",
    PLAYING = "Playing"
}

-- Position
function M.new_pos(x, y)
    return {
        x = x,
        y = y
    }
end

function M.pos_equals(a, b)
    return a.x == b.x and a.y == b.y
end

---@class Session
---@field fd number
---@field connState ConnectionState
---@field lastHeartbeatTime number 上次心跳时间
---@field heartbeatTimerSeq number 心跳定时器序列
---@field heartbeatInterval number 心跳间隔
---@field heartbeatTimeout number 心跳超时时间，单位：秒
---@field handler ProtocolHandler
---@field sendCallback function
---@field reqId number 记录总共收到多少次请求
---@field roomId number 战斗房间ID
---@field roomNode string 战斗房间节点
---@field roomService number 战斗房间服务地址
---@field player_id string
---@field player_name string

---@class MatchPlayer
---@field node string
---@field player_id string
---@field name string
---@field fd number
---@field address number
---@field status string

---@param node string
---@param addr number
---@param id string
---@param name string
---@param fd number
---@return MatchPlayer
function M.new_match_player(node, addr, id, name, fd)
    return {
        node = node,
        address = addr,
        player_id = id,
        name = name or ("Player" .. id),
        fd = fd,
        status = M.PLAYER_STATUS.INIT,
    }
end

---@class Player
---@field node string
---@field address number
---@field player_id string
---@field name string
---@field fd number
---@field alive boolean
---@field score number
---@field direction string
---@field pending string
---@field segments table
---@field status string
---@field room_id any

---@param node string
---@param addr number
---@param id string
---@param name string
---@param fd number
---@return Player
function M.new_player(node, addr, id, name, fd)
    return {
        node = node,
        address = addr,
        player_id = id,
        name = name or ("Player" .. id),
        fd = fd,
        alive = true,
        score = 0,
        direction = M.DIRECTION.RIGHT,
        pending = M.DIRECTION.RIGHT,
        segments = {}, -- linked list of positions
        status = M.PLAYER_STATUS.MATCHING,
        room_id = nil
    }
end

function M.player_to_view(player)
    local segments_list = {}
    local node = player.segments.first
    while node do
        table.insert(segments_list, {
            x = node.value.x,
            y = node.value.y
        })
        node = node.next
    end
    return {
        id = player.player_id,
        name = player.name,
        alive = player.alive,
        score = player.score,
        direction = player.direction,
        segments = segments_list
    }
end

-- Simple linked list implementation
function M.new_linked_list()
    return {
        first = nil,
        last = nil,
        count = 0
    }
end

function M.linked_list_add_first(list, value)
    local node = {
        value = value,
        prev = nil,
        next = list.first
    }
    if list.first then
        list.first.prev = node
    else
        list.last = node
    end
    list.first = node
    list.count = list.count + 1
end

function M.linked_list_add_last(list, value)
    local node = {
        value = value,
        prev = list.last,
        next = nil
    }
    if list.last then
        list.last.next = node
    else
        list.first = node
    end
    list.last = node
    list.count = list.count + 1
end

function M.linked_list_remove_last(list)
    if not list.last then
        return nil
    end
    local value = list.last.value
    if list.last.prev then
        list.last.prev.next = nil
        list.last = list.last.prev
    else
        list.first = nil
        list.last = nil
    end
    list.count = list.count - 1
    return value
end

function M.linked_list_clear(list)
    list.first = nil
    list.last = nil
    list.count = 0
end

---@class MatchQueue
---@field match_size number
---@field queue MatchPlayer[]

---@param match_size number
---@return MatchQueue
function M.new_match_queue(match_size)
    match_size = match_size or 2
    return {
        match_size = match_size,
        queue = {}
    }
end

---@param mq MatchQueue
---@param player MatchPlayer
function M.match_queue_enqueue(mq, player)
    -- Check if player already in queue
    for _, p in ipairs(mq.queue) do
        if p.player_id == player.player_id then
            return
        end
    end
    table.insert(mq.queue, player)
    player.status = M.PLAYER_STATUS.MATCHING
end

function M.match_queue_remove(mq, player)
    for i, p in ipairs(mq.queue) do
        if p.player_id == player.player_id then
            table.remove(mq.queue, i)
            return
        end
    end
end

---@class Room
---@field room_id integer
---@field width integer
---@field height integer
---@field tick_ms integer
---@field status string
---@field players Player[]
---@field foods any[]
---@field room_service integer|nil  Room service address (set by match_loop)

---@param room_id integer
---@param width integer
---@param height integer
---@param tick_ms integer
---@return Room
function M.new_room(room_id, width, height, tick_ms)
    width = width or 32
    height = height or 18
    tick_ms = tick_ms or 160

    return {
        room_id = room_id,
        width = width,
        height = height,
        tick_ms = tick_ms,
        status = M.ROOM_STATUS.WAITING,
        players = {},
        foods = {}
        -- Note: rng removed to avoid serialization issues when passing room object
        -- Code uses math.random directly instead
    }
end

---@param room Room
---@param player Player
function M.room_add_player(room, player)
    if room.status ~= M.ROOM_STATUS.WAITING then
        return false
    end

    if room.players[player.player_id] then
        return false
    end

    -- Initialize player position
    local pos = M.room_find_spawn_position(room)
    local segs = M.new_linked_list()
    M.linked_list_add_first(segs, pos)
    M.linked_list_add_last(segs, {
        x = pos.x - 1,
        y = pos.y
    })
    M.linked_list_add_last(segs, {
        x = pos.x - 2,
        y = pos.y
    })

    player.segments = segs
    player.direction = M.DIRECTION.RIGHT
    player.pending = M.DIRECTION.RIGHT
    player.alive = true
    player.score = 0
    player.room_id = room.room_id
    -- Note: player.status should be set by caller (match_loop), not here
    -- This matches server-cs behavior where Room.AddPlayer doesn't set status

    room.players[player.player_id] = player
    return true
end

function M.room_get_player_ids(room)
    local ids = {}
    for id, _ in pairs(room.players) do
        table.insert(ids, id)
    end
    return ids
end

function M.room_can_close(room)
    if not next(room.players) then
        return true
    end
    if room.status == M.ROOM_STATUS.WAITING then
        return true
    end
    return false
end

function M.room_handle_player_move(room, player_id, dir)
    skynet.error(string.format("Room handle player move: %s, %s", player_id, dir))
    local player = room.players[player_id]
    if not player then
        return
    end

    if not M.is_opposite(player.direction, dir) then
        player.pending = dir
    end
end

function M.room_get_current_state(room)
    local players_list = {}
    for _, player in pairs(room.players) do
        table.insert(players_list, M.player_to_view(player))
    end

    return {
        tick = skynet.now() * 10, -- skynet.now() returns centiseconds, convert to milliseconds
        width = room.width,
        height = room.height,
        foods = room.foods,
        players = players_list
    }
end

---@param room Room
function M.room_advance_world(room)
    M.room_ensure_food(room)
    if not next(room.players) then
        return
    end

    -- Build occupancy map
    local occupancy = {}
    for _, player in pairs(room.players) do
        if player.alive and player.segments.count > 0 then
            local node = player.segments.first
            while node do
                local key = string.format("%d,%d", node.value.x, node.value.y)
                occupancy[key] = true
                node = node.next
            end
        end
    end

    -- Move each player
    for _, player in pairs(room.players) do
        if not player.alive or player.segments.count == 0 then
            goto continue
        end

        local last_node = player.segments.last
        local first_node = player.segments.first
        if not last_node or not first_node then
            goto continue
        end

        -- Allow moving to tail since it will be freed
        local last_key = string.format("%d,%d", last_node.value.x, last_node.value.y)
        occupancy[last_key] = nil

        -- Update direction
        if not M.is_opposite(player.direction, player.pending) then
            player.direction = player.pending
        end

        -- Calculate next head position
        local next_head = M.step(first_node.value, player.direction)
        local hit_wall = next_head.x < 0 or next_head.x >= room.width or next_head.y < 0 or next_head.y >= room.height
        local next_key = string.format("%d,%d", next_head.x, next_head.y)
        local hit_body = occupancy[next_key] == true

        if hit_wall or hit_body then
            player.alive = false
            M.linked_list_clear(player.segments)
            goto continue
        end

        -- Check if ate food
        local ate = false
        for i, food in ipairs(room.foods) do
            if food.x == next_head.x and food.y == next_head.y then
                table.remove(room.foods, i)
                ate = true
                player.score = player.score + 1
                break
            end
        end

        -- Move snake
        M.linked_list_add_first(player.segments, next_head)
        if not ate then
            M.linked_list_remove_last(player.segments)
        end

        -- Add new head to occupancy immediately (like server-cs)
        -- This ensures that if two players try to move to the same position,
        -- the second one will detect the collision
        occupancy[next_key] = true

        ::continue::
    end

    M.room_ensure_food(room)
end

---@param room Room
function M.room_ensure_food(room)
    local target_food = 1
    local attempts = 0
    while #room.foods < target_food and attempts < 1000 do
        local candidate = {
            x = math.random(0, room.width - 1),
            y = math.random(0, room.height - 1)
        }

        -- Check collision with players
        local collision = false
        for _, player in pairs(room.players) do
            if player.segments.count > 0 then
                local node = player.segments.first
                while node do
                    if node.value.x == candidate.x and node.value.y == candidate.y then
                        collision = true
                        break
                    end
                    node = node.next
                end
                if collision then
                    break
                end
            end
        end

        if not collision then
            table.insert(room.foods, candidate)
        end
        attempts = attempts + 1
    end

    if attempts >= 1000 then
        -- Log warning if we couldn't find a position after many attempts
        -- This shouldn't happen in normal gameplay
    end
end

---@param room Room
function M.room_find_spawn_position(room)
    local attempts = 0
    while true do
        local pos = {
            x = math.random(2, room.width - 3),
            y = math.random(2, room.height - 3)
        }
        -- Player body: head at pos, body at (pos.x-1, pos.y), tail at (pos.x-2, pos.y)
        -- Player starts facing RIGHT, so first move will be to (pos.x+1, pos.y)
        local body = {pos, {
            x = pos.x - 1,
            y = pos.y
        }, {
            x = pos.x - 2,
            y = pos.y
        }}
        local first_move_pos = {
            x = pos.x + 1,
            y = pos.y
        }

        local collision = false
        for _, player in pairs(room.players) do
            if player.segments.count > 0 then
                local player_head = player.segments.first.value
                local player_first_move = {
                    x = player_head.x + 1,
                    y = player_head.y
                }

                -- Check collision with player's body segments
                local node = player.segments.first
                while node do
                    -- Check if our body collides with player's body
                    for _, b in ipairs(body) do
                        if node.value.x == b.x and node.value.y == b.y then
                            collision = true
                            break
                        end
                    end
                    if collision then
                        break
                    end

                    -- Check if our first move collides with player's head
                    if first_move_pos.x == player_head.x and first_move_pos.y == player_head.y then
                        collision = true
                        break
                    end

                    -- Check if our head collides with player's first move
                    if pos.x == player_first_move.x and pos.y == player_first_move.y then
                        collision = true
                        break
                    end

                    node = node.next
                end
                if collision then
                    break
                end
            end
        end

        if not collision or attempts > 100 then
            return pos
        end
        attempts = attempts + 1
    end
end

function M.step(pos, dir)
    if dir == M.DIRECTION.UP then
        return {
            x = pos.x,
            y = pos.y - 1
        }
    elseif dir == M.DIRECTION.DOWN then
        return {
            x = pos.x,
            y = pos.y + 1
        }
    elseif dir == M.DIRECTION.LEFT then
        return {
            x = pos.x - 1,
            y = pos.y
        }
    elseif dir == M.DIRECTION.RIGHT then
        return {
            x = pos.x + 1,
            y = pos.y
        }
    end
    return pos
end

function M.is_opposite(a, b)
    if a == M.DIRECTION.UP and b == M.DIRECTION.DOWN then
        return true
    elseif a == M.DIRECTION.DOWN and b == M.DIRECTION.UP then
        return true
    elseif a == M.DIRECTION.LEFT and b == M.DIRECTION.RIGHT then
        return true
    elseif a == M.DIRECTION.RIGHT and b == M.DIRECTION.LEFT then
        return true
    end
    return false
end

return M

