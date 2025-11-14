"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.Handler = void 0;
function default_1(app) {
    return new Handler(app);
}
exports.default = default_1;
class Handler {
    constructor(app) {
        this.app = app;
    }
    async hello(msg, session) {
        return { code: 0, msg };
    }
}
exports.Handler = Handler;
