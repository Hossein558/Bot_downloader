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

# Install dependencies: python3, pip, ffmpeg, curl, unzip, Deno (default JS runtime for yt-dlp)
RUN apt-get update && apt-get install -y \
    python3 \
    python3-pip \
    ffmpeg \
    curl \
    unzip \
    && curl -fsSL https://deno.land/install.sh | sh \
    && mv /root/.deno/bin/deno /usr/local/bin/deno \
    && pip3 install --break-system-packages --no-cache-dir "yt-dlp[default]" \
    && apt-get remove -y curl unzip \
    && apt-get autoremove -y \
    && rm -rf /var/lib/apt/lists/*

# Copy built application
COPY --from=build-env /app/out .

ENTRYPOINT ["dotnet", "YTDLHub.Bot.dll"]
