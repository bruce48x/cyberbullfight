# Repository Guidelines

## Project Structure & Module Organization
- `echo/`: main benchmark suite comparing server implementations across languages (e.g., `server-go`, `server-cs`, `server-cpp`, `server-pinus`, `server-skynet`) plus clients (`client-go`, `client-js`) and infra (`terraform/`, `Dockerfile`s).
- `move_ball/`: small moving-ball performance case (minimal scaffolding).
- `snake_game/`: multiplayer snake game (`server-cs/`, `client-cs/`, `client-ai-cs/`).
- Root tools: `benchmark.sh` for containerized runs and `analyze_csv.py` for result analysis.

## Build, Test, and Development Commands
Use the language-specific runner for each module. Examples:
```sh
# Echo servers
cd echo/server-go && go run main.go
cd echo/server-cs && dotnet run
cd echo/server-cpp && cmake -B build && cmake --build build && ./build/server-cpp
cd echo/server-pinus && yarn install && node dist/app
cd echo/server-skynet && cd skynet && make linux && cd .. && make && ./skynet/skynet ./etc/config

# Echo clients
cd echo/client-js && yarn install && node dist/app
cd echo/client-go && go run main.go

# Container build (echo)
cd echo && ./build-docker.sh

# Snake game
cd snake_game/server-cs && dotnet run
cd snake_game/client-cs && dotnet run 127.0.0.1 5000 "player"
```
Use `benchmark.sh` with the flags shown in `README.md` for repeatable perf runs.

## Coding Style & Naming Conventions
- Keep the existing `server-*` and `client-*` naming pattern for new modules.
- C/C++: format with `.clang-format` at the repo root.
- Other languages: follow the current file’s indentation and conventions; avoid mixing styles within a module.

## Testing Guidelines
- No unified test runner. Validate by running the relevant server/client pair and checking logs.
- For performance work, include a `benchmark.sh` run and note the parameters used.
- If you add tests, use language-standard naming (e.g., `*_test.go` for Go).

## Commit & Pull Request Guidelines
- Commit history uses short, lowercase subjects (e.g., `fix`, `rename`, `refactor server-starnet`); keep messages concise and imperative.
- PRs should include: purpose, how to run/verify (commands), and any metrics/screenshots for behavioral or performance changes.

## Configuration Notes
- Common env vars for clients/containers: `SERVER_HOST`, `SERVER_PORT`, `COUNT` (see `README.md` for defaults).
