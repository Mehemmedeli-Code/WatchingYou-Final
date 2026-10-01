import { useCallback, useEffect, useState } from "react";
import { Flag } from "lucide-react";
import { Panel, Empty, Spinner } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { get, post, query } from "@/lib/api";
import { t, formatWhen } from "@/lib/i18n";

interface ReportRow {
  id: string;
  reporterEmail: string;
  aboutEmail: string;
  quote: string;
  reason?: string | null;
  handled: boolean;
  createdAtUtc: string;
}

/**
 * Reported messages, for the Security desk.
 *
 * The quoted text was copied into the report when it was made, so it is still here after the
 * sender deletes it. Marking one handled leaves the row and the audit entry in place — the
 * point of a report queue is that somebody can see what was decided afterwards.
 */
export function ReportQueue() {
  const [rows, setRows] = useState<ReportRow[] | null>(null);
  const [includeHandled, setIncludeHandled] = useState(false);

  const load = useCallback(async () => {
    setRows(await get<ReportRow[]>("/api/reports" + query({ includeHandled })).catch(() => []));
  }, [includeHandled]);

  useEffect(() => { void load(); }, [load]);

  return (
    <>
      <div className="mb-4">
        <Button size="sm" variant="outline" aria-pressed={includeHandled} onClick={() => setIncludeHandled((v) => !v)}>
          {t("reports.showHandled")}
        </Button>
      </div>

      {rows === null ? <Spinner label={t("common.loading")} /> : null}
      {rows?.length === 0 ? <Empty title={t("reports.empty")} hint="" /> : null}

      <div className="space-y-3">
        {rows?.map((row) => (
          <Panel key={row.id}>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <p className="flex items-center gap-1.5 text-sm text-ink">
                  <Flag size={13} aria-hidden />
                  {t("reports.about")}: {row.aboutEmail}
                </p>
                <p className="mt-1 text-xs text-ink-mute">
                  {t("reports.by")}: {row.reporterEmail} · {formatWhen(row.createdAtUtc)}
                </p>
              </div>

              <div className="flex items-center gap-2">
                {row.handled ? <Badge tone="good">{t("reports.resolve")}</Badge> : null}
                {!row.handled ? (
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={async () => {
                      await post(`/api/reports/${row.id}/resolve`).catch(() => null);
                      await load();
                    }}
                  >
                    {t("reports.resolve")}
                  </Button>
                ) : null}
              </div>
            </div>

            <blockquote className="mt-3 border-l-2 border-line pl-3 text-sm italic text-ink-mute">
              {row.quote}
            </blockquote>

            {row.reason ? <p className="mt-2 text-xs text-warn">{row.reason}</p> : null}
          </Panel>
        ))}
      </div>
    </>
  );
}
