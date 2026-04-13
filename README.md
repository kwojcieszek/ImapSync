# ImapSync

A self-hosted email synchronization service that continuously mirrors messages from a source IMAP mailbox to one or more destination mailboxes, preserving the full folder structure.

## Overview

ImapSync runs as a .NET background worker that periodically connects to configured IMAP accounts, detects new messages, and copies them to all configured destinations. A Node.js web UI provides a management interface for configuring mailbox pairs, sync settings, and monitoring the service.

## Architecture

| Project | Description |
|---|---|
| `ImapSync.Core` | Domain interfaces and models |
| `ImapSync.Application` | Sync business logic — scheduling, caching, state tracking |
| `ImapSync.Infrastructure` | MailKit-based IMAP connections and SMTP notifications |
| `ImapSync.Worker` | .NET hosted background service — runs sync cycles |
| `ImapSync.Web` | Express.js web UI for management |
| `ImapSync.Tests` | Unit tests |

## Features

- **One-to-many sync** — one source mailbox → multiple destinations
- **Incremental sync** — only new messages are copied; full sync on first run
- **Folder mirroring** — destination folder structure is created automatically
- **Deduplication** — in-memory daily cache + server-side verification before each copy
- **Post-append verification** — confirms each message was saved on the destination after upload
- **SMTP error notifications** — optional email alerts on sync failure
- **Persistent state** — last-sync timestamps stored in a JSON state file
- **Web UI** — manage mailbox pairs, adjust settings, restart the service
- **Multi-language UI** — English, Polish, German, French, Spanish
- **Audit log** — daily log files recording all management actions

## Tech Stack

- **.NET 10** · MailKit 4 · Serilog · Microsoft.Extensions.Hosting
- **Node.js** · Express 4 · Alpine.js · Tailwind CSS

## Configuration

Mailbox pairs and sync settings are stored in `ImapSync.Worker/mailboxes.json`:

```json
{
  "SyncSettings": {
    "IntervalMinutes": 5,
    "StateFilePath": "sync-state.json",
    "ErrorNotification": {
      "Host": "smtp.example.com",
      "Port": 587,
      "UseSsl": false,
      "Username": "alerts@example.com",
      "Password": "",
      "From": "alerts@example.com",
      "To": "admin@example.com"
    },
    "MailboxPairs": [
      {
        "Name": "example",
        "Source": {
          "Host": "imap.source.com",
          "Port": 993,
          "UseSsl": true,
          "Username": "user@source.com",
          "Password": "secret"
        },
        "Destinations": [
          {
            "Host": "imap.dest.com",
            "Port": 993,
            "UseSsl": true,
            "Username": "user@dest.com",
            "Password": "secret"
          }
        ]
      }
    ]
  }
}
```

Web UI settings are in `ImapSync.Web/config.json`:

```json
{
  "port": 5000,
  "sessionSecret": "change-this-in-production",
  "mailboxesJsonPath": "../ImapSync.Worker/mailboxes.json",
  "logDir": "./logs",
  "restartCommand": "systemctl restart {service}"
}
```

## Running

**Worker service:**
```bash
cd ImapSync.Worker
dotnet run
```

**Web UI:**
```bash
cd ImapSync.Web
npm install
npm start        # production
npm run dev      # development (nodemon)
```

The web UI is available at `http://localhost:5000`. Default credentials: `admin` / `admin`.

## Running as a Linux Service

Create `/etc/systemd/system/imapsync.service`:

```ini
[Unit]
Description=ImapSync Worker
After=network.target

[Service]
WorkingDirectory=/opt/imapsync/ImapSync.Worker
ExecStart=/usr/bin/dotnet ImapSync.Worker.dll
Restart=always

[Install]
WantedBy=multi-user.target
```

```bash
systemctl enable imapsync
systemctl start imapsync
```

Set `restartCommand` in `config.json` to `systemctl restart imapsync` so the web UI can restart the service.
