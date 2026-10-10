import { useEffect, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import { AlertTriangle, HelpCircle } from "lucide-react";
import { Modal } from "@/components/ui/modal";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { t } from "@/lib/i18n";

/**
 * The site's own confirm and prompt. The browser's window.confirm / window.prompt draw a grey
 * system box headed "localhost:7139 says", in the browser's language rather than the page's;
 * these are the same questions in the site's colours and words, awaited the same way:
 *
 *   if (!(await askConfirm(t("..."), { danger: true }))) return;
 *   const reason = await askText(t("..."));   // null when cancelled
 */
export function askConfirm(message: string, options: { confirmLabel?: string; cancelLabel?: string; danger?: boolean } = {}): Promise<boolean> {
  return open<boolean>((done) => <ConfirmDialog message={message} {...options} onDone={done} />);
}

export function askText(message: string, defaultValue = ""): Promise<string | null> {
  return open<string | null>((done) => <TextDialog message={message} defaultValue={defaultValue} onDone={done} />);
}

/** Mounts one dialog in its own root and removes it once it has an answer. */
function open<T>(render: (done: (value: T) => void) => React.ReactNode): Promise<T> {
  return new Promise<T>((resolve) => {
    const host = document.createElement("div");
    document.body.appendChild(host);
    const root = createRoot(host);
    let settled = false;
    const done = (value: T) => {
      if (settled) return;
      settled = true;
      resolve(value);
      // After the click that answered it has finished, not inside it.
      setTimeout(() => { root.unmount(); host.remove(); }, 0);
    };
    root.render(<>{render(done)}</>);
  });
}

function Frame({ icon, message, onCancel, children }: { icon: React.ReactNode; message: string; onCancel: () => void; children: React.ReactNode }) {
  return (
    <Modal onClose={onCancel} label={message} width="max-w-md" layer={120}>
      <div className="rounded-2xl border border-line bg-surface-raised p-6 shadow-2xl shadow-black/60">
        <div className="flex items-start gap-3">
          <span className="mt-0.5 flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-accent/15 text-accent">{icon}</span>
          <p className="pt-2 text-sm leading-relaxed text-ink">{message}</p>
        </div>
        {children}
      </div>
    </Modal>
  );
}

function ConfirmDialog({ message, confirmLabel, cancelLabel, danger, onDone }: { message: string; confirmLabel?: string; cancelLabel?: string; danger?: boolean; onDone: (ok: boolean) => void }) {
  const yes = useRef<HTMLButtonElement>(null);
  useEffect(() => { yes.current?.focus(); }, []);
  return (
    <Frame icon={danger ? <AlertTriangle size={18} aria-hidden /> : <HelpCircle size={18} aria-hidden />} message={message} onCancel={() => onDone(false)}>
      <div className="mt-6 flex justify-end gap-2">
        <Button variant="outline" onClick={() => onDone(false)}>{cancelLabel ?? t("common.cancel", "Cancel")}</Button>
        <Button ref={yes} variant={danger ? "danger" : "solid"} onClick={() => onDone(true)}>{confirmLabel ?? t("common.ok", "OK")}</Button>
      </div>
    </Frame>
  );
}

function TextDialog({ message, defaultValue, onDone }: { message: string; defaultValue: string; onDone: (value: string | null) => void }) {
  const [value, setValue] = useState(defaultValue);
  return (
    <Frame icon={<HelpCircle size={18} aria-hidden />} message={message} onCancel={() => onDone(null)}>
      <form className="mt-5 space-y-5" onSubmit={(e) => { e.preventDefault(); onDone(value); }}>
        <Input autoFocus value={value} onChange={(e) => setValue(e.target.value)} />
        <div className="flex justify-end gap-2">
          <Button variant="outline" onClick={() => onDone(null)}>{t("common.cancel", "Cancel")}</Button>
          <Button type="submit">{t("common.ok", "OK")}</Button>
        </div>
      </form>
    </Frame>
  );
}
