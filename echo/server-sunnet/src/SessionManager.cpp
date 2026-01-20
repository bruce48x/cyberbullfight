#include "SessionManager.h"
#include <iostream>

SessionManager& SessionManager::getInstance() {
    static SessionManager instance;
    return instance;
}

void SessionManager::onHandshakeSuccess() {
    int currentCount = ++count_;
    std::cout << "[session-manager] Handshake success, total connections: " << currentCount << std::endl;
}

void SessionManager::onConnectionClose() {
    int currentCount = --count_;
    std::cout << "[session-manager] Connection closed, total connections: " << currentCount << std::endl;
}

int SessionManager::getCount() const {
    return count_.load();
}
