"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
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
const pinusTcpClient_1 = require("./pinusTcpClient");
const path = require("node:path");
const logger = Logger.getLogger('client', path.basename(__filename));
async function main() {
    const userId = Math.random().toString(36).substring(2, 15);
    const client = new pinusTcpClient_1.PinusTcpClient({
        host: '127.0.0.1',
        port: 3010,
        userId,
    });
    await client.connect();
    client.on('connect', () => {
        logger.info('connect');
    });
    setInterval(async () => {
        const msg = { data: 'world' };
        const res = await client.request('connector.entryHandler.hello', msg);
        logger.info(`userId: ${userId}, 发送: ${JSON.stringify(msg)}, 收到响应: ${JSON.stringify(res)}`);
    }, 1000);
}
main().catch(logger.error);
