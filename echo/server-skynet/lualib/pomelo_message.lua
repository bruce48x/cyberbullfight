local skynet = require "skynet"
local encoder = require "pomelo_encoder"

local PomeloMessage = {}

PomeloMessage.TYPE_REQUEST = 0
PomeloMessage.TYPE_NOTIFY = 1
PomeloMessage.TYPE_RESPONSE = 2
PomeloMessage.TYPE_PUSH = 3

-- Message encode
-- Actual pinusmod-protocol format: flag(1) + id(variable, base128) + route + body
-- flag: type(3 bits) << 1 | compressRoute(1 bit)
-- id: base128 encoded (only for REQUEST/RESPONSE)
-- route: 2 bytes (big-endian) if compressed, or 1 byte length + string if not
function PomeloMessage.encode(id, type, compressRoute, route, body)
    local msg_bytes = {}

    -- Encode flag: type(3 bits) << 1 | compressRoute(1 bit)
    local flag = (type << 1) | (compressRoute and 1 or 0)
    table.insert(msg_bytes, flag)

    -- Encode id (base128, only for REQUEST/RESPONSE)
    if type == PomeloMessage.TYPE_REQUEST or type == PomeloMessage.TYPE_RESPONSE then
        local id_val = id or 0
        repeat
            local tmp = id_val % 128
            local next = math.floor(id_val / 128)
            if next ~= 0 then
                tmp = tmp + 128
            end
            table.insert(msg_bytes, tmp)
            id_val = next
        until id_val == 0
    end

    -- Encode route (only for REQUEST/NOTIFY/PUSH)
    if type == PomeloMessage.TYPE_REQUEST or type == PomeloMessage.TYPE_NOTIFY or type == PomeloMessage.TYPE_PUSH then
        if compressRoute then
            -- Compressed route: 2 bytes (big-endian)
            local route_num = route
            if type(route) ~= "number" then
                route_num = 0
            end
            if route_num > 0xffff then
                error("route number overflow: " .. route_num)
            end
            table.insert(msg_bytes, (route_num >> 8) & 0xff)
            table.insert(msg_bytes, route_num & 0xff)
        else
            -- Full route string: 1 byte length + route string
            local route_str = route or ""
            local route_str_bytes = encoder.string_to_bytes(route_str)
            if #route_str_bytes > 255 then
                error("route string too long: " .. #route_str_bytes)
            end
            table.insert(msg_bytes, #route_str_bytes)
            for i = 1, #route_str_bytes do
                table.insert(msg_bytes, route_str_bytes[i])
            end
        end
    end

    -- Encode body
    if body then
        local body_bytes = encoder.string_to_bytes(body)
        for i = 1, #body_bytes do
            table.insert(msg_bytes, body_bytes[i])
        end
    end

    return encoder.bytes_to_string(msg_bytes)
end

-- Message decode
-- Actual pinusmod-protocol format: flag(1) + id(variable, base128) + route + body
-- flag: type(3 bits) << 1 | compressRoute(1 bit)
-- id: base128 encoded (only for REQUEST/RESPONSE)
-- route: 2 bytes (big-endian) if compressed, or 1 byte length + string if not
function PomeloMessage.decode(data)
    local bytes = encoder.string_to_bytes(data)
    local offset = 1

    -- Minimum: flag(1) = 1 byte
    if #bytes < 1 then
        skynet.error("[protocol] Message.decode: not enough bytes, have " .. #bytes .. ", need at least 1")
        return nil
    end

    -- Parse flag (1 byte)
    local flag = bytes[offset]
    offset = offset + 1

    local compress_route = flag & 0x1
    local msg_type = (flag >> 1) & 0x7
    local compress_gzip = (flag >> 4) & 0x1

    -- Parse id (base128 encoded, only for REQUEST/RESPONSE)
    local id = 0
    if msg_type == PomeloMessage.TYPE_REQUEST or msg_type == PomeloMessage.TYPE_RESPONSE then
        local m = 0
        local i = 0
        repeat
            if offset > #bytes then
                skynet.error("[protocol] Message.decode: not enough bytes for id")
                return nil
            end
            m = bytes[offset]
            id = id + ((m & 0x7f) << (7 * i))
            offset = offset + 1
            i = i + 1
        until m < 128
    end

    -- Parse route (only for REQUEST/NOTIFY/PUSH)
    local route = nil
    if msg_type == PomeloMessage.TYPE_REQUEST or msg_type == PomeloMessage.TYPE_NOTIFY or msg_type ==
        PomeloMessage.TYPE_PUSH then
        if compress_route == 1 then
            -- Compressed route: 2 bytes (big-endian)
            if offset + 1 > #bytes then
                skynet.error("[protocol] Message.decode: not enough bytes for compressed route")
                return nil
            end
            route = (bytes[offset] << 8) | bytes[offset + 1]
            offset = offset + 2
        else
            -- Full route string: 1 byte length + route string
            if offset > #bytes then
                skynet.error("[protocol] Message.decode: not enough bytes for route length")
                return nil
            end
            local route_len = bytes[offset]
            offset = offset + 1

            if route_len > 0 then
                if offset + route_len - 1 > #bytes then
                    skynet.error("[protocol] Message.decode: not enough bytes for route string")
                    return nil
                end

                local route_bytes = {}
                for i = 1, route_len do
                    route_bytes[i] = bytes[offset + i - 1]
                end
                route = encoder.bytes_to_string(route_bytes)
                offset = offset + route_len
            else
                route = ""
            end
        end
    end

    -- Read body (remaining bytes)
    local body_bytes = {}
    for i = offset, #bytes do
        table.insert(body_bytes, bytes[i])
    end
    local body = encoder.bytes_to_string(body_bytes)

    return {
        id = id,
        type = msg_type,
        compressRoute = compress_route == 1,
        route = route,
        body = body,
        compressGzip = compress_gzip == 1
    }
end

return PomeloMessage
