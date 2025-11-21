local skynet = require "skynet"
local cjson = require "cjson"
local ConnectionState = require "pomelo_connection_state"
local package = require "pomelo_package"
local heartbeat = require "pomelo_heartbeat"

---@class HandshakeHandler
local HandshakeHandler = {}

---@param session Session
---@param body string
function HandshakeHandler:handleHandshake(session, body)
    -- Decode string first (strencode/strdecode)
    local json_str = body or ""

    -- Parse handshake data (JSON)
    local ok, handshake_data = pcall(function()
        return cjson.decode(json_str)
    end)
    if not ok then
        skynet.error("[protocol] Failed to decode handshake, error: " .. tostring(handshake_data))
        -- Try to continue anyway with empty data
        handshake_data = {}
    end

    -- Prepare handshake response
    local sys_data = {
        heartbeat = 10, -- seconds
        dict = {}, -- Route dictionary (empty for now)
        protos = { -- Protobuf definitions (empty for now)
            client = {},
            server = {}
        }
    }
    local response = {
        code = 200, -- RES_OK
        sys = sys_data,
        user = {} -- todo 由业务层传入自定义数据
    }

    local response_body = cjson.encode(response)
    local response_body_encoded = response_body or ""
    local response_pkg = package.encode(package.TYPE_HANDSHAKE, response_body_encoded)

    -- Store callback for sending response
    session.sendCallback(response_pkg)

    session.connState = ConnectionState.ST_WAIT_ACK

    -- Initialize heartbeat if needed
    -- TypeScript: heartbeat * 1000 (milliseconds)
    -- Skynet: skynet.timeout uses centiseconds, so heartbeat * 100
    -- Pinus default: heartbeatTimeout = heartbeatInterval * 2 (same as pinus/lib/connectors/commands/heartbeat.ts)
    if sys_data.heartbeat > 0 then
        session.heartbeatInterval = sys_data.heartbeat * 100 -- Convert seconds to centiseconds
        session.heartbeatTimeout = session.heartbeatInterval * 2 -- Default is 2x heartbeat interval (as per pinus source)
    end
end

---@param session Session
---@param cb function
function HandshakeHandler:handleHandshakeAck(session, cb)
    session.connState = ConnectionState.ST_WORKING

    -- Start heartbeat if configured
    cb()
end

return HandshakeHandler
