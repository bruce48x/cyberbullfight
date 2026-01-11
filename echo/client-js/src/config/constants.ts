export const enum Constants {
    HEAD_SIZE = 4,
}

export const enum readState {
    ST_HEAD = 0,
    ST_BODY = 1,
    ST_CLOSED = 2,
}

export const enum NetState {
    ST_INITED = 0,
    ST_WAIT_ACK = 1,
    ST_WORKING = 2,
    ST_CLOSED = 3,
}

export const enum ResponseState {
    RES_OK = 200,
    RES_FAIL = 500,
    RES_OLD_CLIENT = 501,
}

export const enum BattleSide {
    RED = 1,
    BLUE,
}

export const enum ClientUserState {
    NONE = 1,
    WAIT_RESPONSE,

    REQUEST_PASSPORT,
    REQUEST_PASSPORT_SUCCESS,
    REQUEST_LOGIN,
    REQUEST_LOGIN_SUCCESS,
    REQUEST_SERVER_LIST,
    REQUEST_SERVER_LIST_SUCCESS,
    REQUEST_SELECT_SERVER,
    REQUEST_SELECT_SERVER_SUCCESS,
    REQUEST_TCP_ENTRY,

    LOBBY,
    REQUEST_RECEIVE_REWARD,

    FIND_MATCH,
    ONLINE_BEFORE_BATTLE,
    ONLINE_CHOOSE_HERO,
    ONLINE_READY,
    ONLINE_BATTLE,
    ONLINE_WAIT_BATTLE_END, // 发出 battleEnd 之后，收到服务器通知 onBattleEnd 之前，处于这个状态
    ONLINE_WAIT_CONTINUE, // 收到服务器通知 onBattleEnd 之后，处于这个状态
    ONLINE_AFTER_BATTLE, // 一局战斗结束，处于这个状态

    MISSION_WAIT_START, // 向服务器发送 missionStart 请求之后，进入此状态，但是还未进入战斗
    MISSION, // 战斗中
    MISSION_PICKED_ITEMS,
    MISSION_AWARDS,
    SWEEP_MISSION,

    SAVE_ARCHIVE,
    DELETE_ARCHIVE,

    FINISH,
}

export const enum ClientUserSubState {
    NONE = 1,
    LOBBY,
    FIND_FAST_MATCH,
    FIND_RANKING_MATCH,
    FRIEND_BATTLE,
    FAST_MATCH_BATTLE,
    RANKING_MATCH_BATTLE,
}

export const enum SubStateInvitation {
    RECEIVED_INVITATION = 1,
}
