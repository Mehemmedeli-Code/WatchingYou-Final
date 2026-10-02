import { useEffect, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { cn } from "@/lib/utils";

/**
 * The one dialog frame on the site.
 *
 * Before this existed, five dialogs each carried their own copy of the same overlay, the same
 * click-outside rule and the same Escape handler — and they had already started to drift:
 * two locked the page scroll and three did not. One component means one behaviour.
 */
export function Modal({
  onClose,
  label,
  width = "max-w-lg",
  layer = 90,
  children,
}: {
  onClose: () => void;
  /** What a screen reader announces when the dialog opens. */
  label?: string;
  width?: string;
  /** Stacking order, for the rare dialog opened from inside another. */
  layer?: number;
  children: ReactNode;
}) {
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === "Escape") onClose(); };
    document.addEventListener("keydown", onKey);

    // The page behind should not scroll while a dialog is on top of it.
    const previous = document.body.style.overflow;
    document.body.style.overflow = "hidden";

    return () => {
      document.removeEventListener("keydown", onKey);
      document.body.style.overflow = previous;
    };
  }, [onClose]);

  return createPortal(
    <div
      className="fixed inset-0 flex items-start justify-center overflow-y-auto bg-black/80 p-4 backdrop-blur-sm sm:p-8"
      style={{ zIndex: layer }}
      role="dialog"
      aria-modal="true"
      aria-label={label}
      onClick={onClose}
    >
      {/* Clicks inside the dialog stop here, so only the backdrop closes it. */}
      <div className={cn("my-auto w-full", width)} onClick={(event) => event.stopPropagation()}>
        {children}
      </div>
    </div>,
    document.body,
  );
}

export default Modal;
