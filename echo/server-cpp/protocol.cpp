#include "protocol.hpp"

namespace protocol {

std::vector<uint8_t> Package::encode(PackageType type, const std::vector<uint8_t>& body) {
    size_t body_len = body.size();
    std::vector<uint8_t> result(4 + body_len);

    result[0] = static_cast<uint8_t>(type);
    result[1] = static_cast<uint8_t>((body_len >> 16) & 0xFF);
    result[2] = static_cast<uint8_t>((body_len >> 8) & 0xFF);
    result[3] = static_cast<uint8_t>(body_len & 0xFF);

    if (body_len > 0) {
        std::copy(body.begin(), body.end(), result.begin() + 4);
    }

    return result;
}

std::optional<Package> Package::decode(const std::vector<uint8_t>& data) {
    if (data.size() < 4) return std::nullopt;

    auto pkg_type = static_cast<PackageType>(data[0]);
    int length = (data[1] << 16) | (data[2] << 8) | data[3];

    if (static_cast<int>(data.size()) < 4 + length) return std::nullopt;

    Package pkg;
    pkg.type = pkg_type;
    pkg.body.assign(data.begin() + 4, data.begin() + 4 + length);

    return pkg;
}

std::vector<uint8_t> Message::encode(int id, MessageType type, bool compress_route,
                                     const std::string& route, const std::vector<uint8_t>& body) {
    std::vector<uint8_t> result;

    // Encode flag
    uint8_t flag = static_cast<uint8_t>(static_cast<int>(type) << 1);
    if (compress_route) flag |= 1;
    result.push_back(flag);

    // Encode id (base128, only for REQUEST/RESPONSE)
    if (type == MessageType::Request || type == MessageType::Response) {
        int id_val = id;
        do {
            int tmp = id_val % 128;
            int next = id_val / 128;
            if (next != 0) tmp += 128;
            result.push_back(static_cast<uint8_t>(tmp));
            id_val = next;
        } while (id_val != 0);
    }

    // Encode route (only for REQUEST/NOTIFY/PUSH)
    if (type == MessageType::Request || type == MessageType::Notify || type == MessageType::Push) {
        if (compress_route) {
            result.push_back(0);
            result.push_back(0);
        } else {
            result.push_back(static_cast<uint8_t>(route.size()));
            result.insert(result.end(), route.begin(), route.end());
        }
    }

    // Encode body
    result.insert(result.end(), body.begin(), body.end());

    return result;
}

std::optional<Message> Message::decode(const std::vector<uint8_t>& data) {
    if (data.empty()) return std::nullopt;

    size_t offset = 0;

    // Parse flag
    uint8_t flag = data[offset++];
    bool compress_route = (flag & 0x1) == 1;
    auto msg_type = static_cast<MessageType>((flag >> 1) & 0x7);
    bool compress_gzip = ((flag >> 4) & 0x1) == 1;

    // Parse id (base128, only for REQUEST/RESPONSE)
    int id = 0;
    if (msg_type == MessageType::Request || msg_type == MessageType::Response) {
        int i = 0;
        while (true) {
            if (offset >= data.size()) return std::nullopt;
            uint8_t m = data[offset++];
            id += (m & 0x7F) << (7 * i);
            i++;
            if (m < 128) break;
        }
    }

    // Parse route
    std::string route;
    if (msg_type == MessageType::Request || msg_type == MessageType::Notify || msg_type == MessageType::Push) {
        if (compress_route) {
            if (offset + 2 > data.size()) return std::nullopt;
            offset += 2;
        } else {
            if (offset >= data.size()) return std::nullopt;
            int route_len = data[offset++];
            if (route_len > 0) {
                if (offset + route_len > data.size()) return std::nullopt;
                route.assign(data.begin() + offset, data.begin() + offset + route_len);
                offset += route_len;
            }
        }
    }

    // Read body
    std::vector<uint8_t> body;
    if (offset < data.size()) {
        body.assign(data.begin() + offset, data.end());
    }

    Message msg;
    msg.id = id;
    msg.type = msg_type;
    msg.compress_route = compress_route;
    msg.route = route;
    msg.body = body;
    msg.compress_gzip = compress_gzip;

    return msg;
}

} // namespace protocol

