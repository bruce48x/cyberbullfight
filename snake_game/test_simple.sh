#!/bin/bash

# Simple test script - manual observation

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVER_DIR="$SCRIPT_DIR/server-skynet"
CLIENT_DIR="$SCRIPT_DIR/client-cs"

echo "Starting server..."
cd "$SERVER_DIR"
./skynet/skynet ./etc/config &
SERVER_PID=$!

sleep 3

echo "Starting client 1..."
cd "$CLIENT_DIR"
CLIENT_MODE=ai dotnet run 127.0.0.1 5000 "Player1" &
CLIENT1_PID=$!

sleep 2

echo "Starting client 2..."
CLIENT_MODE=ai dotnet run 127.0.0.1 5000 "Player2" &
CLIENT2_PID=$!

echo ""
echo "Server PID: $SERVER_PID"
echo "Client 1 PID: $CLIENT1_PID"
echo "Client 2 PID: $CLIENT2_PID"
echo ""
echo "Press Ctrl+C to stop all processes"

trap "kill $SERVER_PID $CLIENT1_PID $CLIENT2_PID 2>/dev/null; exit" INT TERM

wait

