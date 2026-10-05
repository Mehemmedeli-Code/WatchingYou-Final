import { ChevronLeft, ChevronRight } from "lucide-react";
import { Button } from "@/components/ui/button";
import { t } from "@/lib/i18n";

/**
 * Which page numbers to show: always the first and last, the current one with a neighbour on
 * each side, and an ellipsis for any gap. Exported for the smoke test.
 */
export function pageWindow(current: number, total: number): (number | "…")[] {
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);

  const pages = new Set([1, total, current - 1, current, current + 1]);
  const sorted = [...pages].filter((p) => p >= 1 && p <= total).sort((a, b) => a - b);

  const out: (number | "…")[] = [];
  for (let i = 0; i < sorted.length; i++) {
    if (i > 0 && sorted[i] - sorted[i - 1] > 1) out.push("…");
    out.push(sorted[i]);
  }
  return out;
}

export function Pagination({
  page,
  totalPages,
  totalCount,
  onChange,
}: {
  page: number;
  totalPages: number;
  totalCount: number;
  onChange: (page: number) => void;
}) {
  if (totalPages <= 1) {
    return <p className="mt-8 text-center text-sm text-ink-mute">{totalCount} {t("paging.titles", "titles")}</p>;
  }

  return (
    <nav className="mt-8 flex flex-wrap items-center justify-center gap-1.5" aria-label={t("common.pages", "Pages")}>
      <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => onChange(page - 1)} aria-label={t("paging.previous", "Previous")}>
        <ChevronLeft size={14} aria-hidden />
      </Button>

      {pageWindow(page, totalPages).map((p, i) =>
        p === "…" ? (
          <span key={`gap-${i}`} className="px-1 text-sm text-ink-mute" aria-hidden>…</span>
        ) : (
          <Button
            key={p}
            size="sm"
            variant={p === page ? "solid" : "outline"}
            aria-current={p === page ? "page" : undefined}
            onClick={() => onChange(p)}
            className="min-w-9"
          >
            {p}
          </Button>
        ),
      )}

      <Button variant="outline" size="sm" disabled={page >= totalPages} onClick={() => onChange(page + 1)} aria-label={t("paging.next", "Next")}>
        <ChevronRight size={14} aria-hidden />
      </Button>

      <span className="ml-3 text-sm text-ink-mute">{totalCount} {t("paging.titles", "titles")}</span>
    </nav>
  );
}
