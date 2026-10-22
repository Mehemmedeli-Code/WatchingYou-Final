import { useCallback, useEffect, useState } from "react";
import { MessageCircle } from "lucide-react";
import { Modal } from "@/components/ui/modal";
import { MessageBox } from "@/components/MessageBox";
import { get } from "@/lib/api";
import { t } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";

/**
 * The message button at the top right, beside the profile picture, as on Instagram: a count
 * of unread messages on it, and the message box when it is pressed. Mounted into the Razor
 * header's #rr-inbox slot by main.tsx, signed in only.
 */
export function HeaderInbox() {
  const [open, setOpen] = useState(false);
  const [unread, setUnread] = useState(0);

  const count = useCallback(() => {
    void get<{ unread: number }[]>("/api/messages")
      .then((rows) => setUnread(rows.reduce((sum, row) => sum + row.unread, 0)))
      .catch(() => null);
  }, []);

  useEffect(count, [count]);
  useRealtime("dm", count);
  const close = useCallback(() => { setOpen(false); count(); }, [count]);

  const label = t("gm.title", "Messages");
  return (
    <>
      <button
        type="button"
        className="rr-inbox"
        onClick={() => setOpen(true)}
        data-count={unread > 0 ? (unread > 9 ? "9+" : String(unread)) : undefined}
        aria-label={unread > 0 ? `${label} (${unread})` : label}
        title={label}
        aria-haspopup="dialog"
      >
        <MessageCircle size={19} aria-hidden />
      </button>
      {open ? (
        <Modal onClose={close} label={label} width="max-w-xl" layer={92}>
          <MessageBox onClose={close} onChanged={count} />
        </Modal>
      ) : null}
    </>
  );
}
