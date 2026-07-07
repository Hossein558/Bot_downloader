# YTDLHub Bot (Telegram Video Downloader)

A robust, private Telegram Bot for downloading videos from YouTube, Instagram, TikTok, and Twitter. 
This bot runs completely on Docker and uses Cloudflare WARP and PO Tokens to bypass YouTube's anti-bot protections.

## Features
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

### 3. Start the Bot
Run the following Docker command to build the image and start all services (Bot, WARP Proxy, PO Token Generator):
```bash
docker compose up -d --build
```

That's it! The bot is now running. 
You can check the logs using:
```bash
docker compose logs -f ytdlhub-bot
```

---

## ⚙️ Configuration Details

Everything the bot needs to run is already committed to this repository.
- **Bot Token:** Hardcoded as an environment variable in `docker-compose.yml`.
- **Cookies:** The file `cookies.txt` is located in the root folder and automatically mounted into the container. If the bot ever stops downloading restricted YouTube videos, simply update `cookies.txt` with a fresh export.

### 🛑 Bypassing Telegram's 50MB Upload Limit

By default, the official Telegram Bot API restricts file uploads to a maximum of **50 MB**. If a downloaded video exceeds this size, the bot will fail to send it.

To bypass this and upload files up to **2 GB**, you must run a **Local Telegram Bot API Server**. We have already configured this in `docker-compose.yml`, but you need your own `API_ID` and `API_HASH` from Telegram.

**Steps to enable the Local API Server:**
1. Go to [my.telegram.org](https://my.telegram.org) and generate an `API_ID` and `API_HASH`.
2. Open `docker-compose.yml`.
3. Uncomment the `telegram-bot-api` service block.
4. Replace `YOUR_API_ID` and `YOUR_API_HASH` with your keys.
5. In the `ytdlhub-bot` service, uncomment `- telegram-bot-api` under `depends_on`.
6. Uncomment the `- Telegram__BaseUrl=http://telegram-bot-api:8081` environment variable.
7. Restart the stack: `docker compose up -d --build`

---

## 📁 Repository Structure
- `src/` : The C# .NET 9 source code for the bot.
- `docker-compose.yml` : Orchestrates the Bot, WARP proxy, and Sidecar services.
- `Dockerfile` : The build instructions for the .NET bot.
- `cookies.txt` : Authentication cookies for yt-dlp to bypass YouTube age/login restrictions.
