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

WORKDIR /app

# Copy only production dependencies from the build stage
COPY --from=builder /app/package*.json yarn.lock* ./
RUN if [ -f yarn.lock ]; then yarn install --production; \
    else npm install --production; fi

COPY --from=builder /app/dist ./dist

# Expose port (adjust if needed)
EXPOSE 3010

WORKDIR /app/dist

# Run the application
ENTRYPOINT ["node"]
CMD ["app.js"]
