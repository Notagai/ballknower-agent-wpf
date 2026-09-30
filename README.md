# 🏀 Ballknower

**Your desktop. Your AI. Your control.**

Ballknower is a lightweight Windows desktop AI assistant built with **WPF and .NET 8**. Bring your own API key, chat with an AI that can use local tools, and keep the assistant close at hand with a sleek, adaptive desktop interface.

## ✨ Features

### 💬 AI Chat & Local Memory
- **Integrated AI chat** in a dedicated, streamlined desktop interface.
- **Local conversation context** to keep the thread of your chats close to home.

### 🔐 Bring Your Own Key — Secured
- **BYOK API integration:** connect using your own API credentials.
- **Windows DPAPI protection** helps keep saved credentials protected on your device.

### 🛠️ Useful Desktop Tools
Give the assistant practical ways to help with everyday tasks:
- **Create, read, edit, and delete files** using local file tools.
- **Move files** between locations.
- **Create shortcuts** and **open applications**, making it easier to work with your desktop.

### ☁️ Google Drive
- **Optional Google Drive integration** with explicit connection from Settings.
- **Broad Drive visibility** so the agent can search and read the user's Drive.
- **Write operations are always confirmation-gated** before Ballknower sends the mutation to Google Drive.
- **Encrypted local OAuth token storage** using Windows DPAPI.
- **Clear connection errors** when Drive is unavailable or authorization fails.
- OAuth client files stay local and are never part of the repository.

### 🎨 Adaptive Desktop Experience
- **Adaptive colors** respond to the background graphics behind the assistant, helping the interface stay legible as its surroundings change.
- **Background blur** gives the window a polished, glass-like presence over your desktop.
- A compact, modern interface designed to feel at home on Windows.

## 🌐 Website & Documentation

Visit the [Ballknower website](https://notagai.github.io/ballknower-agent-wpf/) for the project overview and documentation.

## ☁️ Google Drive Documentation

See [Google Drive Integration](docs/GOOGLE_DRIVE.md) for setup, connection behavior, security notes, troubleshooting, and public-release considerations.

## 🧰 Project

| | |
|---|---|
| **Platform** | Windows |
| **Framework** | .NET 8 · WPF |
| **Repository** | [Notagai/ballknower-agent-wpf](https://github.com/Notagai/ballknower-agent-wpf) |

---
*Built for a more capable, more personal Windows desktop.*
