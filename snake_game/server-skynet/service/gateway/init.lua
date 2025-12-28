local skynet = require "skynet"
local socket = require "skynet.socket"
---@type PomeloProtocol
local protocol = require "pomelo_protocol"
local s = require "service"
local runconfig = require "runconfig"

local sessions = {}

function s.init()
    local mynode = skynet.getenv("node")
    local nodeCnf = runconfig[mynode]
    local port = nodeCnf.gateway[s.id].port
    
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

function s.resp.send(source, fd, msg)
    local sess = sessions[fd]
    if sess == nil then
        return
    end

    socket.write(fd, msg)
end

s.start(...)
