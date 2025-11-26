local function dump(o, indent)
    if o == nil then
        return ""
    end

    indent = indent or 0
    local pad = string.rep("  ", indent)

    if type(o) == "table" then
        local s = "{\n"
        for k, v in pairs(o) do
            s = s .. pad .. "  " .. tostring(k) .. " = " .. dump(v, indent + 1) .. ",\n"
        end
        return s .. pad .. "}"
    else
        return tostring(o)
    end
end

return {
    dump = dump
}
