local skynet = require "skynet"
local ConnectionState = require "pomelo_connection_state"
local package = require "pomelo_package"

---@class HeartbeatHandler
local HeartbeatHandler = {}

---@param session Session
function HeartbeatHandler:startHeartbeat(session)
    if not session.heartbeatInterval or session.heartbeatInterval <= 0 then
        return
    end

    -- Initialize last heartbeat time (use current time)
    session.lastHeartbeatTime = skynet.now()

    -- Start periodic heartbeat timeout check
    -- Same as pinus: each time we receive heartbeat, we reset the timeout timer
    -- Since Skynet can't cancel timeout, we use sequence number to prevent old timers
    local function checkHeartbeatTimeout(seq)
        if session.connState == ConnectionState.ST_WORKING and seq == session.heartbeatTimerSeq then
            local now = skynet.now()
            local elapsed = now - session.lastHeartbeatTime
            if elapsed >= session.heartbeatTimeout then
                skynet.error(
                    "[protocol] Heartbeat timeout, lastHeartbeatTime=" .. session.lastHeartbeatTime .. ", now=" .. now ..
                        ", elapsed=" .. elapsed .. ", timeout=" .. session.heartbeatTimeout)
                session:handleTimeout()
            else
                -- Check again after a short interval (only if this is still the current timer)
                if seq == session.heartbeatTimerSeq then
                    local checkInterval = math.max(100, math.floor(session.heartbeatTimeout / 4)) -- Check every 1/4 of timeout
                    skynet.timeout(checkInterval, function()
                        checkHeartbeatTimeout(seq)
                    end)
                end
            end
        end
    end

    -- Start timeout check (same as pinus: setTimeout with timeout duration)
    local currentSeq = session.heartbeatTimerSeq
    skynet.timeout(session.heartbeatTimeout, function()
        checkHeartbeatTimeout(currentSeq)
    end)

    -- Start sending heartbeats periodically
    local function heartbeat_loop()
        if session.connState == ConnectionState.ST_WORKING then
            -- Send heartbeat
            local heartbeat_pkg = package.encode(package.TYPE_HEARTBEAT)
            session.sendCallback(heartbeat_pkg)

            -- Schedule next heartbeat
            skynet.timeout(session.heartbeatInterval, heartbeat_loop)
        end
    end

    -- Start sending heartbeats (first heartbeat will be sent immediately)
    heartbeat_loop()
end

---@param session Session
function HeartbeatHandler:handleHeartbeat(session)

    -- Update last heartbeat time (we received a heartbeat from client)
    -- This resets the timeout timer (same as pinus: clear old timeout, set new one)
    local oldTime = session.lastHeartbeatTime
    session.lastHeartbeatTime = skynet.now()

    -- Debug: log heartbeat received
    -- skynet.error("[protocol] Heartbeat received, oldTime=" .. oldTime .. ", newTime=" .. session.lastHeartbeatTime)

    -- Send heartbeat response immediately
    local heartbeat_pkg = package.encode(package.TYPE_HEARTBEAT)
    session.sendCallback(heartbeat_pkg)

    -- Reset timeout timer by incrementing sequence and scheduling new check
    -- In Skynet we can't cancel timeout, but we use sequence to prevent old timers
    -- Same as pinus: clear old timeout, set new timeout
    session.heartbeatTimerSeq = session.heartbeatTimerSeq + 1
    local currentSeq = session.heartbeatTimerSeq
    local function checkHeartbeatTimeout(seq)
        if session.connState == ConnectionState.ST_WORKING and seq == session.heartbeatTimerSeq then
            local now = skynet.now()
            local elapsed = now - session.lastHeartbeatTime
            if elapsed >= session.heartbeatTimeout then
                skynet.error(
                    "[protocol] Heartbeat timeout, lastHeartbeatTime=" .. session.lastHeartbeatTime .. ", now=" .. now ..
                        ", elapsed=" .. elapsed .. ", timeout=" .. session.heartbeatTimeout)
                session:handleTimeout()
            end
        end
    end
    skynet.timeout(session.heartbeatTimeout, function()
        checkHeartbeatTimeout(currentSeq)
    end)
end

return HeartbeatHandler
