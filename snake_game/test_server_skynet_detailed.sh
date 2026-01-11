#!/bin/bash

# Detailed test script for server-skynet
# Saves all logs for analysis

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVER_DIR="$SCRIPT_DIR/server-skynet"
CLIENT_DIR="$SCRIPT_DIR/client-cs"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Log files
SERVER_LOG="/tmp/snake_server_detailed.log"
CLIENT1_LOG="/tmp/snake_client1_detailed.log"
CLIENT2_LOG="/tmp/snake_client2_detailed.log"

# Cleanup function
cleanup() {
    echo -e "\n${YELLOW}Cleaning up...${NC}"
    if [ ! -z "$SERVER_PID" ]; then
        kill $SERVER_PID 2>/dev/null || true
        wait $SERVER_PID 2>/dev/null || true
    fi
    if [ ! -z "$CLIENT1_PID" ]; then
        kill $CLIENT1_PID 2>/dev/null || true
        wait $CLIENT1_PID 2>/dev/null || true
    fi
    if [ ! -z "$CLIENT2_PID" ]; then
        kill $CLIENT2_PID 2>/dev/null || true
        wait $CLIENT2_PID 2>/dev/null || true
    fi
}

trap cleanup EXIT INT TERM

# Check if skynet binary exists
if [ ! -f "$SERVER_DIR/skynet/skynet" ]; then
    echo -e "${RED}Error: skynet binary not found${NC}"
    exit 1
fi

echo -e "${GREEN}Starting detailed server-skynet test...${NC}"
echo "Logs will be saved to:"
echo "  Server: $SERVER_LOG"
echo "  Client 1: $CLIENT1_LOG"
echo "  Client 2: $CLIENT2_LOG"
echo ""

# Start server
echo -e "${YELLOW}Starting server...${NC}"
cd "$SERVER_DIR"
./skynet/skynet ./etc/config > "$SERVER_LOG" 2>&1 &
SERVER_PID=$!

# Wait for server to start
echo -e "${YELLOW}Waiting for server to start...${NC}"
for i in {1..30}; do
    if netstat -tuln 2>/dev/null | grep -q ":5000 " || ss -tuln 2>/dev/null | grep -q ":5000 "; then
        echo -e "${GREEN}Server started successfully${NC}"
        break
    fi
    if [ $i -eq 30 ]; then
        echo -e "${RED}Error: Server failed to start${NC}"
        exit 1
    fi
    sleep 1
done

sleep 2

# Start first client
echo -e "${YELLOW}Starting client 1 (AI mode)...${NC}"
cd "$CLIENT_DIR"
CLIENT_MODE=ai dotnet run 127.0.0.1 5000 "TestPlayer1" > "$CLIENT1_LOG" 2>&1 &
CLIENT1_PID=$!

sleep 2

# Start second client
echo -e "${YELLOW}Starting client 2 (AI mode)...${NC}"
CLIENT_MODE=ai dotnet run 127.0.0.1 5000 "TestPlayer2" > "$CLIENT2_LOG" 2>&1 &
CLIENT2_PID=$!

echo ""
echo -e "${GREEN}All processes started. Running test for 60 seconds...${NC}"
echo ""

# Wait for game to progress
sleep 60

echo -e "${YELLOW}Test completed. Analyzing logs...${NC}"
echo ""

# Check for key events
CLIENT1_GAME_START=$(grep -c "游戏开始" "$CLIENT1_LOG" 2>/dev/null || echo "0")
CLIENT1_EAT=$(grep -c "吃食" "$CLIENT1_LOG" 2>/dev/null || echo "0")
CLIENT1_DEATH=$(grep -c "死亡" "$CLIENT1_LOG" 2>/dev/null || echo "0")
CLIENT1_GAME_END=$(grep -c "游戏结束" "$CLIENT1_LOG" 2>/dev/null || echo "0")

CLIENT2_GAME_START=$(grep -c "游戏开始" "$CLIENT2_LOG" 2>/dev/null || echo "0")
CLIENT2_EAT=$(grep -c "吃食" "$CLIENT2_LOG" 2>/dev/null || echo "0")
CLIENT2_DEATH=$(grep -c "死亡" "$CLIENT2_LOG" 2>/dev/null || echo "0")
CLIENT2_GAME_END=$(grep -c "游戏结束" "$CLIENT2_LOG" 2>/dev/null || echo "0")

echo -e "${YELLOW}Client 1 Events:${NC}"
echo "  游戏开始: $CLIENT1_GAME_START"
echo "  吃食: $CLIENT1_EAT"
echo "  死亡: $CLIENT1_DEATH"
echo "  游戏结束: $CLIENT1_GAME_END"
echo ""

echo -e "${YELLOW}Client 2 Events:${NC}"
echo "  游戏开始: $CLIENT2_GAME_START"
echo "  吃食: $CLIENT2_EAT"
echo "  死亡: $CLIENT2_DEATH"
echo "  游戏结束: $CLIENT2_GAME_END"
echo ""

# Check server logs for key events
echo -e "${YELLOW}Server Events:${NC}"
HANDSHAKE_ACK_COUNT=$(grep -c "handshakeAckHandler called" "$SERVER_LOG" 2>/dev/null || echo "0")
ADD_TO_QUEUE_COUNT=$(grep -c "add_player_to_queue" "$SERVER_LOG" 2>/dev/null || echo "0")
MATCHED_COUNT=$(grep -c "Matched.*players" "$SERVER_LOG" 2>/dev/null || echo "0")
ROOM_STARTED_COUNT=$(grep -c "Room.*started with" "$SERVER_LOG" 2>/dev/null || echo "0")
echo "  handshakeAckHandler called: $HANDSHAKE_ACK_COUNT"
echo "  add_player_to_queue: $ADD_TO_QUEUE_COUNT"
echo "  Matched players: $MATCHED_COUNT"
echo "  Rooms started: $ROOM_STARTED_COUNT"
echo ""

# Show relevant server log snippets
echo -e "${YELLOW}Relevant server log snippets:${NC}"
echo "--- handshakeAckHandler calls ---"
grep "handshakeAckHandler" "$SERVER_LOG" 2>/dev/null | tail -5 || echo "  None found"
echo ""
echo "--- add_player_to_queue calls ---"
grep "add_player_to_queue" "$SERVER_LOG" 2>/dev/null | tail -5 || echo "  None found"
echo ""
echo "--- Match queue activity ---"
grep -E "(Match queue|Matched|joined match queue)" "$SERVER_LOG" 2>/dev/null | tail -10 || echo "  None found"
echo ""
echo "--- Room activity ---"
grep -E "(Room.*started|joined room)" "$SERVER_LOG" 2>/dev/null | tail -10 || echo "  None found"
echo ""

# Determine test result
TEST_PASSED=true

if [ "$CLIENT1_GAME_START" -eq "0" ] && [ "$CLIENT2_GAME_START" -eq "0" ]; then
    echo -e "${RED}FAIL: No game start detected${NC}"
    TEST_PASSED=false
fi

if [ "$CLIENT1_EAT" -eq "0" ] && [ "$CLIENT2_EAT" -eq "0" ]; then
    echo -e "${RED}FAIL: No food eating detected${NC}"
    TEST_PASSED=false
fi

if [ "$TEST_PASSED" = true ]; then
    echo -e "${GREEN}========================================${NC}"
    echo -e "${GREEN}TEST PASSED${NC}"
    echo -e "${GREEN}========================================${NC}"
    exit 0
else
    echo -e "${RED}========================================${NC}"
    echo -e "${RED}TEST FAILED${NC}"
    echo -e "${RED}========================================${NC}"
    echo ""
    echo "Full logs saved to:"
    echo "  $SERVER_LOG"
    echo "  $CLIENT1_LOG"
    echo "  $CLIENT2_LOG"
    exit 1
fi

