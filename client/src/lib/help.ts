import type { ChatMessage } from "@/components/ui/chat-messages";

export interface ChatLine {
  id: string;
  fromDesk: boolean;
  authorName: string;
  body: string;
  createdAtUtc: string;
}

export interface ChatThread {
  id: string;
  userEmail: string;
  userName: string;
  status: "Open" | "Answered" | "Closed";
  lastMessageAtUtc: string;
  unreadForDesk: number;
  messages: ChatLine[];
}

export interface InboxRow {
  id: string;
  userEmail: string;
  userName: string;
  status: "Open" | "Answered" | "Closed";
  lastMessageAtUtc: string;
  unread: number;
  preview: string;
}

/**
 * Turns a stored thread into bubbles. `deskIsUs` flips the sides for the Security desk, so
 * each party sees their own messages on the right — the same thread read from two chairs.
 */
export function toBubbles(thread: ChatThread, deskIsUs = false): ChatMessage[] {
  return thread.messages.map((line) => ({
    id: line.id,
    sender: line.fromDesk === deskIsUs ? "user" : "desk",
    content: line.body,
    authorName: line.authorName,
    at: line.createdAtUtc,
  }));
}
