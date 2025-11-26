import * as Logger from 'pinusmod-logger';
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
import * as Protocol from 'pinusmod-protocol';
const protocol = Protocol.Protocol;
const Package = Protocol.Package;
import { NetState } from './config/constants';
import * as path from 'node:path';
const logger = Logger.getLogger('client', path.basename(__filename));

const handlers = {};

const handleHandshake = function (client, pkg) {
    if (client.netState !== NetState.ST_INITED) {
        return;
    }
    try {
        client.emit('handshake', JSON.parse(protocol.strdecode(pkg.body)));
    } catch (ex) {
        logger.info(ex);
        client.emit('handshake', {});
    }
};

const handleHandshakeAck = function (client, pkg) {
    if (client.netState !== NetState.ST_WAIT_ACK) {
        return;
    }
    client.netState = NetState.ST_WORKING;
    client.emit('heartbeat');
};

const handleHeartbeat = function (client, pkg) {
    if (client.netState !== NetState.ST_WORKING) {
        return;
    }
    client.emit('heartbeat');
};

const handleData = function (client, pkg) {
    if (client.netState !== NetState.ST_WORKING) {
        return;
    }
    client.emit('message', pkg);
};

const handleKick = function (client, pkg) {
    if (client.netState !== NetState.ST_WORKING) {
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

export const commonHandler = function (client, pkg) {
    const handler = handlers[pkg.type];
    if (!!handler) {
        handler(client, pkg);
        return 0;
    } else {
        logger.error('could not find handle invalid data package.', pkg, new Error().stack);
        client.disconnect();
        return 1;
    }
};
