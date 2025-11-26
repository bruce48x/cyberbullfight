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

async function main() {
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
            logger.error(`Connection attempt ${i + 1}/${maxRetries} failed:`, err);
            if (i < maxRetries - 1) {
                logger.info(`Retrying in ${retryInterval / 1000} seconds...`);
                await new Promise((resolve) => setTimeout(resolve, retryInterval));
            }
        }
    }
    if (!connected) {
        throw new Error(`Failed to connect after ${maxRetries} attempts`);
    }

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
