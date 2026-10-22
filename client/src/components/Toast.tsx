import { useEffect } from "react";
import { createPortal } from "react-dom";
import { motion, AnimatePresence } from "motion/react";
import { X } from "lucide-react";
import { t } from "@/lib/i18n";

export interface ToastMessage {
  id: number;
  tone: "ok" | "error";
  text: string;
}

/**
 * Feedback has to appear where the eye already is. The catalogue page has two places to
 * rent from — the carousel at the top and the grid far below it — and a notice pinned to
 * one of them is invisible from the other. That is why renting twice felt like nothing
 * had happened: the confirmation was real, just off-screen.
 *
 * Fixed to the viewport, so it lands in view wherever the click came from.
 */
export function Toaster({ toast, onDismiss }: { toast: ToastMessage | null; onDismiss: () => void }) {
  useEffect(() => {
    if (!toast) return;
    const timer = setTimeout(onDismiss, 4500);
    return () => clearTimeout(timer);
  }, [toast, onDismiss]);

  return createPortal(
    <div className="pointer-events-none fixed inset-x-0 bottom-6 z-[80] flex justify-center px-4">
      <AnimatePresence>
        {toast ? (
          <motion.div
            key={toast.id}
            initial={{ opacity: 0, y: 18 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: 10 }}
            transition={{ type: "spring", stiffness: 320, damping: 28 }}
            role="status"
            aria-live="polite"
            className={[
              "pointer-events-auto flex max-w-md items-start gap-3 rounded-xl border px-4 py-3 text-sm shadow-lg",
              toast.tone === "error"
                ? "border-bad-bg bg-bad-bg/90 text-bad"
                : "border-accent-dim bg-surface-raised/95 text-ink",
            ].join(" ")}
          >
            <span className="flex-1">{toast.text}</span>
            <button onClick={onDismiss} aria-label={t("common.close", "Close")} className="mt-0.5 opacity-70 hover:opacity-100">
              <X size={14} aria-hidden />
            </button>
          </motion.div>
        ) : null}
      </AnimatePresence>
    </div>,
    document.body,
  );
}
