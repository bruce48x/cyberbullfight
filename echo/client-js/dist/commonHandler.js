"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.commonHandler = void 0;
const Logger = require("pinusmod-logger");
Logger.configure({
    appenders: {
        console: {
            type: 'console',
        },
    },
    categories: {
        default: {
            appenders: ['console'],
            level: process.env.LOG_LEVEL || 'info',
        },
    },
});
const Protocol = require("pinusmod-protocol");
const protocol = Protocol.Protocol;
const Package = Protocol.Package;
const path = require("node:path");
const logger = Logger.getLogger('client', path.basename(__filename));
const handlers = {};
const handleHandshake = function (client, pkg) {
    if (client.netState !== 0) {
        return;
    }
    try {
        client.emit('handshake', JSON.parse(protocol.strdecode(pkg.body)));
    }
    catch (ex) {
        logger.info(ex);
        client.emit('handshake', {});
    }
};
const handleHandshakeAck = function (client, pkg) {
    if (client.netState !== 1) {
        return;
    }
    client.netState = 2;
    client.emit('heartbeat');
};
const handleHeartbeat = function (client, pkg) {
    if (client.netState !== 2) {
        return;
    }
    client.emit('heartbeat');
};
const handleData = function (client, pkg) {
    if (client.netState !== 2) {
        return;
    }
    client.emit('message', pkg);
};
const handleKick = function (client, pkg) {
    if (client.netState !== 2) {
        return;
    }
    const msg = JSON.parse(protocol.strdecode(pkg.body));
    logger.info('被踢', msg);
    client.emit('kick', msg);
};
handlers[Package.TYPE_HANDSHAKE] = handleHandshake;
handlers[Package.TYPE_HANDSHAKE_ACK] = handleHandshakeAck;
handlers[Package.TYPE_HEARTBEAT] = handleHeartbeat;
handlers[Package.TYPE_DATA] = handleData;
handlers[Package.TYPE_KICK] = handleKick;
const commonHandler = function (client, pkg) {
    const handler = handlers[pkg.type];
    if (!!handler) {
        handler(client, pkg);
        return 0;
    }
    else {
        logger.error('could not find handle invalid data package.', pkg, new Error().stack);
        client.disconnect();
        return 1;
    }
};
exports.commonHandler = commonHandler;
