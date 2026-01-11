"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
const pinus_1 = require("pinus");
const preload_1 = require("./preload");
/**
 *  捕获全局错误
 */
(0, preload_1.preload)();
/**
 * Init app for client.
 */
let app = pinus_1.pinus.createApp();
app.set('name', 'cyberbullfight');
// app configuration
app.configure('production|development', 'connector', function () {
    app.set('connectorConfig', {
        connector: pinus_1.pinus.connectors.hybridconnector,
        heartbeat: 10,
        useDict: true,
        useProtobuf: true
    });
});
// start app
app.start();
