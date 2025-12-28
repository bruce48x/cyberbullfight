local skynet = require "skynet"
local cluster = require "skynet.cluster"

local M = {
    --类型和id
    name = "",
    id = 0,
    --回调函数
    exit = nil,
    init = nil,
    --分发方法
    resp = {},
}

-- ============= 工具函数 =============
local function traceback(err)
    skynet.error(tostring(err))
    skynet.error(debug.traceback())
end

function M.call(node, addr, ...)
    local mynode = skynet.getenv("node")
    if mynode == node then
        return skynet.call(addr, "lua", ...)
    else
        return cluster.call(node, addr, "lua", ...)
    end
end

function M.send(node, addr, ...)
    local mynode = skynet.getenv("node")
    if mynode == node then
        return skynet.send(addr, "lua", ...)
    else
        return cluster.send(node, addr, "lua", ...)
    end
end

-- ============= 分发逻辑 =============
local function dispatch(session, address, cmd, ...)
    local fun = M.resp[cmd]
    if not fun then
        skynet.ret()
        return
    end

    local ret = table.pack(xpcall(fun, traceback, address, ...))
    local isok = ret[1]

    if not isok then
        skynet.ret()
        return
    end

    skynet.retpack(table.unpack(ret, 2))
end

-- ============= 启动逻辑 =============
local function init() 
    skynet.dispatch("lua", dispatch)
    if M.init then
        M.init()
    end
end

function M.start(name, id, ...)
    M.name = name
    M.id = tonumber(id)
    skynet.start(init)
end

return M