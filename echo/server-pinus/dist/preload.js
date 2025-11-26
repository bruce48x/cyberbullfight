"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.preload = void 0;
// 支持注解
require("reflect-metadata");
const pinus_1 = require("pinus");
/**
 *  替换全局Promise
 *  自动解析sourcemap
 *  捕获全局错误
 */
function preload() {
    // 捕获普通异常
    process.on('uncaughtException', function (err) {
        console.error(pinus_1.pinus.app ? pinus_1.pinus.app.getServerId() : "unknownServerId", 'uncaughtException Caught exception: ', err);
    });
    // 捕获async异常
    process.on('unhandledRejection', (reason, p) => {
        console.error(pinus_1.pinus.app ? pinus_1.pinus.app.getServerId() : "unknownServerId", 'Caught Unhandled Rejection at:', p, 'reason:', reason);
    });
}
exports.preload = preload;
