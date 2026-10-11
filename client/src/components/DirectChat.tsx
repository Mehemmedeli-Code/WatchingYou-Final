import { useCallback, useEffect, useRef, useState } from "react";
import { Modal } from "@/components/ui/modal";
import {
  Ban, Check, CheckCheck, ChevronDown, Copy, CornerUpLeft, Flag, Forward, Info, Pencil, Pin, PinOff,
  SmilePlus, Star, StarOff, Trash2, X,
} from "lucide-react";
import { Panel, Notice } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { ChatMessages, EmojiGrid, type ChatMessage } from "@/components/ui/chat-messages";
import { del, get, post, put, ApiError } from "@/lib/api";
import { formatWhen, t } from "@/lib/i18n";
import { openDirectChats, useRealtime, useRealtimeLive, useTypingIndicator, useTypingSender } from "@/lib/realtime";
import { askConfirm, askText } from "@/lib/dialog";
import { cn } from "@/lib/utils";

interface DirectLine {
  id: string;
  mine: boolean;
  senderName: string;
  body: string;
  createdAtUtc: string;
  /** Mine only: when the other person read it. */
  seenAtUtc?: string | null;
  editedAtUtc?: string | null;
  deleted?: boolean;
  forwarded?: boolean;
  replyTo?: { id: string; senderName: string; snippet: string; deleted: boolean } | null;
  reactions?: { emoji: string; count: number; mine: boolean }[];
  starred?: boolean;
}

export interface DirectConversation {
  otherUserId: string;
  otherName: string;
  otherAvatarUrl?: string | null;
  otherCity?: string | null;
  blockedByMe: boolean;
  /** Blocked either way, or the other person left the globe. The reason is not distinguished. */
  unreachable: boolean;
  messages: DirectLine[];
  /** More messages exist above the newest page. */
  hasOlder?: boolean;
  /** A private account is written to by request: "sent" is mine waiting, "received" is theirs
   *  to me, "declined" is theirs that I turned down. */
  request?: "none" | "sent" | "received" | "declined";
  pinned?: DirectLine | null;
}

interface ThreadRow { otherUserId: string; otherName: string; otherAvatarUrl?: string | null }

/** The reactions offered first, as in WhatsApp; the rest are one tap further. */
const QUICK = ["👍", "❤️", "😂", "😮", "😢", "🙏"];
const EDIT_WINDOW_MS = 15 * 60_000;

/**
 * A conversation with another member.
 *
 * Block and report sit in the header rather than behind a menu: the moment somebody needs
 * them is not the moment to make them go looking. Reporting also blocks, because nobody
 * should keep receiving from a person they have just reported while the desk catches up.
 *
 * Each message has its own menu — info, reply, copy, react, forward, pin, star, edit, delete —
 * and every one of those is checked again by the server.
 */
export function DirectChat({
  userId,
  onClose,
  onChanged,
}: {
  userId: string;
  onClose: () => void;
  onChanged?: () => void;
}) {
  const [thread, setThread] = useState<DirectConversation | null>(null);
  // Pages above the newest one, loaded on request. The poll only ever brings the newest page.
  const [older, setOlder] = useState<DirectLine[]>([]);
  const [olderMore, setOlderMore] = useState<boolean | null>(null);
  const [loadingOlder, setLoadingOlder] = useState(false);

  const [replyTo, setReplyTo] = useState<DirectLine | null>(null);
  const [editing, setEditing] = useState<DirectLine | null>(null);
  const [menuFor, setMenuFor] = useState<DirectLine | null>(null);
  const [infoFor, setInfoFor] = useState<DirectLine | null>(null);
  const [forwarding, setForwarding] = useState<DirectLine | null>(null);

  async function loadOlder() {
    const first = older[0] ?? thread?.messages[0];
    if (!first) return;
    setLoadingOlder(true);
    const page = await get<DirectConversation>(`/api/messages/${userId}?before=${encodeURIComponent(first.createdAtUtc)}`).catch(() => null);
    setLoadingOlder(false);
    if (!page) return;
    setOlder((list) => [...page.messages, ...list]);
    setOlderMore(page.hasOlder ?? false);
  }
  const [pending, setPending] = useState(false);
  const [message, setMessage] = useState<{ tone: "ok" | "error"; text: string } | null>(null);
  const sending = useRef(false);

  const load = useCallback(async () => {
    if (sending.current) return;
    const latest = await get<DirectConversation>(`/api/messages/${userId}`).catch(() => undefined);
    if (latest !== undefined) setThread(latest);
  }, [userId]);

  useEffect(() => {
    openDirectChats.add(userId);
    return () => { openDirectChats.delete(userId); };
  }, [userId]);

  const live = useRealtimeLive();
  const [typing, showTyping] = useTypingIndicator();
  const sendTyping = useTypingSender("Typing", userId);

  // A new message from this person reloads the thread at once; the poll stays as a safety
  // net, just much slower while the socket is up. An edit, a reaction or a pin reloads it too.
  useRealtime<{ fromUserId: string }>("dm", (e) => { if (e.fromUserId === userId) void load(); });
  useRealtime<{ fromUserId: string }>("dmChanged", (e) => { if (e.fromUserId === userId) void load(); });
  useRealtime<{ fromUserId: string; fromName: string }>("typing", (e) => {
    if (e.fromUserId === userId) showTyping(e.fromName || thread?.otherName || "");
  });

  useEffect(() => {
    void load();
    const timer = setInterval(() => void load(), live ? 30000 : 5000);
    return () => clearInterval(timer);
  }, [load, live]);

  function failed(err: unknown) {
    setMessage({
      tone: "error",
      text: err instanceof ApiError && err.status === 409 ? (err.message || t("dm.blocked")) : t("error.action"),
    });
  }

  async function send(text: string) {
    sending.current = true;
    setPending(true);
    setMessage(null);
    try {
      if (editing) {
        await put(`/api/messages/item/${editing.id}`, { body: text });
        setEditing(null);
        sending.current = false;
        await load();
      } else {
        setThread(await post<DirectConversation>(`/api/messages/${userId}`, { body: text, replyToId: replyTo?.id ?? null }));
        setReplyTo(null);
      }
      onChanged?.();
    } catch (err) {
      failed(err);
    } finally {
      sending.current = false;
      setPending(false);
    }
  }

  /** One message action, then the thread as the server now has it. */
  async function act(run: () => Promise<unknown>) {
    setMenuFor(null);
    setMessage(null);
    try { await run(); } catch (err) { failed(err); }
    await load();
  }

  async function answerRequest(accept: boolean) {
    try {
      setThread(await put<DirectConversation>(`/api/messages/${userId}/request`, { accept }));
      onChanged?.();
    } catch (err) { failed(err); }
  }

  async function toggleBlock() {
    if (!thread) return;
    await put(`/api/messages/${userId}/block`, { blocked: !thread.blockedByMe }).catch(() => null);
    await load();
    onChanged?.();
  }

  async function report() {
    if (!thread) return;
    const last = [...thread.messages].reverse().find((m) => !m.mine);
    const reason = await askText(t("dm.reportReason"));
    if (reason === null) return;

    await post(`/api/messages/${userId}/report`, { quote: last?.body ?? "(no message)", reason })
      .catch(() => null);
    setMessage({ tone: "ok", text: t("dm.reported") });
    await load();
    onChanged?.();
  }

  const request = thread?.request ?? "none";
  // Reacting, replying and pinning need a conversation both people are in.
  const open = request === "none" && !thread?.unreachable && !thread?.blockedByMe;
  const newest = thread?.messages ?? [];
  const shown = [...older.filter((o) => !newest.some((n) => n.id === o.id)), ...newest];
  const canLoadOlder = olderMore ?? thread?.hasOlder ?? false;

  const bubbles: ChatMessage[] = shown.map((line) => ({
    id: line.id,
    sender: line.mine ? "user" : "desk",
    content: line.deleted ? t("dm.deleted", "This message was deleted") : line.body,
    authorName: line.senderName,
    at: line.createdAtUtc,
    muted: line.deleted,
    top: line.deleted ? null : (
      <>
        {line.forwarded ? (
          <span className="mb-1 flex items-center gap-1 text-[11px] italic opacity-75">
            <Forward size={11} aria-hidden /> {t("dm.forwarded", "Forwarded")}
          </span>
        ) : null}
        {line.replyTo ? (
          <span className={cn("mb-1.5 block rounded-lg border-l-2 px-2 py-1 text-xs",
            line.mine ? "border-surface/60 bg-black/15" : "border-accent bg-surface")}>
            <span className="block font-semibold">{line.replyTo.senderName}</span>
            <span className="line-clamp-2 opacity-80">
              {line.replyTo.deleted ? t("dm.deleted", "This message was deleted") : line.replyTo.snippet}
            </span>
          </span>
        ) : null}
      </>
    ),
    bottom: (
      <span className={cn("mt-1 flex flex-wrap items-center gap-1.5 text-[11px] text-ink-mute", line.mine ? "justify-end" : "justify-start")}>
        {line.starred ? <Star size={11} className="fill-current text-warn" aria-label={t("dm.starred", "Starred")} /> : null}
        {line.editedAtUtc && !line.deleted ? <span>{t("dm.edited", "edited")}</span> : null}
        <time dateTime={line.createdAtUtc}>{formatWhen(line.createdAtUtc)}</time>
        {line.mine && !line.deleted ? (
          line.seenAtUtc
            ? <CheckCheck size={14} className="text-accent" aria-label={t("dm.seen", "Read")} />
            : <Check size={14} aria-label={t("dm.sent", "Sent")} />
        ) : null}
        {line.reactions?.map((r) => (
          <button key={r.emoji} type="button" disabled={!open}
            onClick={() => void act(() => put(`/api/messages/item/${line.id}/reaction`, { emoji: r.mine ? null : r.emoji }))}
            className={cn("rounded-full border px-1.5 py-0.5 text-xs", r.mine ? "border-accent bg-accent/15" : "border-line bg-surface-raised")}>
            {r.emoji}{r.count > 1 ? ` ${r.count}` : ""}
          </button>
        ))}
      </span>
    ),
    menu: line.deleted && !line.mine ? null : (
      <button type="button" onClick={() => setMenuFor(line)} aria-label={t("dm.actions", "Message actions")}
        className="rounded-full p-1 text-ink-mute opacity-60 transition hover:bg-surface hover:text-ink group-hover:opacity-100 focus-visible:opacity-100">
        <ChevronDown size={16} aria-hidden />
      </button>
    ),
  }));

  // Why the input is not there, when it is not.
  const closed =
    thread?.blockedByMe || thread?.unreachable ? t("dm.closed", "You cannot write in this conversation.")
    : request === "sent" ? t("dm.requestSent", "Message request sent. You can write again once they accept it.")
    : request === "received" || request === "declined" ? t("dm.requestAnswerFirst", "Accept the request to reply.")
    : null;

  const above = replyTo || editing ? (
    <div className="mb-2 flex items-start justify-between gap-2 rounded-lg border-l-2 border-accent bg-surface-raised px-3 py-2 text-xs">
      <span className="min-w-0">
        <span className="block font-semibold text-accent">
          {editing ? t("dm.editing", "Editing message") : `${t("dm.replyingTo", "Replying to")} ${replyTo!.senderName}`}
        </span>
        <span className="line-clamp-1 text-ink-mute">{(editing ?? replyTo)!.body}</span>
      </span>
      <button type="button" onClick={() => { setReplyTo(null); setEditing(null); }} aria-label={t("common.cancel")} className="text-ink-mute hover:text-ink">
        <X size={14} aria-hidden />
      </button>
    </div>
  ) : null;

  return (
    <Modal onClose={onClose} label={thread?.otherName} width="max-w-xl" layer={94}>
        <Panel>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="flex items-center gap-3">
              {thread?.otherAvatarUrl ? (
                <img src={thread.otherAvatarUrl} alt="" className="h-10 w-10 rounded-full object-cover" />
              ) : (
                <span className="flex h-10 w-10 items-center justify-center rounded-full bg-surface text-ink-mute">
                  {(thread?.otherName ?? "?").slice(0, 1).toUpperCase()}
                </span>
              )}
              <div>
                <p className="font-display text-lg text-ink">{thread?.otherName ?? "…"}</p>
                {thread?.otherCity ? <p className="text-xs text-ink-mute">{thread.otherCity}</p> : null}
              </div>
            </div>

            <div className="flex items-center gap-2">
              <Button size="sm" variant="outline" onClick={toggleBlock}>
                <Ban size={14} aria-hidden />
                {thread?.blockedByMe ? t("dm.unblock") : t("dm.block")}
              </Button>
              <Button size="sm" variant="outline" onClick={report}>
                <Flag size={14} aria-hidden />
                {t("dm.report")}
              </Button>
              <button onClick={onClose} aria-label={t("common.cancel")} className="text-ink-mute hover:text-ink">
                <X size={18} aria-hidden />
              </button>
            </div>
          </div>

          {message ? <div className="mt-3"><Notice tone={message.tone}>{message.text}</Notice></div> : null}

          {thread?.blockedByMe ? (
            <div className="mt-3"><Notice tone="info">{t("dm.blockedByMe")}</Notice></div>
          ) : thread?.unreachable ? (
            <div className="mt-3"><Notice tone="info">{t("dm.blocked")}</Notice></div>
          ) : null}

          {request === "received" || request === "declined" ? (
            <div className="mt-3 rounded-xl border border-accent/50 bg-accent/5 p-3">
              <p className="text-sm text-ink">
                {request === "received"
                  ? t("dm.requestReceived", "{name} wants to message you. They can write again only if you accept.").replace("{name}", thread?.otherName ?? "")
                  : t("dm.requestDeclined", "You declined this message request.")}
              </p>
              <div className="mt-2 flex gap-2">
                <Button size="sm" onClick={() => void answerRequest(true)}>{t("dm.accept", "Accept")}</Button>
                {request === "received" ? (
                  <Button size="sm" variant="outline" onClick={() => void answerRequest(false)}>{t("dm.decline", "Decline")}</Button>
                ) : null}
              </div>
            </div>
          ) : null}

          {thread?.pinned ? (
            <div className="mt-3 flex items-center gap-2 rounded-xl border border-line bg-surface px-3 py-2 text-sm">
              <Pin size={14} className="shrink-0 text-accent" aria-label={t("dm.pinned", "Pinned")} />
              <span className="min-w-0 flex-1 truncate text-ink">{thread.pinned.body}</span>
              <button type="button" onClick={() => void act(() => put(`/api/messages/item/${thread.pinned!.id}/pin`, { pinned: false }))}
                aria-label={t("dm.unpin", "Unpin")} className="text-ink-mute hover:text-ink">
                <PinOff size={14} aria-hidden />
              </button>
            </div>
          ) : null}

          {canLoadOlder ? (
            <div className="mt-3 text-center">
              <Button size="sm" variant="outline" disabled={loadingOlder} onClick={loadOlder}>
                {t("dm.older", "Earlier messages")}
              </Button>
            </div>
          ) : null}

          <div className="mt-4">
            <ChatMessages
              className="h-[420px] w-full"
              messages={bubbles}
              pending={pending}
              onSend={send}
              title={thread?.otherName ?? ""}
              subtitle={t("dm.safety")}
              placeholder={t("dm.placeholder")}
              live={live}
              otherTyping={typing}
              onTyping={closed ? undefined : sendTyping}
              above={above}
              draft={editing ? { key: editing.id, text: editing.body } : null}
              closed={closed}
              emoji
            />
          </div>
        </Panel>

        {menuFor ? (
          <MessageMenu
            line={menuFor}
            open={open}
            pinned={thread?.pinned?.id === menuFor.id}
            onClose={() => setMenuFor(null)}
            onInfo={() => { setInfoFor(menuFor); setMenuFor(null); }}
            onReply={() => { setEditing(null); setReplyTo(menuFor); setMenuFor(null); }}
            onCopy={() => { void navigator.clipboard?.writeText(menuFor.body); setMenuFor(null); setMessage({ tone: "ok", text: t("dm.copied", "Copied") }); }}
            onReact={(emoji) => void act(() => put(`/api/messages/item/${menuFor.id}/reaction`, {
              emoji: menuFor.reactions?.some((r) => r.mine && r.emoji === emoji) ? null : emoji,
            }))}
            onForward={() => { setForwarding(menuFor); setMenuFor(null); }}
            onPin={() => void act(() => put(`/api/messages/item/${menuFor.id}/pin`, { pinned: thread?.pinned?.id !== menuFor.id }))}
            onStar={() => void act(() => put(`/api/messages/item/${menuFor.id}/star`, { starred: !menuFor.starred }))}
            onEdit={() => { setReplyTo(null); setEditing(menuFor); setMenuFor(null); }}
            onDelete={async (everyone) => {
              const line = menuFor;
              setMenuFor(null);
              const ok = await askConfirm(everyone
                ? t("dm.deleteEveryoneConfirm", "Delete this message for everyone?")
                : t("dm.deleteMeConfirm", "Delete this message for you? The other person still sees it."),
                { confirmLabel: t("dm.delete", "Delete"), danger: true });
              if (ok) await act(() => del(`/api/messages/item/${line.id}?scope=${everyone ? "Everyone" : "Me"}`));
            }}
          />
        ) : null}

        {infoFor ? (
          <Modal onClose={() => setInfoFor(null)} label={t("dm.info", "Message info")} width="max-w-sm" layer={97}>
            <Panel>
              <p className="font-display text-lg text-ink">{t("dm.info", "Message info")}</p>
              <p className="mt-2 line-clamp-3 text-sm text-ink-mute">{infoFor.deleted ? t("dm.deleted", "This message was deleted") : infoFor.body}</p>
              <dl className="mt-4 space-y-2 text-sm">
                <div className="flex justify-between gap-3"><dt className="text-ink-mute">{t("dm.sentAt", "Sent")}</dt><dd className="text-ink">{formatWhen(infoFor.createdAtUtc)}</dd></div>
                {infoFor.mine ? (
                  <div className="flex justify-between gap-3"><dt className="text-ink-mute">{t("dm.seenAt", "Read")}</dt><dd className="text-ink">{infoFor.seenAtUtc ? formatWhen(infoFor.seenAtUtc) : "—"}</dd></div>
                ) : null}
                {infoFor.editedAtUtc ? (
                  <div className="flex justify-between gap-3"><dt className="text-ink-mute">{t("dm.editedAt", "Edited")}</dt><dd className="text-ink">{formatWhen(infoFor.editedAtUtc)}</dd></div>
                ) : null}
              </dl>
              <Button className="mt-5 w-full" variant="outline" onClick={() => setInfoFor(null)}>{t("common.close", "Close")}</Button>
            </Panel>
          </Modal>
        ) : null}

        {forwarding ? (
          <ForwardPicker
            line={forwarding}
            exclude={userId}
            onClose={() => setForwarding(null)}
            onDone={(name) => { setForwarding(null); setMessage({ tone: "ok", text: `${t("dm.forwardedTo", "Forwarded to")} ${name}` }); onChanged?.(); }}
          />
        ) : null}
    </Modal>
  );
}

/** The menu of one message, as a small sheet: it is never cut off at the edge of the chat. */
function MessageMenu({
  line, open, pinned, onClose, onInfo, onReply, onCopy, onReact, onForward, onPin, onStar, onEdit, onDelete,
}: {
  line: DirectLine;
  /** False while the conversation is a request: nothing that needs both people yet. */
  open: boolean;
  pinned: boolean;
  onClose: () => void;
  onInfo: () => void;
  onReply: () => void;
  onCopy: () => void;
  onReact: (emoji: string) => void;
  onForward: () => void;
  onPin: () => void;
  onStar: () => void;
  onEdit: () => void;
  onDelete: (everyone: boolean) => void;
}) {
  const [more, setMore] = useState(false);
  const live = !line.deleted;
  const editable = line.mine && live && Date.now() - new Date(line.createdAtUtc).getTime() < EDIT_WINDOW_MS;
  const item = "flex w-full items-center gap-3 rounded-lg px-3 py-2 text-left text-sm text-ink transition hover:bg-surface";

  return (
    <Modal onClose={onClose} label={t("dm.actions", "Message actions")} width="max-w-xs" layer={96}>
      <div className="rounded-2xl border border-line bg-surface-raised p-2 shadow-2xl shadow-black/60">
        {live && open ? (
          <div className="mb-1 flex items-center justify-between gap-1 border-b border-line px-1 pb-2">
            {QUICK.map((emoji) => (
              <button key={emoji} type="button" onClick={() => onReact(emoji)} aria-label={emoji}
                className={cn("flex h-9 w-9 items-center justify-center rounded-full text-xl transition hover:bg-surface",
                  line.reactions?.some((r) => r.mine && r.emoji === emoji) && "bg-accent/20")}>
                {emoji}
              </button>
            ))}
            <button type="button" onClick={() => setMore((m) => !m)} aria-label={t("dm.moreReactions", "More reactions")} aria-expanded={more}
              className="flex h-9 w-9 items-center justify-center rounded-full text-ink-mute transition hover:bg-surface hover:text-ink">
              <SmilePlus size={18} aria-hidden />
            </button>
          </div>
        ) : null}
        {more ? <EmojiGrid className="mb-2 border-0 shadow-none" onPick={onReact} /> : null}

        <button type="button" className={item} onClick={onInfo}><Info size={16} aria-hidden /> {t("dm.info", "Message info")}</button>
        {live && open ? <button type="button" className={item} onClick={onReply}><CornerUpLeft size={16} aria-hidden /> {t("dm.reply", "Reply")}</button> : null}
        {live ? <button type="button" className={item} onClick={onCopy}><Copy size={16} aria-hidden /> {t("dm.copy", "Copy")}</button> : null}
        {live ? <button type="button" className={item} onClick={onForward}><Forward size={16} aria-hidden /> {t("dm.forward", "Forward")}</button> : null}
        {live && open ? (
          <button type="button" className={item} onClick={onPin}>
            {pinned ? <PinOff size={16} aria-hidden /> : <Pin size={16} aria-hidden />} {pinned ? t("dm.unpin", "Unpin") : t("dm.pin", "Pin")}
          </button>
        ) : null}
        {live ? (
          <button type="button" className={item} onClick={onStar}>
            {line.starred ? <StarOff size={16} aria-hidden /> : <Star size={16} aria-hidden />} {line.starred ? t("dm.unstar", "Unstar") : t("dm.star", "Star")}
          </button>
        ) : null}
        {editable ? <button type="button" className={item} onClick={onEdit}><Pencil size={16} aria-hidden /> {t("dm.edit", "Edit")}</button> : null}
        <div className="my-1 border-t border-line" />
        <button type="button" className={cn(item, "text-bad")} onClick={() => onDelete(false)}>
          <Trash2 size={16} aria-hidden /> {t("dm.deleteForMe", "Delete for me")}
        </button>
        {line.mine && live ? (
          <button type="button" className={cn(item, "text-bad")} onClick={() => onDelete(true)}>
            <Trash2 size={16} aria-hidden /> {t("dm.deleteForEveryone", "Delete for everyone")}
          </button>
        ) : null}
      </div>
    </Modal>
  );
}

/** Forward: pick one of my conversations; the message goes there marked "Forwarded". */
function ForwardPicker({ line, exclude, onClose, onDone }: {
  line: DirectLine; exclude: string; onClose: () => void; onDone: (name: string) => void;
}) {
  const [rows, setRows] = useState<ThreadRow[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    void get<ThreadRow[]>("/api/messages").then((list) => setRows(list.filter((r) => r.otherUserId !== exclude))).catch(() => setRows([]));
  }, [exclude]);

  async function forward(row: ThreadRow) {
    setBusy(true);
    setError(null);
    try {
      await post(`/api/messages/${row.otherUserId}`, { body: line.body, forwarded: true });
      onDone(row.otherName);
    } catch (err) {
      setError(err instanceof ApiError && err.message ? err.message : t("error.action"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal onClose={onClose} label={t("dm.forward", "Forward")} width="max-w-sm" layer={97}>
      <Panel>
        <p className="font-display text-lg text-ink">{t("dm.forwardTo", "Forward to…")}</p>
        <p className="mt-1 line-clamp-2 text-sm text-ink-mute">{line.body}</p>
        {error ? <div className="mt-3"><Notice tone="error">{error}</Notice></div> : null}
        <ul className="mt-3 max-h-72 divide-y divide-line overflow-y-auto rounded-xl border border-line">
          {rows === null ? <li className="px-3 py-3 text-sm text-ink-mute">{t("common.loading")}</li> : null}
          {rows?.length === 0 ? <li className="px-3 py-3 text-sm text-ink-mute">{t("dm.noOtherChats", "No other conversations yet.")}</li> : null}
          {rows?.map((row) => (
            <li key={row.otherUserId}>
              <button type="button" disabled={busy} onClick={() => void forward(row)}
                className="flex w-full items-center gap-3 px-3 py-2.5 text-left text-sm text-ink transition hover:bg-surface disabled:opacity-50">
                {row.otherAvatarUrl
                  ? <img src={row.otherAvatarUrl} alt="" className="h-8 w-8 rounded-full object-cover" />
                  : <span className="flex h-8 w-8 items-center justify-center rounded-full bg-surface text-ink-mute">{row.otherName.slice(0, 1).toUpperCase()}</span>}
                {row.otherName}
              </button>
            </li>
          ))}
        </ul>
        <Button className="mt-4 w-full" variant="outline" onClick={onClose}>{t("common.cancel")}</Button>
      </Panel>
    </Modal>
  );
}
