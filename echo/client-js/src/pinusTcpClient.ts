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
import { EventEmitter } from 'events';
import * as Protocol from 'pinusmod-protocol';
const protocol = Protocol.Protocol;
const Package = Protocol.Package;
const Message = Protocol.Message;
import { Protobuf } from 'pinusmod-protobuf';
import * as net from 'node:net';
import * as tls from 'node:tls';
import { commonHandler } from './commonHandler';
import * as util from 'node:util';
import * as path from 'node:path';
import { Constants, NetState, readState, ResponseState } from './config/constants';
const logger = Logger.getLogger('client', path.basename(__filename));

const handshankeData = {
    sys: {
        type: 'client-simulator',
        version: '0.1.0',
        rsa: {},
    },
    user: {},
};

const gapThreshold = 100; // heartbeat gap threashold

export class PinusTcpClient extends EventEmitter {
    private opts: { host: string; port: number; userId: string; tcpEncrypt: boolean };
    userId: string;
    private readState: any;
    private headBuffer: any;
    private headOffset: any;
    private packageOffset: any;
    private packageSize: any;
    private packageBuffer: any;
    private netState: any;
    private socket: net.Socket | tls.TLSSocket;
    private heartbeatInterval: any;
    private heartbeatTimeout: any;
    private nextHeartbeatTimeout: any;
    private heartbeatTimeoutId: any;
    private heartbeatId: NodeJS.Timeout;
    private reqId: any;
    private dict: any;
    private abbrs: any;
    private protos: any;
    private protobuf: Protobuf;
    private callbacks: { [reqId: number]: (...params: any[]) => void };

    constructor(opts) {
        super();
        this.userId = opts.userId;
        this.callbacks = {};
        this.opts = opts;

        this.readState = readState.ST_HEAD;

        this.headBuffer = Buffer.alloc(Constants.HEAD_SIZE);
        this.headOffset = 0;

        this.packageOffset = 0;
        this.packageSize = 0;
        this.packageBuffer = null;

        this.netState = NetState.ST_INITED;
        this.socket = null;

        this.heartbeatInterval = null;
        this.heartbeatTimeout = null;
        this.nextHeartbeatTimeout = null;
        this.heartbeatTimeoutId = null;
        this.heartbeatId = null;

        this.reqId = 0;

        this.dict = null;
        this.abbrs = null;
        this.protos = null;
    }

    destroy() {
        this.disconnect();
        this.removeAllListeners('handshake');
        this.removeAllListeners('heartbeat');
        this.removeAllListeners('message');
    }

    async connect() {
        const { port, host, tcpEncrypt } = this.opts;
        if (tcpEncrypt) {
            this.socket = tls.connect(port, host);
        } else {
            this.socket = net.connect(port, host);
        }
        this.socket.setTimeout(600 * 1000);
        this.socket.on('close', (had_error) => {
            logger.info('socket on close, had error = ', had_error);
        });
        this.socket.on('connect', () => {
            const handshakeBuff = Package.encode(
                Package.TYPE_HANDSHAKE,
                protocol.strencode(JSON.stringify(handshankeData)),
            );
            this._send(handshakeBuff);
        });
        this.socket.on('data', (data) => {
            let offset = 0;
            const len = data.length;
            while (offset < len && this.readState !== readState.ST_CLOSED) {
                if (this.readState === readState.ST_HEAD) {
                    offset = this._readHead(data, offset);
                }
                if (this.readState === readState.ST_BODY) {
                    offset = this._readBody(data, offset);
                }
            }
        });
        this.socket.on('drain', () => {
            logger.info('socket on drain');
        });
        this.socket.on('end', () => {
            logger.info('socket on end');
        });
        this.socket.on('error', (err) => {
            logger.info('socket on error', err.stack);
        });
        this.socket.on('lookup', (err, address, family, host) => {
            logger.info('socket on lookup', { err, address, family, host });
        });
        this.socket.on('timeout', () => {
            logger.info('socket on timeout');
        });

        this.on('heartbeat', this._handleHeartbeat);
        this.on('message', (pkg) => {
            const msg = Message.decode(pkg.body);
            if (msg.compressRoute && this.abbrs && this.abbrs[msg.route]) {
                msg.route = this.abbrs[msg.route];
            }
            msg.body = this._decode(msg.route, msg.body);
            if (msg.type === Message.TYPE_PUSH) {
                if (!msg.id) {
                    logger.trace(this.userId, '通知', msg.route, util.inspect(msg.body, false, 10));
                    this.emit(msg.route, msg.body);
                }
            } else if (msg.type == Message.TYPE_RESPONSE) {
                const { id, body } = msg;
                const cb = this.callbacks[id];
                if (cb) {
                    logger.trace(this.userId, '响应', util.inspect(body, false, 10));
                    cb(body);
                    delete this.callbacks[id];
                }
            }
        });

        await this._connComplete();
    }

    disconnect() {
        this.netState = NetState.ST_CLOSED;
        if (this.socket) {
            this.socket.end();
            this.socket = null;
        }
    }

    private _connComplete(): Promise<void> {
        return new Promise((resolve, reject) => {
            this.on('handshake', (data) => {
                if (data.code === ResponseState.RES_OLD_CLIENT) {
                    throw new Error('client version not fullfill');
                }

                if (data.code !== ResponseState.RES_OK) {
                    throw new Error('handshake fail');
                }

                this._handshakeInit(data);
                this.netState = NetState.ST_WORKING;

                const obj = Package.encode(Package.TYPE_HANDSHAKE_ACK);
                this.socket.write(obj, () => {
                    resolve();
                });
            });
        });
    }

    private _send(buff) {
        if (!!this.socket) {
            this.socket.write(buff);
        }
    }

    // ============================================== socket 数据处理 ==============================================

    private _readHead(data, offset) {
        const hlen = Constants.HEAD_SIZE - this.headOffset;
        const dlen = data.length - offset;
        const len = Math.min(hlen, dlen);
        let dend = offset + len;

        data.copy(this.headBuffer, this.headOffset, offset, dend);
        this.headOffset += len;

        if (this.headOffset === Constants.HEAD_SIZE) {
            // if head segment finished
            const size = PinusTcpClient.headHandler(this.headBuffer);
            if (size < 0) {
                throw new Error('invalid body size: ' + size);
            }
            // check if header contains a valid type
            if (PinusTcpClient.checkTypeData(this.headBuffer[0])) {
                this.packageSize = size + Constants.HEAD_SIZE;
                this.packageBuffer = Buffer.alloc(this.packageSize);
                this.headBuffer.copy(this.packageBuffer, 0, 0, Constants.HEAD_SIZE);
                this.packageOffset = Constants.HEAD_SIZE;
                this.readState = readState.ST_BODY;
            } else {
                dend = data.length;
                logger.info(
                    'close the connection with invalid head message, the remote ip is %s && port is %s && message is %j',
                    this.socket.remoteAddress,
                    this.socket.remotePort,
                    data,
                );
                this.socket.end();
            }
        }

        return dend;
    }

    private static headHandler(headBuffer) {
        let len = 0;
        for (let i = 1; i < 4; i++) {
            if (i > 1) {
                len <<= 8;
            }
            len += headBuffer.readUInt8(i);
        }
        return len;
    }

    private static checkTypeData(data) {
        return (
            data === Package.TYPE_HANDSHAKE ||
            data === Package.TYPE_HANDSHAKE_ACK ||
            data === Package.TYPE_HEARTBEAT ||
            data === Package.TYPE_DATA ||
            data === Package.TYPE_KICK
        );
    }

    private _readBody(data, offset) {
        const blen = this.packageSize - this.packageOffset;
        const dlen = data.length - offset;
        const len = Math.min(blen, dlen);
        const dend = offset + len;

        data.copy(this.packageBuffer, this.packageOffset, offset, dend);

        this.packageOffset += len;

        if (this.packageOffset === this.packageSize) {
            // if all the package finished
            this._processPackage(this.packageBuffer);
            this._reset();
        }

        return dend;
    }

    private _reset() {
        this.headOffset = 0;
        this.packageOffset = 0;
        this.packageSize = 0;
        this.packageBuffer = null;
        this.readState = readState.ST_HEAD;
    }

    private _processPackage(buff) {
        const pkg = Package.decode(buff);
        if (Array.isArray(pkg)) {
            for (const i of pkg) {
                if (this._isHandshakeACKPackage(pkg[i].type)) {
                    this.netState = NetState.ST_WORKING;
                }
                commonHandler(this, pkg[i]);
            }
        } else {
            if (this._isHandshakeACKPackage(pkg.type)) {
                this.netState = NetState.ST_WORKING;
            }
            commonHandler(this, pkg);
        }
    }

    private _isHandshakeACKPackage(type) {
        return type === Package.TYPE_HANDSHAKE_ACK;
    }

    // ============================================== 握手 ==============================================

    private _initData(data) {
        if (!data || !data.sys) {
            return;
        }
        this.dict = data.sys.dict;
        this.protos = data.sys.protos;

        //Init compress dict
        if (this.dict) {
            this.abbrs = {};

            for (const route in this.dict) {
                this.abbrs[this.dict[route]] = route;
            }
        }

        //Init protobuf protos
        if (this.protos) {
            this.protobuf = new Protobuf({ encoderProtos: this.protos.client, decoderProtos: this.protos.server });
        }
    }

    private _handshakeInit(data) {
        if (data.sys && data.sys.heartbeat) {
            this.heartbeatInterval = data.sys.heartbeat * 1000; // heartbeat interval
            this.heartbeatTimeout = this.heartbeatInterval * 2; // max heartbeat timeout
        } else {
            this.heartbeatInterval = 0;
            this.heartbeatTimeout = 0;
        }

        this._initData(data);

        // if (typeof handshakeCallback === 'function') {
        //     handshakeCallback(data.user);
        // }
    }

    // ============================================== 心跳 ==============================================

    private _heartbeatTimeoutCb() {
        logger.info(`tcp heartbeat timeout`);
        const gap = this.nextHeartbeatTimeout - Date.now();
        if (gap > gapThreshold) {
            this.heartbeatTimeoutId = setTimeout(this._heartbeatTimeoutCb.bind(this), gap);
        } else {
            this.emit('heartbeatTimeout');
        }
    }

    private _handleHeartbeat() {
        if (!this.heartbeatInterval) {
            return;
        }

        const obj = Package.encode(Package.TYPE_HEARTBEAT);
        if (this.heartbeatTimeoutId) {
            clearTimeout(this.heartbeatTimeoutId);
            this.heartbeatTimeoutId = null;
        }

        if (this.heartbeatId) {
            // already in a heartbeat interval
            // return;
            clearTimeout(this.heartbeatId);
            this.heartbeatId = undefined;
        }
        this.heartbeatId = setTimeout(() => {
            this.heartbeatId = null;
            this._send(obj);

            this.nextHeartbeatTimeout = Date.now() + this.heartbeatTimeout;
            this.heartbeatTimeoutId = setTimeout(this._heartbeatTimeoutCb.bind(this), this.heartbeatTimeout);
        }, this.heartbeatInterval);
    }

    // ============================================== 对外 API ==============================================

    private _encode(route, msg) {
        const normalizedRoute = this.protobuf?.normalizeRoute(route);
        if (this.protos && normalizedRoute && this.protobuf?.check('server', normalizedRoute)) {
            msg = this.protobuf.encode(normalizedRoute, msg);
        } else {
            msg = protocol.strencode(JSON.stringify(msg));
        }
        return msg;
    }

    private _decode(route, body) {
        const normalizedRoute = this.protobuf?.normalizeRoute(route);
        if (this.protos && normalizedRoute && this.protobuf?.check('client', normalizedRoute)) {
            body = this.protobuf.decode(normalizedRoute, body);
        } else {
            body = JSON.parse(protocol.strdecode(body));
        }
        return body;
    }

    private _routeCompress(route) {
        if (this.dict && this.dict[route]) {
            return { route: this.dict[route], compressRoute: 1 };
        }
        return { route: route, compressRoute: 0 };
    }

    //todo 没用
    private _routeDecompress(route) {
        if (this.abbrs[route]) {
            return this.abbrs[route];
        }
        return route;
    }

    async request(route, msg?: any): Promise<any> {
        if (!route) {
            logger.error(new Error('!route'));
            throw new Error('route cannot be null or undefined');
        }
        if (!msg) {
            msg = {};
        }
        logger.trace(this.userId, `发起 ${route} = `, msg);
        const reqId = ++this.reqId;
        msg = this._encode(route, msg);
        const res = this._routeCompress(route);
        const compressRoute = res.compressRoute;
        route = res.route;
        const encodedMsg = Message.encode(reqId, Message.TYPE_REQUEST, !!compressRoute, route, msg);
        const buff = Package.encode(Package.TYPE_DATA, encodedMsg);
        this._send(buff);
        return new Promise((resolve, reject) => {
            const cb = (res) => {
                if (res) {
                    resolve(res);
                } else {
                    reject();
                }
            };
            this.callbacks[reqId] = cb;
        });
    }

    notify(route, msg) {
        msg = this._encode(route, msg);
        const res = this._routeCompress(route);
        const compressRoute = res.compressRoute;
        route = res.route;
        const encodedMsg = Message.encode(0, Message.TYPE_NOTIFY, !!compressRoute, route, msg);
        const buff = Package.encode(Package.TYPE_DATA, encodedMsg);
        this._send(buff);
    }
}
