---@class ConnectionState
local ConnectionState = {
    ST_INITED = 0,
    ST_WAIT_ACK = 1,
    ST_WORKING = 2,
    ST_CLOSED = 3
}

return ConnectionState