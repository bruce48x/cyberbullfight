FROM gcc:13 AS builder

WORKDIR /app

RUN apt-get update && apt-get install -y cmake

COPY . .

RUN cmake -B build -DCMAKE_BUILD_TYPE=Release && cmake --build build

FROM debian:bookworm-slim

WORKDIR /app

COPY --from=builder /app/build/server-cpp .

EXPOSE 3010

CMD ["./server-cpp"]

