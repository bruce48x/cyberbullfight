import { Application, FrontendSession } from 'pinus';

export default function (app: Application) {
    return new Handler(app);
}

export class Handler {
    constructor(private app: Application) {
    }

    async hello(msg: any, session: FrontendSession) {
        let reqId = session.get('reqId') || 0;
        reqId++;
        session.set('reqId', reqId);
        session.pushAll((err, result) => {
            if (err) {
                console.error(`session.pushAll 出错 : ${err}`);
            }
        });
        msg.serverReqId = reqId;
        return { code: 0, msg };
    }
}