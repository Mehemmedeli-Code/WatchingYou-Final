import { useEffect, useRef, useState } from "react";
import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from "@microsoft/signalr";
import { auth } from "@/lib/api";

/**
 * One SignalR connection per tab, shared by every chat on the page.
 *
 * Messages still go out over plain HTTP (the POST endpoints validate and save them); the
 * socket only carries the nudge "something new arrived" plus typing notices. That keeps a
 * dropped connection harmless: the page falls back to polling and nothing is lost.
 */
export type RealtimeEvent =
  | "dm"          // { fromUserId, fromName }
  | "typing"      // { fromUserId, fromName }
  | "help"        // { conversationId } — to the customer who owns the conversation
  | "helpDesk"    // { conversationId } — to every Security/Admin connection
  | "helpTyping"; // { conversationId, fromDesk, name }

let connection: HubConnection | null = null;
let starting: Promise<void> | null = null;
const stateListeners = new Set<(live: boolean) => void>();

function announceState() {
  const live = connection?.state === HubConnectionState.Connected;
  for (const listener of stateListeners) listener(live);
}

function build(): HubConnection {
  const hub = new HubConnectionBuilder()
    // Absolute, so the client never has to guess the base (it cannot outside a browser).
    .withUrl(new URL("/hubs/chat", window.location.href).href, {
      // Browsers cannot put an Authorization header on a WebSocket upgrade, so the token
      // travels as ?access_token= and the server reads it only on /hubs paths.
      accessTokenFactory: async () => (await auth.ensureToken()) ?? "",
      withCredentials: true,
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(LogLevel.Warning)
    .build();

  hub.onreconnecting(announceState);
  hub.onreconnected(announceState);
  hub.onclose(announceState);
  return hub;
}

export async function startRealtime(): Promise<void> {
  if (!auth.isSignedIn) return;
  try {
    if (!connection) connection = build();
  } catch {
    return;                           // no real-time here; polling still works
  }
  if (connection.state !== HubConnectionState.Disconnected) return;
  if (starting) return starting;

  starting = connection
    .start()
    .then(announceState)
    .catch(() => announceState())      // polling carries on regardless
    .finally(() => { starting = null; });
  return starting;
}

export function stopRealtime() {
  void connection?.stop();
  connection = null;
  announceState();
}

/** Fire-and-forget call to a hub method; silently skipped when the socket is down. */
export function invokeRealtime(method: string, ...args: unknown[]) {
  if (connection?.state === HubConnectionState.Connected) {
    void connection.invoke(method, ...args).catch(() => undefined);
  }
}

/** Subscribes to one server event for the lifetime of the component. */
export function useRealtime<T>(event: RealtimeEvent, handler: (payload: T) => void) {
  const saved = useRef(handler);
  saved.current = handler;

  useEffect(() => {
    void startRealtime();
    const listener = (payload: T) => saved.current(payload);
    const hub = connection;
    hub?.on(event, listener);
    return () => hub?.off(event, listener);
  }, [event]);
}

/** True while the socket is connected. Chats use it to slow their polling right down. */
export function useRealtimeLive(): boolean {
  const [live, setLive] = useState(connection?.state === HubConnectionState.Connected);
  useEffect(() => {
    stateListeners.add(setLive);
    void startRealtime().then(() => setLive(connection?.state === HubConnectionState.Connected));
    return () => { stateListeners.delete(setLive); };
  }, []);
  return live;
}

/**
 * Typing notices, rate-limited so a fast typist sends one every few seconds rather than one
 * per key. Returns the function to call from the input's change handler.
 */
export function useTypingSender(method: string, ...args: unknown[]) {
  const last = useRef(0);
  const argsRef = useRef(args);
  argsRef.current = args;
  return () => {
    const now = Date.now();
    if (now - last.current < 2500) return;
    last.current = now;
    invokeRealtime(method, ...argsRef.current);
  };
}

/** Shows "is typing" for a few seconds after the last notice, then lets it lapse. */
export function useTypingIndicator(): [string | null, (name: string) => void] {
  const [name, setName] = useState<string | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(() => () => { if (timer.current) clearTimeout(timer.current); }, []);
  return [
    name,
    (who: string) => {
      setName(who);
      if (timer.current) clearTimeout(timer.current);
      timer.current = setTimeout(() => setName(null), 4000);
    },
  ];
}

/** Which direct chats are open in this tab, so an incoming-message toast is not shown for
 *  a conversation the reader is already looking at. */
export const openDirectChats = new Set<string>();
