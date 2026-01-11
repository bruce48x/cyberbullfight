local skynet = require "skynet"
local json = require "cjson"
local game = require "snake.game"
local s = require "service"

local H = {
    route = "snake.move",
    ---@param sess Session
    handler = function(sess, body)
        if body and type(body) == "string" then
            local ok, move_data = pcall(json.decode, body)
            if ok and move_data and move_data.dir then
                local dir_str = tostring(move_data.dir)
                local dir = nil
                if dir_str == "Up" or dir_str == "up" then
                    dir = game.DIRECTION.UP
                elseif dir_str == "Down" or dir_str == "down" then
                    dir = game.DIRECTION.DOWN
                elseif dir_str == "Left" or dir_str == "left" then
                    dir = game.DIRECTION.LEFT
                elseif dir_str == "Right" or dir_str == "right" then
                    dir = game.DIRECTION.RIGHT
                end

                if dir and sess.roomService then
                    -- Send move command to room service through the session's stored room service address
                    -- sess.roomService was set when player joined the room
                    skynet.send(sess.roomService, "lua", "handle_player_move", sess.player_id, dir)
                end
            end
        end
        return {
            code = 0,
        }
    end
}

return H
