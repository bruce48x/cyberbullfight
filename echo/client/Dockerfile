FROM node:22.21.1

WORKDIR /app

# Copy package files
COPY package.json yarn.lock* package-lock.json* ./

# Install production dependencies only
RUN if [ -f yarn.lock ]; then yarn install; \
    else npm instal; fi

# Copy source code
COPY . .

# Build the application
RUN npm run build

# Run the application
ENTRYPOINT [ "node" ]
CMD ["dist/app.js"]
