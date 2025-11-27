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
async function createRobot(index) {
    const userId = Math.random().toString(36).substring(2, 15);
    const client = new pinusTcpClient_1.PinusTcpClient({
        host: process.env.SERVER_HOST || '127.0.0.1',
        port: parseInt(process.env.SERVER_PORT || '3010'),
        userId,
    });
    const maxRetries = 10;
    const retryInterval = 5000;
    let connected = false;
    for (let i = 0; i < maxRetries; i++) {
        try {
            await client.connect();
            connected = true;
            break;
        }
        catch (err) {
            logger.error(`Robot ${index} connection attempt ${i + 1}/${maxRetries} failed:`, err);
            if (i < maxRetries - 1) {
                logger.info(`Robot ${index} retrying in ${retryInterval / 1000} seconds...`);
                await new Promise((resolve) => setTimeout(resolve, retryInterval));
            }
        }
    }
    if (!connected) {
        throw new Error(`Robot ${index} failed to connect after ${maxRetries} attempts`);
    }
    client.on('connect', () => {
        logger.info(`Robot ${index} connected`);
    });
    let reqId = 1;
    setInterval(async () => {
        const msg = { data: `world${reqId}` };
        reqId++;
        const res = await client.request('connector.entryHandler.hello', msg);
        logger.info(`Robot ${index} userId: ${userId}, 发送: ${JSON.stringify(msg)}, 收到响应: ${JSON.stringify(res)}`);
    }, 1000);
}
async function main() {
    const count = parseInt(process.env.COUNT || '1');
    logger.info(`Starting ${count} robot(s)...`);
    const promises = [];
    for (let i = 0; i < count; i++) {
        promises.push(createRobot(i + 1));
    }
    await Promise.all(promises);
}
main().catch(logger.error);
