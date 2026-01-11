# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Multiplayer snake game with multiple server implementations and C# clients. The project implements an authoritative server architecture where all game logic runs server-side to prevent cheating.

**Architecture:**
- `server-cs/` - C# server using .NET 8 with System.IO.Pipelines for high-performance networking
- `server-skynet/` - Lua-based server using the Skynet framework (alternative implementation)
- `client-cs/` - C# console client with WASD controls and optional AI mode

## Running the Application

### Start C# Server
```bash
cd server-cs
dotnet run
```
Server listens on `0.0.0.0:5000`

### Start Client
```bash
cd client-cs
dotnet run [server_address] [port] [player_name]
```
Examples:
- `dotnet run` - Connect to localhost with defaults
- `dotnet run 127.0.0.1 5000 "MyName"` - Custom server and name
- `CLIENT_MODE=ai dotnet run` - Run in AI mode (stress testing, no visuals)

### Start Skynet Server
```bash
cd server-skynet
./skynet/skynet ./etc/config.node1
```
Requires compiling skynet first: `cd skynet && make linux` (or `macosx` for macOS)

### Testing
```bash
# Test skynet server with AI clients
./test_server_skynet.sh

# Quick test
./test_simple.sh

# Docker build
./build-docker.sh
```

## Code Architecture

### C# Server (`server-cs/`)

**Core Components:**
- `Program.cs` - Main server entry point with TCP listener, client handling loops, and background tasks
- `Room.cs` - Game room logic, runs authoritative game loop at ~160ms ticks, handles collision/movement
- `MatchQueue.cs` - Matchmaking queue that groups players into rooms (default match size: 2)
- `Player.cs` - Player state including snake segments, direction, score, socket connection

**Background Loops (Program.cs):**
- `MatchLoop()` - Checks every 100ms for enough players to create a room
- `RoomCleanupLoop()` - Checks every 200ms for finished rooms, returns players to queue

**Protocol (shared with client):**
- `Protocol/Package.cs` - Binary protocol framing (type + 3-byte length + body)
- `Protocol/Message.cs` - Message encoding with route strings and JSON body

**Flow:**
1. Client connects → handshake exchange
2. Player added to `MatchQueue`
3. When queue reaches `matchSize`, players moved to new `Room`
4. Room game loop broadcasts `ServerState` via `snake.state` push messages
5. Client sends moves via `snake.move` notify messages

**Networking:**
- Uses `System.IO.Pipelines` for efficient zero-copy network I/O
- `ReceiveLoop()` reads directly into PipeWriter
- `SendDataAsync()` sends directly from ReadOnlyMemory
- Package decoding optimized with `SequenceReader<byte>`

### C# Client (`client-cs/`)

**Core Components:**
- `Program.cs` - Main client, handles input, rendering, game loop
- `NetworkClient.cs` - TCP connection with Pipelines, handshake, message sending
- `ReconnectManager.cs` - Auto-reconnect on connection loss
- `GameStateTracker.cs` - Detects and logs game state changes (start/eat/death/end)
- `AIController.cs` - Automated snake movement for stress testing
- `Renderer.cs` - Console rendering (O/o for self, Q/q for others, @ for food)
- `Models/GameModels.cs` - Shared data types (Pos, Direction, ServerState, PlayerView)

**Modes:**
- Normal mode: WASD controls, full game rendering
- AI mode (`CLIENT_MODE=ai`): Auto-movement, minimal logging (only key events)

**Protocol:** Identical to server's protocol files

### Skynet Server (`server-skynet/`)

Lua-based server using the Skynet actor framework. Structure:
- `service/main.lua` - Main service bootstrap
- `service/gateway/` - Connection handling and protocol
- `service/match_loop/` - Matchmaking logic
- `service/room/` - Game room service (per room)

Uses the same binary protocol as the C# server for compatibility.

## Game Configuration

Set in `server-cs/Program.cs`:
- `width = 32` - Grid width
- `height = 18` - Grid height
- `tick = TimeSpan.FromMilliseconds(160)` - Game tick rate
- `matchSize = 2` - Players per room

## Key Design Patterns

**Locking Strategy:**
- `playersLock` guards `allPlayers` dictionary
- `roomsLock` guards `rooms` dictionary
- `_stateLock` in Room guards game state
- Always acquire locks in consistent order to avoid deadlocks

**Game Loop:**
- Room maintains authoritative state
- AdvanceWorld() calculates all movements atomically
- BroadcastStateAsync() sends to all players (non-blocking)
- Room closes when all players dead → players returned to match queue

**Direction Handling:**
- `Direction` - Current movement direction
- `Pending` - Next direction (prevents 180° turns within single tick)
- `IsOpposite()` checks for illegal direction reversals
