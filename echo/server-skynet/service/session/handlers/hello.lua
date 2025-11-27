local skynet = require "skynet"
local cjson = require "cjson"

local H = {
    route = "connector.entryHandler.hello",
    handler = function(route, body)
        -- skynet.error("调用 hello handler. route: " .. route .. ", body: " .. (body and cjson.encode(body) or "nil"))
        return {
            code = 0,
            msg = body
        }
    end
}

return H
