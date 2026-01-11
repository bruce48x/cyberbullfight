#pragma once

#include <cstdint>
#include <string>
#include <vector>
#include <optional>

namespace protocol {

// Package types
enum class PackageType : uint8_t {
    Handshake = 1,
    HandshakeAck = 2,
    Heartbeat = 3,
    Data = 4,
    Kick = 5
};

// Message types
enum class MessageType : int {
    Request = 0,
    Notify = 1,
    Response = 2,
    Push = 3
};

struct Package {
    PackageType type;
    std::vector<uint8_t> body;

    static std::vector<uint8_t> encode(PackageType type, const std::vector<uint8_t>& body);
    static std::optional<Package> decode(const std::vector<uint8_t>& data);
};

struct Message {
    int id = 0;
    MessageType type = MessageType::Request;
    bool compress_route = false;
    std::string route;
    std::vector<uint8_t> body;
    bool compress_gzip = false;

    static std::vector<uint8_t> encode(int id, MessageType type, bool compress_route,
                                       const std::string& route, const std::vector<uint8_t>& body);
    static std::optional<Message> decode(const std::vector<uint8_t>& data);
};

} // namespace protocol

