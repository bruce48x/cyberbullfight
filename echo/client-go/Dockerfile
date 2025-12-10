# Build stage
FROM golang:1.24 AS builder

WORKDIR /app

# Copy go mod files
COPY go.mod go.sum* ./
RUN go mod download

# Copy source code
COPY . .

# Build the application
RUN CGO_ENABLED=0 GOOS=linux go build -a -installsuffix cgo -o client-go .

FROM debian:13.2-slim  
WORKDIR /app
COPY --from=builder /app/client-go .

# Run the application
CMD ["./client-go"]
