local skynet = require "skynet"
local socket = require "skynet.socket"
---@type PomeloProtocol
local protocol = require "pomelo_protocol"

local sessions = {}

local CMD = {}

function CMD.start(source)
    local port = 5000
    local listenfd = socket.listen("0.0.0.0", port)
    skynet.error("listen on port :" .. port .. ", fd: " .. listenfd)

    socket.start(listenfd, function(fd, addr)
        skynet.error("client connected. fd: " .. fd .. ", addr: " .. addr)

        local session = skynet.newservice("session")
        sessions[fd] = session
        skynet.send(session, "lua", "start", fd)
    end)
    
    return true -- Return value for skynet.call
end

skynet.start(function()
    skynet.dispatch("lua", function(session, source, cmd, ...)
        local f = assert(CMD[cmd])
        local result = f(source, ...)
        -- Only return response if there's a session (called via skynet.call)
        if session ~= 0 then
            skynet.ret(skynet.pack(result))
        end
    end)
end)
