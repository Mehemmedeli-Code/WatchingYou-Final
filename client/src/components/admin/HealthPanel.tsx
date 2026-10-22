import { useEffect, useState } from "react";
import { Section, Panel } from "@/components/Shell";
import { Badge } from "@/components/ui/badge";
import { get } from "@/lib/api";
import { formatWhen, t } from "@/lib/i18n";

interface HealthReport {
  name: string;
  atUtc: string;
  ok: boolean;
  summary?: string | null;
  problems: string[];
}

/**
 * What the checks outside the site found: the database's business rules and links (daily, after
 * the backup) and the full API check (nightly, and before every deploy). The owner is e-mailed
 * when one of them finds a problem; this is where to read it.
 */
export function HealthPanel() {
  const [reports, setReports] = useState<HealthReport[] | null>(null);
  useEffect(() => { void get<HealthReport[]>("/api/admin/health").then(setReports).catch(() => setReports([])); }, []);
  if (!reports || reports.length === 0) return null;

  // A day and a half without a new report means the check itself stopped running.
  const stale = (r: HealthReport) => Date.now() - new Date(r.atUtc).getTime() > 36 * 3600_000;
  return (
    <Section title={t("health.title", "Health")} lede={t("health.lede", "The daily database check and the nightly API check. The owner is e-mailed when one finds a problem.")}>
      <div className="grid gap-3 sm:grid-cols-2">
        {reports.map((r) => (
          <Panel key={r.name}>
            <div className="flex flex-wrap items-center gap-2">
              <p className="font-display text-lg text-ink">{t(r.name === "db" ? "health.db" : "health.api", r.name)}</p>
              {r.ok ? <Badge tone="good">{t("health.ok", "All clear")}</Badge> : <Badge tone="bad">{t("health.problems", "Problems")}: {r.problems.length}</Badge>}
              {stale(r) ? <Badge tone="warn">{t("health.stale", "Not run lately")}</Badge> : null}
            </div>
            <p className="mt-1 text-xs text-ink-mute">{formatWhen(r.atUtc)}{r.summary ? ` · ${r.summary}` : ""}</p>
            {r.problems.length > 0 ? (
              <ul className="mt-3 space-y-1 text-xs text-bad">
                {r.problems.slice(0, 20).map((p, i) => <li key={i} className="break-words">{p}</li>)}
              </ul>
            ) : null}
          </Panel>
        ))}
      </div>
    </Section>
  );
}
