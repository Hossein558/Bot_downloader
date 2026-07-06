# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-env
WORKDIR /app

# Copy csproj files and restore dependencies
COPY YTDLHub.sln ./
COPY src/YTDLHub.Core/YTDLHub.Core.csproj src/YTDLHub.Core/
COPY src/YTDLHub.Infrastructure/YTDLHub.Infrastructure.csproj src/YTDLHub.Infrastructure/
COPY src/YTDLHub.Bot/YTDLHub.Bot.csproj src/YTDLHub.Bot/

# Restore packages
RUN dotnet restore YTDLHub.sln

# Copy everything else and build release
COPY . ./
RUN dotnet publish src/YTDLHub.Bot/YTDLHub.Bot.csproj -c Release -o out

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS runtime
WORKDIR /app

# Install dependencies: python3, ffmpeg, curl
RUN apt-get update && apt-get install -y \
    python3 \
    ffmpeg \
    curl \
    && curl -L https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -o /usr/local/bin/yt-dlp \
    && chmod a+rx /usr/local/bin/yt-dlp \
    && rm -rf /var/lib/apt/lists/*

# Copy built application
COPY --from=build-env /app/out .

ENTRYPOINT ["dotnet", "YTDLHub.Bot.dll"]
