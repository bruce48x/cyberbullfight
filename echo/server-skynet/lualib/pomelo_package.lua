local encoder = require "pomelo_encoder"

---@class PomeloPackage
local PomeloPackage = {}

PomeloPackage.TYPE_HANDSHAKE = 1
PomeloPackage.TYPE_HANDSHAKE_ACK = 2
PomeloPackage.TYPE_HEARTBEAT = 3
PomeloPackage.TYPE_DATA = 4
PomeloPackage.TYPE_KICK = 5

---@return string
function PomeloPackage.encode(type, body)
    local body_str = body or ""
    local body_len = #body_str

    if body_len > 0xFFFFFF then
        error("Package body too large: " .. body_len)
    end

    local head_bytes = {type}
    local len_bytes = encoder.int24_to_bytes(body_len)
    for i = 1, 3 do
        table.insert(head_bytes, len_bytes[i])
    end

    local head_str = encoder.bytes_to_string(head_bytes)
    return head_str .. body_str
end

-- Package decode
function PomeloPackage.decode(data)
    if #data < 4 then
        return nil
    end

    local bytes = encoder.string_to_bytes(data)
    local type = bytes[1]
    local length = encoder.bytes_to_int24(bytes, 2)

    if #data < 4 + length then
        return nil
    end

    local body = string.sub(data, 5, 4 + length)

    return {
        type = type,
        length = length,
        body = body,
        total_length = 4 + length
    }
end

return PomeloPackage
