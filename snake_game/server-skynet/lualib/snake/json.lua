-- Simple JSON encoder/decoder for snake game
-- Compatible with C# client JSON format

local M = {}

local function escape_string(str)
    str = string.gsub(str, "\\", "\\\\")
    str = string.gsub(str, "\"", "\\\"")
    str = string.gsub(str, "\n", "\\n")
    str = string.gsub(str, "\r", "\\r")
    str = string.gsub(str, "\t", "\\t")
    return str
end

local function encode_value(value)
    local t = type(value)
    if t == "nil" then
        return "null"
    elseif t == "boolean" then
        return value and "true" or "false"
    elseif t == "number" then
        return tostring(value)
    elseif t == "string" then
        return "\"" .. escape_string(value) .. "\""
    elseif t == "table" then
        -- Check if it's an array
        local is_array = true
        local max_index = 0
        for k, v in pairs(value) do
            if type(k) ~= "number" or k ~= math.floor(k) or k < 1 then
                is_array = false
                break
            end
            if k > max_index then
                max_index = k
            end
        end
        
        if is_array and max_index > 0 then
            -- Encode as array
            local parts = {}
            for i = 1, max_index do
                table.insert(parts, encode_value(value[i]))
            end
            return "[" .. table.concat(parts, ",") .. "]"
        else
            -- Encode as object
            local parts = {}
            for k, v in pairs(value) do
                local key = type(k) == "string" and k or tostring(k)
                table.insert(parts, "\"" .. escape_string(key) .. "\":" .. encode_value(v))
            end
            return "{" .. table.concat(parts, ",") .. "}"
        end
    else
        error("Unsupported type: " .. t)
    end
end

function M.encode(value)
    return encode_value(value)
end

-- Simple JSON decoder (basic implementation)
function M.decode(str)
    -- Use a simple approach: try to parse basic JSON structures
    -- For full compatibility, we might need a more complete parser
    -- For now, we'll use a simple pattern matching approach
    
    -- Remove whitespace
    str = string.gsub(str, "%s+", "")
    
    -- This is a simplified decoder - for production use, consider a full JSON parser
    -- For now, we'll handle the specific cases we need
    
    -- Try to use cjson if available, otherwise use simple parser
    local ok, cjson = pcall(require, "cjson")
    if ok and cjson then
        return cjson.decode(str)
    end
    
    -- Fallback: very basic parser (handles simple objects and arrays)
    -- This is a minimal implementation - may need enhancement
    error("JSON decode not fully implemented - need cjson library")
end

return M

