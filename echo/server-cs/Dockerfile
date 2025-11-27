# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder

WORKDIR /app

COPY *.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o out

# Run stage
FROM mcr.microsoft.com/dotnet/runtime:8.0

WORKDIR /app

COPY --from=builder /app/out .

EXPOSE 3010

ENTRYPOINT ["dotnet"]
CMD ["server-cs.dll"]
