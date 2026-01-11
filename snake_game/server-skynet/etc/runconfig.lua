return {
    -- 集群
    cluster = {
        node1 = "127.0.0.1:7771",
        node2 = "127.0.0.1:7772",
    },
    -- 节点1
    node1 = {
        matchloop = {},
        gateway = {
            [1] = { port = 5000 },
            [2] = { port = 5001 },
        }
    },
    -- 节点2
    node2 = {
        gateway = {
            [1] = { port = 5010 },
            [2] = { port = 5011 },
        }
    },
}
