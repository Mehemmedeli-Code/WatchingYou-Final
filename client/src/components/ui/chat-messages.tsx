import { useCallback, useEffect, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { AnimatePresence, motion } from "motion/react";
import { Send, ShieldCheck, Smile } from "lucide-react";
import { cn } from "@/lib/utils";
import { t } from "@/lib/i18n";

/**
 * Chat surface, adapted from the 21st.dev chat-messages component.
 *
 * Used by the Help service, where the other side is a person at the Security desk rather than
 * a model — which is why the demo's scripted transcript and fabricated reply had to go: a
 * support window that invents an answer on a timer is worse than one that says nothing.
 *
 * What changed, and why:
 *
 *  - `"use client"` removed — a Next.js marker with no meaning in a Vite build.
 *  - `framer-motion` swapped for `motion/react`. It is the same library under its current
 *    name, already installed here; adding framer-motion would have shipped two copies of it.
 *  - The demo's auto-play, scripted transcript and fake assistant reply are gone. They exist
 *    to make a screenshot look alive; here the replies are real, and a component that invents
 *    a plausible-sounding answer on a timer is the opposite of what a support box should do.
 *  - Indigo/violet gradients and zinc surfaces replaced with the site's tokens.
 */

export interface ChatMessage {
  id: string;
  sender: "user" | "desk";
  content: string;
  /** Who wrote it, shown on the desk's side so the customer sees a name, not a department. */
  authorName?: string;
  at?: string;
  /** Inside the bubble, above the text: a "forwarded" label, the line being answered. */
  top?: ReactNode;
  /** Under the bubble: time, ticks, reactions. */
  bottom?: ReactNode;
  /** A control beside the bubble — the message menu. */
  menu?: ReactNode;
  /** Greyed and italic, for "This message was deleted". */
  muted?: boolean;
}

/** The emoji offered in the composer and as reactions. A fixed set: no picker library for a
 *  grid of characters every system already draws. */
export const EMOJI = [
  "😀", "😂", "🤣", "😊", "😍", "🥰", "😘", "😎", "🤩", "🥳", "😇", "🙂", "😉", "😋", "😜", "🤔",
  "🤗", "🤭", "😴", "😮", "😢", "😭", "😡", "🥺", "😱", "🤯", "😬", "🙄", "😏", "😅", "🤝", "👏",
  "👍", "👎", "🙏", "💪", "👋", "✌️", "👌", "🤞", "❤️", "🧡", "💛", "💚", "💙", "💜", "🖤", "💔",
  "🔥", "✨", "🎉", "🎬", "🍿", "🎥", "⭐", "🌙", "☀️", "🌹", "☕", "🍕", "⚽", "🎵", "✅", "❌",
];

/** A grid of {@link EMOJI}; the caller decides what a pick does. */
export function EmojiGrid({ onPick, className }: { onPick: (emoji: string) => void; className?: string }) {
  return (
    <div role="listbox" aria-label={t("chat.emoji", "Emoji")} className={cn("grid grid-cols-8 gap-1 rounded-xl border border-line bg-surface-raised p-2 shadow-xl shadow-black/40", className)}>
      {EMOJI.map((emoji) => (
        <button key={emoji} type="button" role="option" aria-selected={false} onClick={() => onPick(emoji)}
          className="flex h-8 w-8 items-center justify-center rounded-lg text-lg transition hover:bg-surface">
          {emoji}
        </button>
      ))}
    </div>
  );
}

function TypingIndicator() {
  return (
    <motion.div
      initial={{ opacity: 0, scale: 0.9 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.9 }}
      transition={{ duration: 0.2, ease: "easeOut" }}
      className="inline-flex items-center gap-1 rounded-2xl rounded-tl-md border border-line bg-surface-raised px-4 py-3"
    >
      {[0, 1, 2].map((i) => (
        <motion.span
          key={i}
          className="h-2 w-2 rounded-full bg-ink-mute"
          animate={{ opacity: [0.4, 1, 0.4], y: [0, -4, 0] }}
          transition={{ duration: 0.8, repeat: Infinity, delay: i * 0.15, ease: "easeInOut" }}
        />
      ))}
    </motion.div>
  );
}

/** Minimal markdown: **bold** only. The answers use it for page names and nothing else, so a
 *  full parser would be a dependency bought for one asterisk pair. */
function renderContent(text: string) {
  return text.split(/(\*\*[^*]+\*\*)/g).map((part, i) =>
    part.startsWith("**") && part.endsWith("**")
      ? <strong key={i} className="text-accent">{part.slice(2, -2)}</strong>
      : <span key={i}>{part}</span>,
  );
}

function MessageBubble({ message }: { message: ChatMessage }) {
  const isUser = message.sender === "user";

  return (
    <motion.div
      initial={{ opacity: 0, y: 12, scale: 0.96, x: isUser ? 20 : -20 }}
      animate={{ opacity: 1, y: 0, scale: 1, x: 0 }}
      transition={{ duration: 0.35, ease: [0.22, 1, 0.36, 1] }}
      className={cn("flex w-full", isUser ? "justify-end" : "justify-start")}
    >
      <div className={cn("group flex items-end gap-2", isUser && "flex-row-reverse")}>
        {!isUser ? (
          <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-accent-dim">
            <ShieldCheck className="size-4 text-surface" aria-hidden />
          </div>
        ) : null}

        <div className={cn("max-w-[80%]", isUser ? "text-right" : "text-left")}>
          {!isUser && message.authorName ? (
            <p className="mb-1 text-[11px] text-ink-mute">{message.authorName}</p>
          ) : null}

          <div
            className={cn(
              "whitespace-pre-wrap rounded-2xl px-4 py-2.5 text-left text-sm leading-relaxed",
              isUser
                ? "rounded-tr-md bg-accent text-surface"
                : "rounded-tl-md border border-line bg-surface-raised text-ink",
              message.muted && "italic opacity-70",
            )}
          >
            {message.top}
            {renderContent(message.content)}
          </div>
          {message.bottom}
        </div>
        {message.menu ? <div className="self-center">{message.menu}</div> : null}
      </div>
    </motion.div>
  );
}

export function ChatMessages({
  messages,
  pending,
  onSend,
  title,
  subtitle,
  placeholder,
  className,
  otherTyping,
  onTyping,
  live,
  above,
  draft,
  closed,
  emoji,
}: {
  /** Above the input: the "replying to" or "editing" strip. */
  above?: ReactNode;
  /** Put this text in the input whenever the key changes (editing a message). */
  draft?: { key: string; text: string } | null;
  /** In place of the input: why nothing can be written here now. */
  closed?: ReactNode;
  /** An emoji button beside the input. */
  emoji?: boolean;
  /** Name of whoever is typing on the other end, shown under the last message. */
  otherTyping?: string | null;
  /** Called as the reader types; the caller decides how often to tell the other side. */
  onTyping?: () => void;
  /** True while the real-time connection is up, so the header can say replies arrive live. */
  live?: boolean;
  messages: ChatMessage[];
  pending: boolean;
  onSend: (text: string) => void;
  title: string;
  subtitle: string;
  placeholder: string;
  className?: string;
}) {
  const [value, setValue] = useState("");
  const [picking, setPicking] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!draft) return;
    setValue(draft.text);
    inputRef.current?.focus();
  }, [draft]);

  const scrollToBottom = useCallback(() => {
    const list = scrollRef.current;
    if (!list) return;
    if (typeof list.scrollTo === "function") list.scrollTo({ top: list.scrollHeight, behavior: "smooth" });
    else list.scrollTop = list.scrollHeight;
  }, []);

  useEffect(() => { scrollToBottom(); }, [messages.length, pending, otherTyping, scrollToBottom]);

  function send() {
    const text = value.trim();
    if (!text || pending) return;
    onSend(text);
    setValue("");
    setPicking(false);
  }

  function onKeyDown(event: KeyboardEvent) {
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      send();
    }
  }

  return (
    <div className={cn("flex flex-col overflow-hidden rounded-2xl border border-line bg-surface", className)}>
      <div className="flex items-center justify-between border-b border-line px-4 py-3">
        <div className="flex items-center gap-2">
          <div className="flex h-8 w-8 items-center justify-center rounded-full bg-accent">
            <ShieldCheck className="size-4 text-surface" aria-hidden />
          </div>
          <div>
            <h3 className="text-sm font-medium text-ink">{title}</h3>
            <p className="flex items-center gap-1.5 text-xs text-ink-mute">
              {live ? (
                <span className="inline-flex items-center gap-1 text-good" title={t("chat.liveHint", "Replies arrive instantly")}>
                  <span className="h-1.5 w-1.5 rounded-full bg-good" aria-hidden />
                  {t("chat.live", "Live")}
                  <span aria-hidden>·</span>
                </span>
              ) : null}
              {subtitle}
            </p>
          </div>
        </div>

      </div>

      <div
        ref={scrollRef}
        role="log"
        aria-label={title}
        aria-live="polite"
        className="flex-1 space-y-3 overflow-y-auto p-4"
      >
        {messages.map((message) => <MessageBubble key={message.id} message={message} />)}
        <AnimatePresence>{pending ? <TypingIndicator /> : null}</AnimatePresence>
        {otherTyping && !pending ? (
          <p className="text-xs italic text-ink-mute" aria-live="polite">
            {otherTyping} {t("chat.typing", "is typing…")}
          </p>
        ) : null}
      </div>

      <div className="relative border-t border-line p-3">
        {closed ? <div className="text-center text-sm text-ink-mute">{closed}</div> : (
        <>
        {above}
        {picking ? (
          <EmojiGrid className="absolute bottom-full left-3 mb-2" onPick={(e) => { setValue((v) => v + e); inputRef.current?.focus(); }} />
        ) : null}
        <div className="flex items-center gap-2 rounded-xl border border-line bg-surface-raised px-4 py-2 focus-within:border-accent">
          {emoji ? (
            <button type="button" onClick={() => setPicking((p) => !p)} aria-label={t("chat.emoji", "Emoji")} aria-expanded={picking}
              className={cn("text-ink-mute transition hover:text-ink", picking && "text-accent")}>
              <Smile className="size-5" aria-hidden />
            </button>
          ) : null}
          <input
            ref={inputRef}
            type="text"
            value={value}
            onChange={(e) => { setValue(e.target.value); if (e.target.value) onTyping?.(); }}
            onKeyDown={onKeyDown}
            onFocus={() => setPicking(false)}
            placeholder={placeholder}
            aria-label={placeholder}
            className="flex-1 bg-transparent text-sm text-ink outline-none placeholder:text-ink-mute/70"
          />
          <button
            onClick={send}
            disabled={pending || !value.trim()}
            aria-label={t("help.send")}
            className={cn(
              "flex h-8 w-8 items-center justify-center rounded-lg transition-colors",
              value.trim() && !pending ? "bg-accent text-surface hover:bg-accent-bright" : "bg-line text-ink-mute",
            )}
          >
            <Send className="size-4" aria-hidden />
          </button>
        </div>
        </>
        )}
      </div>
    </div>
  );
}

export default ChatMessages;
