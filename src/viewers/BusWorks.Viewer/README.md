# BusWorks.Viewer

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-blue)
![Blazor Server](https://img.shields.io/badge/Blazor-Server-purple?logo=blazor)

A self-hostable **Blazor Server** web application for inspecting and managing an **Azure Service Bus** namespace.  
It runs as a local web server and automatically opens in your default browser — no cloud deployment required.

---

## Table of contents

- [Quick start](#quick-start)
- [What it does](#what-it-does)
- [Prerequisites](#prerequisites)
- [Configuration](#configuration)
- [Running locally](#running-locally-development)
- [Publishing as a self-contained executable](#publishing-as-a-self-contained-executable)
- [Changing the port](#changing-the-port)
- [Security](#security)
- [Tech stack](#tech-stack)

---

## Quick start

```bash
# 1. Clone & run
dotnet run --project src/viewers/BusWorks.Viewer

# 2. Open http://localhost:5000 and go to Configuration
# 3. Paste your two Service Bus connection strings and click Save
# 4. Navigate to Queues or Topics — done
```

> **No .NET installation on the target machine?** See [Publishing as a self-contained executable](#publishing-as-a-self-contained-executable).

---

## What it does

BusWorks.Viewer gives you a full management UI for every queue and topic in your Service Bus namespace, all from a single executable.

### Queues

| Feature | Description |
|---|---|
| Overview | Live table of all queues with active, dead-letter, and total message counts |
| Create | Add a new queue with session support, max delivery count, lock duration, and TTL |
| Edit | Update status, max delivery count, lock duration, TTL, dead-letter on expiry, and batched operations. Read-only properties (partitioning, duplicate detection, max size) shown for reference |
| Rename | Creates a new queue with the updated settings and deletes the original. You are warned that existing messages are lost |
| Delete | Confirmation dialog before permanent removal |
| Send message | Post a JSON message to a queue. Session ID field is shown automatically when the queue requires sessions |

### Topics & Subscriptions

| Feature | Description |
|---|---|
| Overview | Live table of all topics with subscription count and aggregate message counts |
| Create | Add a new topic with TTL and batched operations |
| Edit | Update status, TTL, and batched operations. Read-only properties (partitioning, duplicate detection, max size) shown for reference |
| Delete | Confirmation dialog before permanent removal |
| Send message | Post a JSON message to a topic. The dialog loads all subscriptions on open and automatically shows the Session ID field only when at least one subscription requires sessions — with a named warning listing exactly which subscriptions are affected |
| Subscription overview | Per-topic detail page listing every subscription with message counts and settings |
| Create subscription | Add a subscription with session support, max delivery count, lock duration, and dead-letter settings |
| Edit subscription | Update status, max delivery count, lock duration, TTL, dead-letter on expiry, and batched operations |
| Delete subscription | Confirmation dialog before permanent removal |

### General

- **Live message counts** — peek-based counts capped at 250 (displayed as `250+`)
- **Formatted JSON editor** — real-time syntax validation and a formatted preview panel when sending messages
- **Dark / light mode** — toggle from the Configuration page, persisted locally
- **In-app configuration** — enter connection strings through the UI; no file editing required
- **Persistent settings** — saved to `user-settings.json` next to the executable, layered on top of `appsettings.json`

---

## Prerequisites

| Requirement | Version |
|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0 or later |
| Azure Service Bus namespace | Standard or Premium tier |

> **Emulator:** The [Azure Service Bus emulator](https://learn.microsoft.com/azure/service-bus-messaging/overview-emulator) is fully supported for local development.

---

## Configuration

The application requires **two connection strings**:

| Setting | Purpose |
|---|---|
| `AdministrationConnectionString` | Manage entities — create, update, delete queues/topics/subscriptions. Requires `Manage` claim. |
| `ClientConnectionString` | Send and receive messages (peek). Requires `Send` + `Listen` claims. |

### Option A — in-app (recommended)

Start the application and navigate to **Configuration** in the left menu. Enter the connection strings and click **Save**. They are written to `user-settings.json` alongside the executable and loaded on every subsequent start.

### Option B — appsettings.json

Edit `appsettings.json` before running:

```json
{
  "ServiceBus": {
    "AdministrationConnectionString": "Endpoint=sb://<namespace>.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=<key>",
    "ClientConnectionString": "Endpoint=sb://<namespace>.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=<key>"
  }
}
```

### Option C — environment variables

```bash
ServiceBus__AdministrationConnectionString="Endpoint=sb://..."
ServiceBus__ClientConnectionString="Endpoint=sb://..."
```

---

## Running locally (development)

```bash
# From the repository root
dotnet run --project src/viewers/BusWorks.Viewer
```

The application starts on `https://localhost:5001` / `http://localhost:5000`.

---

## Publishing as a self-contained executable

A self-contained publish bundles the .NET 10 runtime into a single file — **no .NET installation is needed** on the target machine.

### Windows (x64)

**bash / zsh (macOS, Linux, Git Bash)**
```bash
dotnet publish src/viewers/BusWorks.Viewer \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishReadyToRun=true \
  -o ./publish/win-x64
```

**PowerShell**
```powershell
dotnet publish src/viewers/BusWorks.Viewer `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishReadyToRun=true `
  -o ./publish/win-x64
```

Output: `./publish/win-x64/BusWorks.Viewer.exe`

### Windows (ARM64)

**bash / zsh**
```bash
dotnet publish src/viewers/BusWorks.Viewer \
  -c Release \
  -r win-arm64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishReadyToRun=true \
  -o ./publish/win-arm64
```

**PowerShell**
```powershell
dotnet publish src/viewers/BusWorks.Viewer `
  -c Release `
  -r win-arm64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishReadyToRun=true `
  -o ./publish/win-arm64
```

### macOS (Apple Silicon)

```bash
dotnet publish src/viewers/BusWorks.Viewer \
  -c Release \
  -r osx-arm64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishReadyToRun=true \
  -o ./publish/osx-arm64
```

### macOS (Intel)

```bash
dotnet publish src/viewers/BusWorks.Viewer \
  -c Release \
  -r osx-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishReadyToRun=true \
  -o ./publish/osx-x64
```

### Linux (x64)

```bash
dotnet publish src/viewers/BusWorks.Viewer \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishReadyToRun=true \
  -o ./publish/linux-x64
```

> **`-p:PublishReadyToRun=true`** pre-compiles IL to native code for faster cold-start times. Remove it if you need a smaller binary.

### Running the published executable

**Windows**
```
BusWorks.Viewer.exe
```

**macOS / Linux** — make executable first:
```bash
chmod +x ./BusWorks.Viewer
./BusWorks.Viewer
```

The application starts on `http://localhost:5000` and **automatically opens your default browser**.

### Files next to the executable

| File | Purpose |
|---|---|
| `appsettings.json` | Default configuration — edit before first run to pre-seed connection strings |
| `user-settings.json` | Created automatically when you save settings through the UI. Takes precedence over `appsettings.json` |

> Keep `appsettings.json` and `user-settings.json` (if present) **in the same directory** as the executable. The application reads them from its working directory on startup.

---

## Changing the port

By default the published build listens on `http://localhost:5000`. To use a different port, either:

**Environment variable:**
```bash
ASPNETCORE_URLS=http://localhost:8080 ./BusWorks.Viewer
```

**Or in `appsettings.json`:**
```json
{
  "Urls": "http://localhost:8080"
}
```

Remember to also update the browser auto-open URL in `Program.cs` if you rebuild from source.

---

## Security

`user-settings.json` is created automatically when you save connection strings through the UI. It contains credentials in plain text and **must not be committed to source control**.

Add it to your `.gitignore`:

```gitignore
# BusWorks.Viewer — local user overrides (may contain connection strings)
user-settings.json
```

Additional recommendations:

- Use a **dedicated shared-access policy** with only the permissions the viewer needs (`Manage` for the admin client, `Send` + `Listen` for the messaging client) rather than the root `RootManageSharedAccessKey`.
- The viewer is intended as a **local developer tool** and listens on `localhost` only. Do not expose it to a network interface in a shared or production environment.

---

## Tech stack

| Library | Role |
|---|---|
| ASP.NET Core 10 Blazor Server | UI framework — interactive server-side rendering |
| [MudBlazor 9](https://mudblazor.com) | Component library (tables, dialogs, forms, snackbars) |
| [Azure.Messaging.ServiceBus 7](https://github.com/Azure/azure-sdk-for-net/tree/main/sdk/servicebus) | Administration + messaging client |


