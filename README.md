# YTDLHub (Telegram Video Downloader & Web Dashboard)

A robust, private Telegram Bot and Blazor Web Panel for downloading videos from YouTube, Instagram, TikTok, and Twitter. 
This unified application runs completely on Docker and uses Cloudflare WARP and PO Tokens to bypass YouTube's anti-bot protections.

## Features
- **Web Dashboard:** A premium Syncfusion-powered Blazor web UI (Port 8080) to monitor and start downloads.
- **Telegram Bot:** Direct integration for users to request downloads via chat.
- **YouTube Anti-Ban:** Routes YouTube downloads through a local Cloudflare WARP SOCKS5 proxy to appear as a residential user.
- **PO Tokens:** Automatically generates YouTube Proof-of-Origin tokens.
- **Cookie Support:** Uses `cookies.txt` for authenticated downloads.
- **Async & Fast:** Built on .NET 9 and `yt-dlp`.

---

## 🚀 How to Deploy (From Scratch)

If the server is ever deleted, you can bring the entire bot back online in **less than 2 minutes** using these exact commands:

### 1. Connect to the Server
SSH into your Linux server (Ubuntu/Debian recommended). Ensure Docker and Git are installed.

### 2. Clone this Repository
```bash
git clone https://github.com/Hossein558/Bot_downloader.git
cd Bot_downloader
```

### 3. Start the Bot & Dashboard
Run the following Docker command to build the image and start all services (Web, Bot, WARP Proxy, PO Token Generator):
```bash
docker compose up -d --build
```

That's it! 
- The **Bot** is now running on Telegram.
- The **Web Dashboard** is available at `http://<your-server-ip>:8080`.

You can check the logs using:
```bash
docker compose logs -f ytdlhub-web
```

---

## ⚙️ Configuration Details

Everything the application needs to run is already committed to this repository.
- **Bot Token & Syncfusion License:** Configured as environment variables or `appsettings.json`.
- **Cookies:** The file `cookies.txt` is located in the root folder and automatically mounted into the container.

### 🛑 Bypassing Telegram's 50MB Upload Limit

By default, the official Telegram Bot API restricts file uploads to a maximum of **50 MB**. If a downloaded video exceeds this size, the bot will fail to send it.

To bypass this and upload files up to **2 GB**, you must run a **Local Telegram Bot API Server**. We have already configured this in `docker-compose.yml`, but you need your own `API_ID` and `API_HASH` from Telegram.

**Steps to enable the Local API Server:**
1. Go to [my.telegram.org](https://my.telegram.org) and generate an `API_ID` and `API_HASH`.
2. Open `docker-compose.yml`.
3. Uncomment the `telegram-bot-api` service block.
4. Replace `YOUR_API_ID` and `YOUR_API_HASH` with your keys.
5. In the `ytdlhub-web` service, uncomment `- telegram-bot-api` under `depends_on`.
6. Uncomment the `- Telegram__BaseUrl=http://telegram-bot-api:8081` environment variable.
7. Restart the stack: `docker compose up -d --build`

---

## 📁 Repository Structure
- `src/YTDLHub.Web/` : The Blazor Web Dashboard project (Entry point).
- `src/YTDLHub.Bot/` : The Telegram Bot logic and background worker.
- `src/YTDLHub.Core/` & `src/YTDLHub.Infrastructure/` : Shared models and `yt-dlp` services.
- `docker-compose.yml` : Orchestrates the App, WARP proxy, and Sidecar services.
- `Dockerfile` : The build instructions for the .NET application.
- `cookies.txt` : Authentication cookies for yt-dlp to bypass YouTube age/login restrictions.
