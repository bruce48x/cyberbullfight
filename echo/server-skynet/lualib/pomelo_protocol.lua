-- Pomelo/Pinus Protocol Handler for Skynet
-- Based on pinusmod-protocol
local skynet = require "skynet"
local cjson = require "cjson"
---@type HandshakeHandler
local handshake = require "pomelo_handshake"
---@type HeartbeatHandler
local heartbeat = require "pomelo_heartbeat"
local package = require "pomelo_package"
local message = require "pomelo_message"
local ConnectionState = require "pomelo_connection_state"
local encoder = require "pomelo_encoder"
local utils = require "utils"

---@class PomeloProtocol
local M = {}

-- Protocol utility functions (strencode/strdecode)
-- In pomelo/pinus protocol, strencode converts string to binary string
-- and strdecode converts binary string back to string
-- In Lua, strings are already byte sequences, so these are mostly identity functions
-- However, we need to handle UTF-8 encoding properly
function M.strencode(str)
    -- In Lua, strings are byte sequences, so we just return the string
    -- But we should ensure it's valid UTF-8
    return str or ""
end

function M.strdecode(data)
    -- In Lua, strings are byte sequences, so we just return the string
    return data or ""
end
-- Protocol state
local ProtocolState = {
    ST_HEAD = 1, -- Reading header
    ST_BODY = 2, -- Reading body
    ST_CLOSED = 3 -- Connection closed
}

-- Protocol handler class
---@class ProtocolHandler
---@field session Session
---@field sendCallback function
---@field routeHandler function
---@field notifyHandler function
---@field closeCallback function
local ProtocolHandler = {}
ProtocolHandler.__index = ProtocolHandler

---@return ProtocolHandler
function ProtocolHandler:new()
    local obj = {
        readState = ProtocolState.ST_HEAD,
        headBuffer = "",
        headOffset = 0,
        packageBuffer = nil,
        packageOffset = 0,
        packageSize = 0,
        connState = ConnectionState.ST_INITED,
        heartbeatInterval = 0,
        heartbeatTimeout = 0,
        heartbeatTimer = nil,
        heartbeatTimerSeq = 0, -- Sequence number for heartbeat timer to prevent old timers from triggering
        lastHeartbeatTime = 0, -- Last time we received a heartbeat
        dict = nil,
        abbrs = nil,
        callbacks = {}, -- For request callbacks
        reqId = 0
    }
    setmetatable(obj, ProtocolHandler)
    return obj
end

function ProtocolHandler:reset()
    self.readState = ProtocolState.ST_HEAD
    self.headBuffer = ""
    self.headOffset = 0
    self.packageBuffer = nil
    self.packageOffset = 0
    self.packageSize = 0
end

function ProtocolHandler:readHead(data, offset)
    offset = offset or 1
    if offset > #data then
        return offset
    end
    local hlen = 4 - self.headOffset
    local dlen = #data - offset + 1
    local len = math.min(hlen, dlen)
    local dend = offset + len - 1

    self.headBuffer = self.headBuffer .. string.sub(data, offset, dend)
    self.headOffset = self.headOffset + len

    if self.headOffset == 4 then
        -- Header complete
        local bytes = encoder.string_to_bytes(self.headBuffer)
        local type = bytes[1]
        local body_len = encoder.bytes_to_int24(bytes, 2)

        -- Validate type
        if type < package.TYPE_HANDSHAKE or type > package.TYPE_KICK then
            return -1 -- Invalid type
        end

        self.packageSize = 4 + body_len
        self.packageBuffer = self.headBuffer
        self.packageOffset = 4
        self.readState = ProtocolState.ST_BODY
    end

    return dend + 1
end

function ProtocolHandler:readBody(data, offset)
    offset = offset or 1
    local blen = self.packageSize - self.packageOffset
    local dlen = #data - offset + 1
    local len = math.min(blen, dlen)
    local dend = offset + len - 1

    self.packageBuffer = self.packageBuffer .. string.sub(data, offset, dend)
    self.packageOffset = self.packageOffset + len

    if self.packageOffset == self.packageSize then
        -- Package complete
        self:processPackage(self.packageBuffer)
        self:reset()
    end

    return dend + 1
end

function ProtocolHandler:processPackage(pkg_data)
    local pkg = package.decode(pkg_data) -- M.Package.decode(pkg_data)
    if not pkg then
        skynet.error("[protocol] Failed to decode package")
        return
    end

    if pkg.type == package.TYPE_HANDSHAKE then
        handshake:handleHandshake(self.session, pkg.body)
    elseif pkg.type == package.TYPE_HANDSHAKE_ACK then
        handshake:handleHandshakeAck(self.session, function()
            -- 触发心跳
            heartbeat:startHeartbeat(self.session)
        end)
    elseif pkg.type == package.TYPE_HEARTBEAT then
        heartbeat:handleHeartbeat(self.session)
    elseif pkg.type == package.TYPE_DATA then
        self:handleData(pkg.body)
    elseif pkg.type == package.TYPE_KICK then
        self:handleKick(pkg.body)
    end
end

function ProtocolHandler:handleHandshake(body)
    -- Decode string first (strencode/strdecode)
    local json_str = M.strdecode(body)

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
        heartbeat = 3, -- 3 seconds
        dict = {}, -- Route dictionary (empty for now)
        protos = { -- Protobuf definitions (empty for now)
            client = {},
            server = {}
        }
    }
    local response = {
        code = 200, -- RES_OK
        sys = sys_data,
        user = {}
    }

    local response_body = cjson.encode(response)
    local response_body_encoded = M.strencode(response_body)
    local response_pkg = package.encode(package.TYPE_HANDSHAKE, response_body_encoded) -- M.Package.encode(M.Package.TYPE_HANDSHAKE, response_body_encoded)

    -- Store callback for sending response
    self.sendCallback(response_pkg)

    self.connState = ConnectionState.ST_WAIT_ACK

    -- Initialize heartbeat if needed
    -- TypeScript: heartbeat * 1000 (milliseconds)
    -- Skynet: skynet.timeout uses centiseconds, so heartbeat * 100
    -- Pinus default: heartbeatTimeout = heartbeatInterval * 2 (same as pinus/lib/connectors/commands/heartbeat.ts)
    if sys_data.heartbeat > 0 then
        self.heartbeatInterval = sys_data.heartbeat * 100 -- Convert seconds to centiseconds
        self.heartbeatTimeout = self.heartbeatInterval * 2 -- Default is 2x heartbeat interval (as per pinus source)
    end
end

function ProtocolHandler:handleHandshakeAck()
    self.connState = ConnectionState.ST_WORKING

    -- Start heartbeat if configured
    if self.heartbeatInterval > 0 then
        self:startHeartbeat()
    end
end

function ProtocolHandler:handleHeartbeat()
    -- Respond with heartbeat immediately (same as pinus HeartbeatCommand.handle)
    if self.connState ~= ConnectionState.ST_WORKING then
        return
    end

    -- Update last heartbeat time (we received a heartbeat from client)
    -- This resets the timeout timer (same as pinus: clear old timeout, set new one)
    local oldTime = self.lastHeartbeatTime
    self.lastHeartbeatTime = skynet.now()

    -- Debug: log heartbeat received
    -- skynet.error("[protocol] Heartbeat received, oldTime=" .. oldTime .. ", newTime=" .. self.lastHeartbeatTime)

    -- Send heartbeat response immediately
    local heartbeat_pkg = package.encode(package.TYPE_HEARTBEAT) -- M.Package.encode(M.Package.TYPE_HEARTBEAT)
    self.sendCallback(heartbeat_pkg)

    -- Reset timeout timer by incrementing sequence and scheduling new check
    -- In Skynet we can't cancel timeout, but we use sequence to prevent old timers
    -- Same as pinus: clear old timeout, set new timeout
    self.heartbeatTimerSeq = self.heartbeatTimerSeq + 1
    local currentSeq = self.heartbeatTimerSeq
    local function checkHeartbeatTimeout(seq)
        if self.connState == ConnectionState.ST_WORKING and seq == self.heartbeatTimerSeq then
            local now = skynet.now()
            local elapsed = now - self.lastHeartbeatTime
            if elapsed >= self.heartbeatTimeout then
                skynet.error("[protocol] Heartbeat timeout, lastHeartbeatTime=" .. self.lastHeartbeatTime .. ", now=" ..
                                 now .. ", elapsed=" .. elapsed .. ", timeout=" .. self.heartbeatTimeout)
                self:handleTimeout()
            end
        end
    end
    skynet.timeout(self.heartbeatTimeout, function()
        checkHeartbeatTimeout(currentSeq)
    end)
end

function ProtocolHandler:startHeartbeat()
    if not self.heartbeatInterval or self.heartbeatInterval <= 0 then
        return
    end

    -- Initialize last heartbeat time (use current time)
    self.lastHeartbeatTime = skynet.now()

    -- Start periodic heartbeat timeout check
    -- Same as pinus: each time we receive heartbeat, we reset the timeout timer
    -- Since Skynet can't cancel timeout, we use sequence number to prevent old timers
    local function checkHeartbeatTimeout(seq)
        if self.connState == ConnectionState.ST_WORKING and seq == self.heartbeatTimerSeq then
            local now = skynet.now()
            local elapsed = now - self.lastHeartbeatTime
            if elapsed >= self.heartbeatTimeout then
                skynet.error("[protocol] Heartbeat timeout, lastHeartbeatTime=" .. self.lastHeartbeatTime .. ", now=" ..
                                 now .. ", elapsed=" .. elapsed .. ", timeout=" .. self.heartbeatTimeout)
                self:handleTimeout()
            else
                -- Check again after a short interval (only if this is still the current timer)
                if seq == self.heartbeatTimerSeq then
                    local checkInterval = math.max(100, math.floor(self.heartbeatTimeout / 4)) -- Check every 1/4 of timeout
                    skynet.timeout(checkInterval, function()
                        checkHeartbeatTimeout(seq)
                    end)
                end
            end
        end
    end

    -- Start timeout check (same as pinus: setTimeout with timeout duration)
    local currentSeq = self.heartbeatTimerSeq
    skynet.timeout(self.heartbeatTimeout, function()
        checkHeartbeatTimeout(currentSeq)
    end)

    -- Start sending heartbeats periodically
    local function heartbeat_loop()
        if self.connState == ConnectionState.ST_WORKING then
            -- Send heartbeat
            local heartbeat_pkg = package.encode(package.TYPE_HEARTBEAT) -- M.Package.encode(M.Package.TYPE_HEARTBEAT)
            self.sendCallback(heartbeat_pkg)

            -- Schedule next heartbeat
            skynet.timeout(self.heartbeatInterval, heartbeat_loop)
        end
    end

    -- Start sending heartbeats (first heartbeat will be sent immediately)
    heartbeat_loop()
end

function ProtocolHandler:handleTimeout()
    -- Connection timeout, close it
    skynet.error("[protocol] Connection timeout")
    self:close()
end

function ProtocolHandler:handleData(body)
    if not body or #body == 0 then
        skynet.error("[protocol] handleData: empty body")
        return
    end

    -- Receiving any data from client proves the connection is alive,
    -- so refresh heartbeat timestamp to avoid false positives when
    -- the client is actively sending requests but hasn't sent a
    -- heartbeat packet yet.
    if self.session and self.session.connState == ConnectionState.ST_WORKING then
        self.session.lastHeartbeatTime = skynet.now()
    end

    local msg = message.decode(body) -- M.Message.decode(body)
    if not msg then
        skynet.error("[protocol] Failed to decode message, body length=" .. #body)
        return
    end

    -- Decompress route if needed
    if msg.compressRoute and self.abbrs and self.abbrs[tostring(msg.route)] then
        msg.route = self.abbrs[tostring(msg.route)]
    end

    -- Decode string first (strencode/strdecode)
    local msg_body_str = M.strdecode(msg.body)

    -- Parse body (JSON)
    local msg_body
    if msg_body_str and #msg_body_str > 0 then
        local ok, decoded = pcall(cjson.decode, msg_body_str)
        if ok then
            msg_body = decoded
        else
            skynet.error("[protocol] Failed to decode message body: " .. msg_body_str)
            msg_body = {}
        end
    else
        msg_body = {}
    end

    if msg.type == message.TYPE_REQUEST then
        -- Handle request
        self:handleRequest(msg.id, msg.route, msg_body)
    elseif msg.type == message.TYPE_NOTIFY then
        -- Handle notify
        self:handleNotify(msg.route, msg_body)
    end
end

function ProtocolHandler:handleRequest(id, route, body)

    -- Call route handler
    local response_body
    if self.routeHandler then
        response_body = self.routeHandler(route, body)
    else
        -- Default echo handler
        response_body = {
            code = 0,
            msg = body
        }
    end

    -- Send response
    local response_body_str = cjson.encode(response_body)
    local response_body_encoded = M.strencode(response_body_str)
    local response_msg = message.encode(id, message.TYPE_RESPONSE, false, "", response_body_encoded) -- M.Message.encode(id, message.Message.TYPE_RESPONSE, false, "", response_body_encoded)
    local response_pkg = package.encode(package.TYPE_DATA, response_msg) -- M.Package.encode(M.Package.TYPE_DATA, response_msg)

    self.sendCallback(response_pkg)
end

function ProtocolHandler:handleNotify(route, body)

    -- Call notify handler
    if self.notifyHandler then
        self.notifyHandler(route, body)
    end
end

function ProtocolHandler:handleKick(body)
    skynet.error("[protocol] Kick received: " .. (body or ""))
    self:close()
end

function ProtocolHandler:close()
    self.connState = ConnectionState.ST_CLOSED
    self.readState = ProtocolState.ST_CLOSED
    if self.closeCallback then
        self.closeCallback()
    end
end

function ProtocolHandler:feed(data)
    -- Validate data is a string
    if type(data) ~= "string" then
        skynet.error("[protocol] feed: data is not a string, type: " .. type(data))
        return -1
    end

    if #data == 0 then
        return 0
    end

    local offset = 1
    while offset <= #data and self.readState ~= ProtocolState.ST_CLOSED do
        if self.readState == ProtocolState.ST_HEAD then
            offset = self:readHead(data, offset)
            if offset == -1 then
                return -1 -- Invalid data
            end
        elseif self.readState == ProtocolState.ST_BODY then
            offset = self:readBody(data, offset)
        end
    end
    return 0
end

-- Create protocol handler factory
function M.createHandler(opts)
    opts = opts or {}
    local handler = ProtocolHandler:new()
    handler.session = opts.session
    handler.session.heartbeatTimeout = handler.heartbeatTimeout
    handler.session.heartbeatInterval = handler.heartbeatInterval

    -- Set callbacks
    handler.sendCallback = opts.sendCallback or function(data)
        skynet.error("[protocol] No sendCallback set")
    end
    handler.routeHandler = opts.routeHandler
    handler.notifyHandler = opts.notifyHandler
    handler.closeCallback = opts.closeCallback

    return handler
end

return M

