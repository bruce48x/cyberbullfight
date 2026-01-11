#!/bin/bash

# Test script for server-skynet
# Tests the complete game flow: game start, eating food, death, game end

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVER_DIR="$SCRIPT_DIR/server-skynet"
CLIENT_DIR="$SCRIPT_DIR/client-cs"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

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
    rm -f /tmp/snake_server.log /tmp/snake_client1.log /tmp/snake_client2.log
}

trap cleanup EXIT INT TERM

# Check if skynet binary exists
if [ ! -f "$SERVER_DIR/skynet/skynet" ]; then
    echo -e "${RED}Error: skynet binary not found. Please compile skynet first:${NC}"
    echo "  cd $SERVER_DIR/skynet && make linux"
    exit 1
fi

# Check if cjson.so exists
if [ ! -f "$SERVER_DIR/luaclib/cjson.so" ]; then
    echo -e "${YELLOW}Warning: cjson.so not found. Compiling...${NC}"
    cd "$SERVER_DIR" && make
fi

# Check if client exists
if [ ! -d "$CLIENT_DIR" ]; then
    echo -e "${RED}Error: client-cs directory not found${NC}"
    exit 1
fi

echo -e "${GREEN}Starting server-skynet test...${NC}"
echo ""

# Start server
echo -e "${YELLOW}Starting server...${NC}"
cd "$SERVER_DIR"
./skynet/skynet ./etc/config > /tmp/snake_server.log 2>&1 &
SERVER_PID=$!

# Wait for server to start (check if port 5000 is listening)
echo -e "${YELLOW}Waiting for server to start...${NC}"
for i in {1..30}; do
    if netstat -tuln 2>/dev/null | grep -q ":5000 " || ss -tuln 2>/dev/null | grep -q ":5000 "; then
        echo -e "${GREEN}Server started successfully${NC}"
        break
    fi
    if [ $i -eq 30 ]; then
        echo -e "${RED}Error: Server failed to start within 30 seconds${NC}"
        echo "Server log:"
        tail -20 /tmp/snake_server.log
        exit 1
    fi
    sleep 1
done

# Give server a bit more time to fully initialize
sleep 2

# Start first client
echo -e "${YELLOW}Starting client 1 (AI mode)...${NC}"
cd "$CLIENT_DIR"
CLIENT_MODE=ai dotnet run 127.0.0.1 5000 "TestPlayer1" > /tmp/snake_client1.log 2>&1 &
CLIENT1_PID=$!

# Wait a bit before starting second client
sleep 2

# Start second client
echo -e "${YELLOW}Starting client 2 (AI mode)...${NC}"
CLIENT_MODE=ai dotnet run 127.0.0.1 5000 "TestPlayer2" > /tmp/snake_client2.log 2>&1 &
CLIENT2_PID=$!

echo ""
echo -e "${GREEN}All processes started. Running test for 60 seconds...${NC}"
echo ""

# Wait for game to progress
sleep 60

# Check logs for key events
echo -e "${YELLOW}Checking logs for key events...${NC}"
echo ""

# Check client 1 logs
CLIENT1_GAME_START=$(grep -c "游戏开始" /tmp/snake_client1.log 2>/dev/null | head -1 || echo "0")
CLIENT1_EAT=$(grep -c "吃食" /tmp/snake_client1.log 2>/dev/null | head -1 || echo "0")
CLIENT1_DEATH=$(grep -c "死亡" /tmp/snake_client1.log 2>/dev/null | head -1 || echo "0")
CLIENT1_GAME_END=$(grep -c "游戏结束" /tmp/snake_client1.log 2>/dev/null | head -1 || echo "0")

# Check client 2 logs
CLIENT2_GAME_START=$(grep -c "游戏开始" /tmp/snake_client2.log 2>/dev/null | head -1 || echo "0")
CLIENT2_EAT=$(grep -c "吃食" /tmp/snake_client2.log 2>/dev/null | head -1 || echo "0")
CLIENT2_DEATH=$(grep -c "死亡" /tmp/snake_client2.log 2>/dev/null | head -1 || echo "0")
CLIENT2_GAME_END=$(grep -c "游戏结束" /tmp/snake_client2.log 2>/dev/null | head -1 || echo "0")

# Print results
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

# Determine test result
TEST_PASSED=true

# Convert to integers (handle empty strings)
CLIENT1_GAME_START=${CLIENT1_GAME_START:-0}
CLIENT1_EAT=${CLIENT1_EAT:-0}
CLIENT1_DEATH=${CLIENT1_DEATH:-0}
CLIENT1_GAME_END=${CLIENT1_GAME_END:-0}
CLIENT2_GAME_START=${CLIENT2_GAME_START:-0}
CLIENT2_EAT=${CLIENT2_EAT:-0}
CLIENT2_DEATH=${CLIENT2_DEATH:-0}
CLIENT2_GAME_END=${CLIENT2_GAME_END:-0}

if [ "$CLIENT1_GAME_START" -eq "0" ] && [ "$CLIENT2_GAME_START" -eq "0" ]; then
    echo -e "${RED}FAIL: No game start detected on either client${NC}"
    TEST_PASSED=false
fi

if [ "$CLIENT1_EAT" -eq "0" ] && [ "$CLIENT2_EAT" -eq "0" ]; then
    echo -e "${RED}FAIL: No food eating detected on either client${NC}"
    TEST_PASSED=false
fi

# At least one client should have died or game ended
if [ "$CLIENT1_DEATH" -eq "0" ] && [ "$CLIENT2_DEATH" -eq "0" ] && [ "$CLIENT1_GAME_END" -eq "0" ] && [ "$CLIENT2_GAME_END" -eq "0" ]; then
    echo -e "${YELLOW}WARNING: No death or game end detected. Game may still be running.${NC}"
    # This is not a failure, just a warning
fi

# Print summary
echo ""
if [ "$TEST_PASSED" = true ]; then
    echo -e "${GREEN}========================================${NC}"
    echo -e "${GREEN}TEST PASSED: Key events detected${NC}"
    echo -e "${GREEN}========================================${NC}"
    
    # Show recent logs
    echo ""
    echo -e "${YELLOW}Recent Client 1 logs (last 10 lines):${NC}"
    tail -10 /tmp/snake_client1.log | sed 's/^/  /'
    echo ""
    echo -e "${YELLOW}Recent Client 2 logs (last 10 lines):${NC}"
    tail -10 /tmp/snake_client2.log | sed 's/^/  /'
    
    exit 0
else
    echo -e "${RED}========================================${NC}"
    echo -e "${RED}TEST FAILED: Missing key events${NC}"
    echo -e "${RED}========================================${NC}"
    
    # Show full logs for debugging
    echo ""
    echo -e "${YELLOW}Full Client 1 log:${NC}"
    cat /tmp/snake_client1.log
    echo ""
    echo -e "${YELLOW}Full Client 2 log:${NC}"
    cat /tmp/snake_client2.log
    echo ""
    echo -e "${YELLOW}Server log (last 50 lines):${NC}"
    tail -50 /tmp/snake_server.log
    
    exit 1
fi

