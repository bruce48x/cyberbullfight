FROM node:22.21.1 AS builder

WORKDIR /app

# Copy package files
COPY package.json yarn.lock* package-lock.json* ./

# Install production dependencies only
RUN if [ -f yarn.lock ]; then yarn install; \
    else npm ci; fi

# Copy source code
COPY . .

# Build the application
RUN npm run build

FROM node:22.21.1-slim

COPY package.json yarn.lock* package-lock.json* ./
RUN if [ -f yarn.lock ]; then yarn install --production; \
    else npm install --production; fi

COPY --from=builder /app/dist ./dist

# Run the application
ENTRYPOINT [ "node" ]
CMD ["dist/app.js"]
