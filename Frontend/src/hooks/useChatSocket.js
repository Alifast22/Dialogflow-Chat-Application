import { useCallback, useEffect, useRef, useState } from 'react';

/**
 * Stable per-browser session id so Dialogflow can maintain multi-turn context.
 * We store it in sessionStorage (wiped when the tab closes) so a refresh keeps
 * the same conversation, but a new tab starts fresh.
 */
function getSessionId() {
  const KEY = 'dialogflow.sessionId';
  try {
    let id = sessionStorage.getItem(KEY);
    if (!id) {
      id = crypto.randomUUID();
      sessionStorage.setItem(KEY, id);
    }
    return id;
  } catch {
    // Private mode / storage disabled — use a volatile id.
    return crypto.randomUUID();
  }
}

/**
 * Manages the WebSocket connection to the backend.
 *
 * Responsibilities:
 *  - open / reopen the socket, with exponential backoff on failure
 *  - expose a `sendMessage(text)` function that writes JSON envelopes
 *  - expose `messages`, `status`, and `error` for the UI to render
 *
 * The backend contract is documented in the backend README:
 *   client → server: { sessionId, text }
 *   server → client: { type: 'bot' | 'error' | 'system', text, intent?, confidence? }
 */
export function useChatSocket(url) {
  const [messages, setMessages] = useState([]);
  const [status, setStatus] = useState('connecting'); // connecting | open | closed | error
  const [isBotTyping, setIsBotTyping] = useState(false);

  const socketRef = useRef(null);
  const reconnectTimerRef = useRef(null);
  const reconnectAttemptsRef = useRef(0);
  const sessionIdRef = useRef(getSessionId());
  const unmountedRef = useRef(false);

  const appendMessage = useCallback((msg) => {
    setMessages((prev) => [
      ...prev,
      { id: crypto.randomUUID(), timestamp: new Date().toISOString(), ...msg }
    ]);
  }, []);

  const connect = useCallback(() => {
    if (unmountedRef.current) return;

    setStatus('connecting');

    let ws;
    try {
      ws = new WebSocket(url);
    } catch (err) {
      // Bad URL, blocked by browser, etc.
      console.error('WebSocket construction failed', err);
      setStatus('error');
      scheduleReconnect();
      return;
    }

    socketRef.current = ws;

    ws.addEventListener('open', () => {
      reconnectAttemptsRef.current = 0;
      setStatus('open');
    });

    ws.addEventListener('message', (event) => {
      let parsed;
      try {
        parsed = JSON.parse(event.data);
      } catch {
        console.warn('Received non-JSON frame:', event.data);
        return;
      }

      // The server echoes back different `type`s — we route them into
      // one normalized message stream for the UI.
      setIsBotTyping(false);

      appendMessage({
        role: parsed.type === 'bot' ? 'bot'
            : parsed.type === 'error' ? 'error'
            : 'system',
        text: parsed.text || '',
        intent: parsed.intent,
        confidence: parsed.confidence,
      });
    });

    ws.addEventListener('error', (event) => {
      console.warn('WebSocket error', event);
      setStatus('error');
    });

    ws.addEventListener('close', (event) => {
      console.info('WebSocket closed', event.code, event.reason);
      setStatus('closed');
      setIsBotTyping(false);

      // Only schedule a reconnect if this wasn't a deliberate unmount.
      if (!unmountedRef.current) {
        scheduleReconnect();
      }
    });
  }, [url, appendMessage]);

  const scheduleReconnect = useCallback(() => {
    if (unmountedRef.current) return;

    // Cap the backoff at 10 seconds to avoid 30s stalls on transient blips.
    const attempt = reconnectAttemptsRef.current;
    const delay = Math.min(1000 * 2 ** attempt, 10_000);
    reconnectAttemptsRef.current = attempt + 1;

    clearTimeout(reconnectTimerRef.current);
    reconnectTimerRef.current = setTimeout(connect, delay);
  }, [connect]);

  useEffect(() => {
    unmountedRef.current = false;
    connect();

    return () => {
      unmountedRef.current = true;
      clearTimeout(reconnectTimerRef.current);
      if (socketRef.current && socketRef.current.readyState <= WebSocket.OPEN) {
        socketRef.current.close(1000, 'Component unmounting');
      }
    };
  }, [connect]);

  const sendMessage = useCallback((text) => {
    const trimmed = (text || '').trim();
    if (!trimmed) return false;

    const ws = socketRef.current;
    if (!ws || ws.readyState !== WebSocket.OPEN) {
      appendMessage({
        role: 'error',
        text: 'Not connected. Waiting to reconnect…'
      });
      return false;
    }

    // Optimistically render the user's own message immediately.
    appendMessage({ role: 'user', text: trimmed });

    try {
      ws.send(JSON.stringify({
        sessionId: sessionIdRef.current,
        text: trimmed,
      }));
      setIsBotTyping(true);
      return true;
    } catch (err) {
      console.error('Send failed', err);
      appendMessage({ role: 'error', text: 'Failed to send message.' });
      return false;
    }
  }, [appendMessage]);

  return { messages, status, isBotTyping, sendMessage, sessionId: sessionIdRef.current };
}
