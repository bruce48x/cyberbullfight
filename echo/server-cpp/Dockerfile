FROM debian:13.2 AS builder

WORKDIR /app

RUN apt-get update
RUN apt-get install -y build-essential
RUN apt-get install -y cmake

COPY . .

RUN cmake -B build -DCMAKE_BUILD_TYPE=Release && cmake --build build

FROM debian:13.2-slim

WORKDIR /app

COPY --from=builder /app/build/server-cpp .

EXPOSE 3010

CMD ["./server-cpp"]
