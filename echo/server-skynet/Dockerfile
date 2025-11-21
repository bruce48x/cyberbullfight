# Build stage
FROM debian:12.12 AS builder

ENV DEBIAN_FRONTEND=noninteractive

# Install build dependencies
RUN apt-get update && apt-get install -y \
    build-essential \
    gcc \
    g++ \
    make \
    git \
    libreadline8 \
    autoconf \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy source code
COPY . .

# Build skynet (this will build lua, jemalloc, and all skynet components)
WORKDIR /app/skynet
RUN make linux

# Build cjson for the application
WORKDIR /app
RUN make

# Expose port (adjust if needed)
EXPOSE 3010

# Run skynet
ENTRYPOINT ["./skynet/skynet"]
CMD ["./etc/config"]
