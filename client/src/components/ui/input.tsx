import * as React from "react";
import { cn } from "@/lib/utils";

// React 19 passes ref as an ordinary prop, so these are plain components (no forwardRef).

export function Input({ className, ...props }: React.ComponentProps<"input">) {
  return (
    <input
      className={cn(
        "h-10 w-full rounded-md border border-line bg-surface-raised px-3 text-sm text-ink",
        "placeholder:text-ink-mute/70 focus:border-accent focus:outline-none",
        className,
      )}
      {...props}
    />
  );
}

export function Select({ className, children, ...props }: React.ComponentProps<"select">) {
  return (
    <select
      className={cn(
        "h-10 w-full rounded-md border border-line bg-surface-raised px-3 text-sm text-ink",
        "focus:border-accent focus:outline-none",
        className,
      )}
      {...props}
    >
      {children}
    </select>
  );
}

export function Textarea({ className, ...props }: React.ComponentProps<"textarea">) {
  return (
    <textarea
      className={cn(
        "min-h-24 w-full rounded-md border border-line bg-surface-raised p-3 text-sm text-ink",
        "placeholder:text-ink-mute/70 focus:border-accent focus:outline-none",
        className,
      )}
      {...props}
    />
  );
}

export function Field({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) {
  return (
    <label className="block space-y-1.5">
      <span className="text-sm text-ink-mute">{label}</span>
      {children}
      {hint ? <span className="block text-xs text-ink-mute/80">{hint}</span> : null}
    </label>
  );
}
