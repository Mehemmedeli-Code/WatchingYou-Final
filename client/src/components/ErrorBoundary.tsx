import { Component, type ErrorInfo, type ReactNode } from "react";
import { t } from "@/lib/i18n";

/**
 * Without one of these, a single failing component takes the whole page with it: React
 * unmounts the tree and the visitor gets a blank screen with no clue why. That is what
 * happened here — a widget that could not start WebGL blanked an entire page.
 *
 * A boundary turns that into a contained message, and prints the real error to the console
 * so the cause is one keypress away instead of a guess.
 */
export class ErrorBoundary extends Component<
  { children: ReactNode; label?: string; fallback?: ReactNode },
  { error: Error | null }
> {
  state: { error: Error | null } = { error: null };

  static getDerivedStateFromError(error: Error) {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error(`[${this.props.label ?? "component"}] failed:`, error, info.componentStack);
  }

  render() {
    if (!this.state.error) return this.props.children;
    if (this.props.fallback !== undefined) return this.props.fallback;

    return (
      <div className="rounded-xl border border-bad-bg bg-bad-bg/40 p-4 text-sm text-bad">
        <p className="font-medium">{t("error.partFailed", "This part of the page could not load.")}</p>
        <p className="mt-1 text-xs opacity-80">{this.state.error.message}</p>
      </div>
    );
  }
}
