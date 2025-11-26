# Build stage
FROM debian:13.2

ENV DEBIAN_FRONTEND=noninteractive

# Install build dependencies
RUN apt-get update
RUN apt-get install -y build-essential
RUN apt-get install -y autoconf
RUN apt-get install -y libreadline8
RUN rm -rf /var/lib/apt/lists/*

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
