"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.AuthRemoter = void 0;
function default_1(app) {
    return new AuthRemoter(app);
}
exports.default = default_1;
class AuthRemoter {
    constructor(app) {
        this.app = app;
    }
    /**
     *
     * @param username
     * @param password
     */
    async auth(username, password) {
        return true;
    }
    // 私有方法不会加入到RPC提示里
    async privateMethod(testarg, arg2) {
    }
}
exports.AuthRemoter = AuthRemoter;
