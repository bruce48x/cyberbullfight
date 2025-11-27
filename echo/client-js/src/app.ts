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
import { PinusTcpClient } from './pinusTcpClient';
import * as path from 'node:path';

const logger = Logger.getLogger('client', path.basename(__filename));

// 全局统计
let totalRequests = 0;
let successCount = 0;
let failCount = 0;

function printStats() {
    logger.info(`\n========== 统计信息 ==========`);
    logger.info(`总请求数: ${totalRequests}`);
    logger.info(`成功: ${successCount}`);
    logger.info(`失败: ${failCount}`);
    logger.info(`==============================\n`);
}

process.on('SIGINT', () => {
    printStats();
    process.exit(0);
});

process.on('SIGTERM', () => {
    printStats();
    process.exit(0);
});

async function createRobot(index: number) {
    const userId = Math.random().toString(36).substring(2, 15);
    const client = new PinusTcpClient({
        host: process.env.SERVER_HOST || '127.0.0.1',
        port: parseInt(process.env.SERVER_PORT || '3010'),
        userId,
    });

    // Connect with retry (max 10 attempts, 5 second interval)
    const maxRetries = 10;
    const retryInterval = 5000;
    let connected = false;
    for (let i = 0; i < maxRetries; i++) {
        try {
            await client.connect();
            connected = true;
            break;
        } catch (err) {
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
        totalRequests++;
        try {
            const res = await client.request('connector.entryHandler.hello', msg);
            successCount++;
            logger.info(`Robot ${index} userId: ${userId}, 发送: ${JSON.stringify(msg)}, 收到响应: ${JSON.stringify(res)}`);
        } catch (err) {
            failCount++;
            logger.error(`Robot ${index} userId: ${userId}, 发送: ${JSON.stringify(msg)}, 请求失败:`, err);
        }
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
