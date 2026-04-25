import { useEffect, useMemo, useRef, useState } from 'react';
import { useChatSocket } from './hooks/useChatSocket.js';

// The WebSocket URL can be overridden via Vite env var `VITE_WS_URL`.
// Default points to the .NET backend running on :5000.
const DEFAULT_WS_URL = 'ws://localhost:5000/ws/chat';
const WS_URL = import.meta.env.VITE_WS_URL || DEFAULT_WS_URL;

// ─── Sub-components ──────────────────────────────────────────────────────────

function StatusBadge({ status }) {
  const label = {
    connecting: 'Connecting',
    open: 'Live',
    closed: 'Offline',
    error: 'Retrying',
  }[status] || 'Unknown';

  return (
    <span className={`status status--${status}`}>
      <span className="status__dot" />
      {label}
    </span>
  );
}

function MessageBubble({ message }) {
  const { role, text, intent, confidence, timestamp } = message;

  // System messages get their own quieter presentation.
  if (role === 'system') {
    return (
      <div className="msg msg--system" role="status">
        <span>{text}</span>
      </div>
    );
  }

  if (role === 'error') {
    return (
      <div className="msg msg--error" role="alert">
        <span className="msg__icon">⚠</span>
        <span>{text}</span>
      </div>
    );
  }

  const isUser = role === 'user';
  const time = timestamp
    ? new Date(timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
    : '';

  return (
    <div className={`msg ${isUser ? 'msg--user' : 'msg--bot'}`}>
      <div className="msg__bubble">
        <p className="msg__text">{text}</p>
        <div className="msg__meta">
          <span className="msg__time">{time}</span>
          {!isUser && intent && (
            <span className="msg__intent" title={`confidence ${Math.round((confidence ?? 0) * 100)}%`}>
              {intent}
            </span>
          )}
        </div>
      </div>
    </div>
  );
}

function TypingIndicator() {
  return (
    <div className="msg msg--bot">
      <div className="msg__bubble msg__bubble--typing" aria-label="Bot is typing">
        <span className="typing-dot" />
        <span className="typing-dot" />
        <span className="typing-dot" />
      </div>
    </div>
  );
}

// ─── App ─────────────────────────────────────────────────────────────────────

export default function App() {
  const { messages, status, isBotTyping, sendMessage, sessionId } = useChatSocket(WS_URL);
  const [draft, setDraft] = useState('');
  const scrollerRef = useRef(null);
  const inputRef = useRef(null);

  // Auto-scroll to the newest message whenever the list or typing state changes.
  useEffect(() => {
    const el = scrollerRef.current;
    if (!el) return;
    el.scrollTo({ top: el.scrollHeight, behavior: 'smooth' });
  }, [messages, isBotTyping]);

  // Focus the input once we're connected so the user can just start typing.
  useEffect(() => {
    if (status === 'open') inputRef.current?.focus();
  }, [status]);

  const shortSession = useMemo(
    () => (sessionId ? sessionId.slice(0, 8) : ''),
    [sessionId]
  );

  const handleSubmit = (e) => {
    e.preventDefault();
    if (sendMessage(draft)) setDraft('');
  };

  const canSend = status === 'open' && draft.trim().length > 0;

  return (
    <div className="app">
      <div className="aurora" aria-hidden="true" />

      <main className="chat">
        <header className="chat__header">
          <div className="chat__title-group">
            <span className="chat__mark" aria-hidden="true">✦</span>
            <div>
              <h1 className="chat__title">Dialogflow Chat</h1>
              <p className="chat__subtitle">
                Real-time over WebSocket · session <code>{shortSession}</code>
              </p>
            </div>
          </div>
          <StatusBadge status={status} />
        </header>

        <div className="chat__scroller" ref={scrollerRef}>
          {messages.length === 0 && status === 'open' && (
            <div className="chat__empty">
              <p className="chat__empty-title">Start a conversation</p>
              <p className="chat__empty-hint">
                Try: <em>"I need to book a flight"</em>
              </p>
            </div>
          )}

          {messages.map((m) => (
            <MessageBubble key={m.id} message={m} />
          ))}

          {isBotTyping && <TypingIndicator />}
        </div>

        <form className="chat__composer" onSubmit={handleSubmit}>
          <input
            ref={inputRef}
            type="text"
            className="chat__input"
            placeholder={
              status === 'open'
                ? 'Type a message…'
                : 'Waiting for connection…'
            }
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            disabled={status !== 'open'}
            aria-label="Message"
            autoComplete="off"
          />
          <button
            type="submit"
            className="chat__send"
            disabled={!canSend}
            aria-label="Send message"
          >
            <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M4 12l16-8-5 16-3-7-8-1z" />
            </svg>
          </button>
        </form>
      </main>

      <footer className="footer">    
      </footer>
    </div>
  );
}
