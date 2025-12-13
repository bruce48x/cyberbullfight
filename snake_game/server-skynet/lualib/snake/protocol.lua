-- Package and Message protocol implementation
-- Compatible with C# client

local M = {}

-- Package types
M.PACKAGE_TYPE = {
    HANDSHAKE = 1,
    HANDSHAKE_ACK = 2,
    HEARTBEAT = 3,
    DATA = 4,
    KICK = 5,
}

-- Message types
M.MESSAGE_TYPE = {
    REQUEST = 0,
    NOTIFY = 1,
    RESPONSE = 2,
    PUSH = 3,
}

-- Helper functions for binary encoding
local function write_byte(value)
    return string.char(value % 256)
end

local function write_int24(value)
    return string.char(
        math.floor(value / 65536) % 256,
        math.floor(value / 256) % 256,
        value % 256
    )
end

local function read_byte(buffer, offset)
    return string.byte(buffer, offset)
end

local function read_int24(buffer, offset)
    local b1 = string.byte(buffer, offset)
    local b2 = string.byte(buffer, offset + 1)
    local b3 = string.byte(buffer, offset + 2)
    return b1 * 65536 + b2 * 256 + b3
end

-- Encode package: [type(1 byte)][length(3 bytes)][body]
function M.encode_package(pkg_type, body)
    local body_len = body and #body or 0
    local data = write_byte(pkg_type) .. write_int24(body_len)
    if body_len > 0 then
        data = data .. body
    end
    return data
end

-- Decode package from buffer
function M.decode_package(buffer, offset)
    offset = offset or 1
    if #buffer < offset + 3 then
        return nil, offset
    end
    
    local pkg_type = read_byte(buffer, offset)
    local body_len = read_int24(buffer, offset + 1)
    
    if #buffer < offset + 3 + body_len then
        return nil, offset
    end
    
    local body = ""
    if body_len > 0 then
        body = string.sub(buffer, offset + 4, offset + 3 + body_len)
    end
    
    return {
        type = pkg_type,
        length = body_len,
        body = body,
    }, offset + 4 + body_len
end

-- Encode message: [flag(1 byte)][id(base128)][route][body]
function M.encode_message(id, msg_type, compress_route, route, body)
    local result = {}
    
    -- Encode flag: type(3 bits) << 1 | compressRoute(1 bit)
    local flag = (msg_type * 2) % 256
    if compress_route then
        flag = flag + 1
    end
    table.insert(result, string.char(flag))
    
    -- Encode id (base128, only for REQUEST/RESPONSE)
    if msg_type == M.MESSAGE_TYPE.REQUEST or msg_type == M.MESSAGE_TYPE.RESPONSE then
        local id_val = id
        repeat
            local tmp = id_val % 128
            local next = math.floor(id_val / 128)
            if next ~= 0 then
                tmp = tmp + 128
            end
            table.insert(result, string.char(tmp))
            id_val = next
        until id_val == 0
    end
    
    -- Encode route (only for REQUEST/NOTIFY/PUSH)
    if msg_type == M.MESSAGE_TYPE.REQUEST or msg_type == M.MESSAGE_TYPE.NOTIFY or msg_type == M.MESSAGE_TYPE.PUSH then
        if compress_route then
            -- Compressed route: 2 bytes (big-endian)
            table.insert(result, string.char(0, 0))
        else
            -- Full route string: 1 byte length + route string
            local route_bytes = route or ""
            table.insert(result, string.char(#route_bytes))
            if #route_bytes > 0 then
                table.insert(result, route_bytes)
            end
        end
    end
    
    -- Encode body
    if body and #body > 0 then
        table.insert(result, body)
    end
    
    return table.concat(result)
end

-- Decode message
function M.decode_message(data)
    if #data < 1 then
        return nil
    end
    
    local offset = 1
    
    -- Parse flag (1 byte)
    local flag = string.byte(data, offset)
    offset = offset + 1
    
    local compress_route = (flag % 2) == 1
    local msg_type = math.floor(flag / 2) % 8
    local compress_gzip = (math.floor(flag / 16) % 2) == 1
    
    -- Parse id (base128 encoded, only for REQUEST/RESPONSE)
    local id = 0
    if msg_type == M.MESSAGE_TYPE.REQUEST or msg_type == M.MESSAGE_TYPE.RESPONSE then
        local i = 0
        while true do
            if offset > #data then
                return nil
            end
            local m = string.byte(data, offset)
            id = id + ((m % 128) * (2 ^ (7 * i)))
            offset = offset + 1
            i = i + 1
            if m < 128 then
                break
            end
        end
    end
    
    -- Parse route (only for REQUEST/NOTIFY/PUSH)
    local route = ""
    if msg_type == M.MESSAGE_TYPE.REQUEST or msg_type == M.MESSAGE_TYPE.NOTIFY or msg_type == M.MESSAGE_TYPE.PUSH then
        if compress_route then
            -- Compressed route: 2 bytes (big-endian)
            if offset + 2 > #data then
                return nil
            end
            offset = offset + 2
        else
            -- Full route string: 1 byte length + route string
            if offset > #data then
                return nil
            end
            local route_len = string.byte(data, offset)
            offset = offset + 1
            if route_len > 0 then
                if offset + route_len > #data then
                    return nil
                end
                route = string.sub(data, offset, offset + route_len - 1)
                offset = offset + route_len
            end
        end
    end
    
    -- Read body (remaining bytes)
    local body = ""
    if offset <= #data then
        body = string.sub(data, offset)
    end
    
    return {
        id = id,
        type = msg_type,
        compress_route = compress_route,
        route = route,
        body = body,
        compress_gzip = compress_gzip,
    }
end

return M

