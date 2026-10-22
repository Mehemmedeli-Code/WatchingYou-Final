import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

export function Section({
  title,
  lede,
  actions,
  children,
  className,
}: {
  title: string;
  lede?: string;
  actions?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section className={cn("mx-auto w-full max-w-6xl px-4 py-12 sm:px-6", className)}>
      <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
        <div className="max-w-[60ch]">
          <h2 className="font-display text-3xl font-normal tracking-tight text-ink">{title}</h2>
          {lede ? <p className="mt-2 text-sm text-ink-mute">{lede}</p> : null}
        </div>
        {actions}
      </div>
      {children}
    </section>
  );
}

export function Panel({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div className={cn("rounded-xl border border-line bg-surface-raised p-5", className)}>{children}</div>
  );
}

export function Empty({ title, hint, action }: { title: string; hint: string; action?: ReactNode }) {
  return (
    <div className="rounded-xl border border-dashed border-line px-6 py-14 text-center">
      <p className="font-display text-xl text-ink">{title}</p>
      <p className="mx-auto mt-2 max-w-[46ch] text-sm text-ink-mute">{hint}</p>
      {action ? <div className="mt-5 flex justify-center">{action}</div> : null}
    </div>
  );
}

export function Notice({ tone = "info", children }: { tone?: "info" | "error" | "ok"; children: ReactNode }) {
  const tones = {
    info: "border-line text-ink-mute",
    error: "border-bad text-bad",
    ok: "border-good text-good",
  } as const;

  return (
    <p role={tone === "error" ? "alert" : "status"} className={cn("rounded-md border px-3 py-2 text-sm", tones[tone])}>
      {children}
    </p>
  );
}

export function Spinner({ label = "Loading" }: { label?: string }) {
  return (
    <div className="flex items-center gap-3 py-10 text-sm text-ink-mute">
      <span className="h-4 w-4 animate-spin rounded-full border-2 border-line border-t-accent" aria-hidden />
      {label}
    </div>
  );
}
