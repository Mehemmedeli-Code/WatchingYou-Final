import * as React from "react";
import { cn } from "@/lib/utils";

export const Input = React.forwardRef<HTMLInputElement, React.InputHTMLAttributes<HTMLInputElement>>(
  ({ className, ...props }, ref) => (
    <input
      ref={ref}
      className={cn(
        "h-10 w-full rounded-md border border-line bg-surface-raised px-3 text-sm text-ink",
        "placeholder:text-ink-mute/70 focus:border-accent focus:outline-none",
        className,
      )}
      {...props}
    />
  ),
);
Input.displayName = "Input";

export const Select = React.forwardRef<HTMLSelectElement, React.SelectHTMLAttributes<HTMLSelectElement>>(
  ({ className, children, ...props }, ref) => (
    <select
      ref={ref}
      className={cn(
        "h-10 w-full rounded-md border border-line bg-surface-raised px-3 text-sm text-ink",
        "focus:border-accent focus:outline-none",
        className,
      )}
      {...props}
    >
      {children}
    </select>
  ),
);
Select.displayName = "Select";

export const Textarea = React.forwardRef<HTMLTextAreaElement, React.TextareaHTMLAttributes<HTMLTextAreaElement>>(
  ({ className, ...props }, ref) => (
    <textarea
      ref={ref}
      className={cn(
        "min-h-24 w-full rounded-md border border-line bg-surface-raised p-3 text-sm text-ink",
        "placeholder:text-ink-mute/70 focus:border-accent focus:outline-none",
        className,
      )}
      {...props}
    />
  ),
);
Textarea.displayName = "Textarea";

export function Field({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) {
  return (
    <label className="block space-y-1.5">
      <span className="text-sm text-ink-mute">{label}</span>
      {children}
      {hint ? <span className="block text-xs text-ink-mute/80">{hint}</span> : null}
    </label>
  );
}
