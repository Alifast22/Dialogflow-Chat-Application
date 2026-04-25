#----------------------BACKEND----------------------

# Dialogflow Chat API (Backend)

.NET 8 Web API that bridges a browser-based chat UI and **Google Dialogflow ES** over a **WebSocket** connection.

## Architecture

```
┌────────────────┐  WebSocket   ┌─────────────────────────┐  HTTPS / gRPC  ┌──────────────┐
│  React Client  │◄────────────►│  .NET 8 WebSocket API   │◄──────────────►│ Dialogflow ES│
└────────────────┘              │                         │                └──────────────┘
                                │  /api/webhook (POST) ◄──── Fulfillment ───┘
                                └─────────────────────────┘
```

- **`/ws/chat`** – WebSocket endpoint the React UI connects to.
- **`/api/webhook`** – Public HTTPS endpoint that Dialogflow calls for intents with fulfillment enabled.
- **`/health`** – Liveness probe.

## Project layout

```
DialogflowChatApi/
├── Program.cs                          # Entry point, pipeline, WebSocket mount
├── Controllers/
│   └── WebhookController.cs            # Dialogflow fulfillment webhook
├── Services/
│   ├── IDialogflowService.cs           # Abstraction
│   └── DialogflowService.cs            # SessionsClient wrapper
├── WebSockets/
│   ├── ConnectionManager.cs            # Tracks open sockets
│   └── ChatWebSocketHandler.cs         # Per-connection receive loop
├── Models/
│   └── ChatMessages.cs                 # Inbound/Outbound message envelopes
├── Properties/
│   └── launchSettings.json
├── appsettings.json
├── appsettings.Development.json
└── DialogflowChatApi.csproj
```

## Prerequisites

1. [.NET 8 SDK](https://dotnet.microsoft.com/download)
2. A Google Cloud project with Dialogflow ES enabled
3. A service-account JSON key with the role **Dialogflow API Client** (or **Dialogflow API Admin**)

## Setup

### 1. Restore and build

```bash
cd DialogflowChatApi
dotnet restore
dotnet build
```

### 2. Configure Dialogflow

Drop your service-account JSON key next to `Program.cs` as `dialogflow-credentials.json` (this filename is already in `.gitignore`).

Edit `appsettings.json`:

```json
{
  "Dialogflow": {
    "ProjectId": "your-gcp-project-id",
    "LanguageCode": "en-US",
    "CredentialsPath": "dialogflow-credentials.json"
  }
}
```

Alternatively, set the standard env var and leave `CredentialsPath` empty:

```bash
export GOOGLE_APPLICATION_CREDENTIALS=/absolute/path/to/credentials.json
```

### 3. Run

```bash
dotnet run
```

The server listens on `http://localhost:5000` by default. The WebSocket endpoint will be at:

```
ws://localhost:5000/ws/chat
```

### 4. (Optional) Expose the fulfillment webhook

Dialogflow can only reach your webhook over public HTTPS. For local development, use [ngrok](https://ngrok.com/):

```bash
ngrok http 5000
```

## Local environment: Copy .env.example to .env and fill in your credentials path.
bashcp .env.example .env
# then edit .env with your real path









#--------------------------FRONTEND----------------------------
