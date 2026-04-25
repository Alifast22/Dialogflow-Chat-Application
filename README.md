**#----------------------BACKEND----------------------**

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








**#--------------------------FRONTEND----------------------------**



Dialogflow Chat UI (Frontend)
A React chat interface that speaks to the .NET WebSocket backend, which in turn talks to Google Dialogflow ES. Built with Vite for fast local dev and a tiny production bundle.

Features
Real-time WebSocket messaging (client ↔ server ↔ Dialogflow ES)
Stable per-tab session id (so Dialogflow tracks multi-turn context)
Automatic reconnect with exponential backoff
Connection status indicator (live / retrying / offline)
Typing indicator while the bot is thinking
Optimistic rendering of the user's own messages
Inline intent + confidence tags on bot replies (debug aid)
Responsive design (desktop + mobile)
Graceful error messaging for socket drops and malformed frames
Project structure
dialogflow-chat-ui/
├── index.html                # Vite HTML entry point (loads fonts)
├── package.json
├── vite.config.js
└── src/
    ├── main.jsx              # React bootstrap
    ├── App.jsx               # ChatPanel, MessageBubble, TypingIndicator
    ├── styles.css            # Design tokens + component styles
    └── hooks/
        └── useChatSocket.js  # WebSocket lifecycle + reconnect logic
Setup
1. Install dependencies
cd dialogflow-chat-ui
npm install
2. (Optional) Configure the backend URL
By default the app connects to ws://localhost:5000/ws/chat. To point at a different host, create a .env.local file:

VITE_WS_URL=ws://my-backend.example.com:5000/ws/chat
3. Run the dev server
npm run dev
Open http://localhost:5173. The UI will attempt to open a WebSocket to the backend and show a status badge in the header.


How it works
The useChatSocket hook owns the WebSocket instance for the lifetime of the app. When the socket closes (network blip, backend restart) it waits 1s, 2s, 4s, 8s… up to 10s before reconnecting — so transient issues recover automatically without hammering the server.

Messages are stored in React state with client-generated UUIDs, so the list survives re-renders and animates in smoothly via CSS @keyframes fade-up.

The session id is stored in sessionStorage so a full-page refresh keeps the conversation going, but opening the app in a new tab starts a fresh conversation with Dialogflow.
