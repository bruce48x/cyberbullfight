local M = {}

-- Helper functions for binary data
function M.string_to_bytes(str)
    local bytes = {}
    for i = 1, #str do
        bytes[i] = string.byte(str, i)
    end
    return bytes
end

function M.bytes_to_string(bytes)
    local str = ""
    for i = 1, #bytes do
        str = str .. string.char(bytes[i])
    end
    return str
end

function M.int32_to_bytes(n)
    local bytes = {}
    bytes[1] = math.floor((n / (256 * 256 * 256)) % 256)
    bytes[2] = math.floor((n / (256 * 256)) % 256)
    bytes[3] = math.floor((n / 256) % 256)
    bytes[4] = n % 256
    return bytes
end

function M.bytes_to_int32(bytes, offset)
    offset = offset or 1
    return bytes[offset] * 256 * 256 * 256 + bytes[offset + 1] * 256 * 256 + bytes[offset + 2] * 256 + bytes[offset + 3]
end

function M.int24_to_bytes(n)
    local bytes = {}
    bytes[1] = math.floor((n / (256 * 256)) % 256)
    bytes[2] = math.floor((n / 256) % 256)
    bytes[3] = n % 256
    return bytes
end

function M.bytes_to_int24(bytes, offset)
    offset = offset or 1
    return bytes[offset] * 256 * 256 + bytes[offset + 1] * 256 + bytes[offset + 2]
end

return M
