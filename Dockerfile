# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-env
WORKDIR /app

# Copy csproj files and restore dependencies
COPY YTDLHub.sln ./
COPY src/YTDLHub.Core/YTDLHub.Core.csproj src/YTDLHub.Core/
COPY src/YTDLHub.Infrastructure/YTDLHub.Infrastructure.csproj src/YTDLHub.Infrastructure/
COPY src/YTDLHub.Bot/YTDLHub.Bot.csproj src/YTDLHub.Bot/
COPY src/YTDLHub.Web/YTDLHub.Web.csproj src/YTDLHub.Web/

RUN dotnet restore YTDLHub.sln

COPY . ./
RUN dotnet publish src/YTDLHub.Web/YTDLHub.Web.csproj -c Release -o out

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install system packages: python3, pip, ffmpeg, curl, unzip (for Deno installer)
RUN apt-get update && apt-get install -y \
    python3 \
    python3-pip \
    ffmpeg \
    curl \
    unzip \
    && curl -fsSL https://deno.land/install.sh | sh \
    && mv /root/.deno/bin/deno /usr/local/bin/deno \
    && pip3 install --break-system-packages --no-cache-dir \
        "yt-dlp[default]" \
        "bgutil-ytdlp-pot-provider" \
        "gallery-dl" \
    && apt-get remove -y curl unzip \
    && apt-get autoremove -y \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build-env /app/out .

EXPOSE 8080

ENTRYPOINT ["dotnet", "YTDLHub.Web.dll"]
