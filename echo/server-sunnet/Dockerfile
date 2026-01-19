# Build stage
FROM debian:13.2 AS builder

ENV DEBIAN_FRONTEND=noninteractive

# Install build dependencies
RUN apt-get update
RUN apt-get install -y build-essential
RUN apt-get install -y autoconf
RUN apt-get install -y cmake
RUN rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy source code (build/ is ignored via .dockerignore)
COPY . .

RUN cmake -B build && cmake --build build

FROM debian:13.2-slim

WORKDIR /app

COPY --from=builder /app .

# Expose port (adjust if needed)
EXPOSE 3010

# Run skynet
ENTRYPOINT ["./build/server-sunnet"]
