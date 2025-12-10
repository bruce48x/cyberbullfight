# Build stage
FROM golang:1.24 AS builder

WORKDIR /app

COPY go.mod ./
RUN go mod download

COPY . .
RUN CGO_ENABLED=0 GOOS=linux go build -o server-go .

# Run stage
FROM debian:13.2-slim  

WORKDIR /app

COPY --from=builder /app/server-go .

EXPOSE 3010

ENTRYPOINT ["./server-go"]

