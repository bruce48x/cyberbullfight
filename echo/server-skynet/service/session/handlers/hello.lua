local skynet = require "skynet"
local cjson = require "cjson"

local H = {
    route = "connector.entryHandler.hello",
    handler = function(session, body)
        -- skynet.error("调用 hello handler. route: " .. route .. ", body: " .. (body and cjson.encode(body) or "nil"))
        session.reqId = session.reqId + 1
        body.serverReqId = session.reqId
        return {
            code = 0,
            msg = body,
        }
    end
}

return H
