import { useEffect, useState } from "react";
import { Panel, Spinner, Empty } from "@/components/Shell";
import { Badge } from "@/components/ui/badge";
import { get } from "@/lib/api";
import { t, formatWhen } from "@/lib/i18n";

interface AuditRecord {
  id: string;
  action: string;
  subject: string;
  reason?: string | null;
  actorId: string;
  actorName: string;
  actorRoles: string;
  atUtc: string;
}

/** Read-only by design. There is no edit and no delete, here or on the server. */
export function AuditTrail() {
  const [entries, setEntries] = useState<AuditRecord[] | null>(null);

  useEffect(() => {
    get<AuditRecord[]>("/api/admin/audit?take=100").then(setEntries).catch(() => setEntries([]));
  }, []);

  if (entries === null) return <Spinner label={t("common.loading")} />;
  if (entries.length === 0) return <Empty title="—" hint={t("admin.auditLede")} />;

  // Approving over a Security flag is the entry worth spotting from across the room.
  const tone = (action: string) =>
    action.includes("rejected") || action.includes("suspended") || action.includes("flagged")
      ? "bad"
      : action.includes("approved") || action.includes("cleared") || action.includes("checkin")
        ? "good"
        : "warn";

  return (
    <div className="space-y-2">
      {entries.map((entry) => (
        <Panel key={entry.id} className="flex flex-wrap items-start justify-between gap-3 py-3">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <Badge tone={tone(entry.action)}>{t(`audit.${entry.action}`, entry.action)}</Badge>
              <span className="text-sm text-ink">{entry.subject}</span>
            </div>
            {entry.reason ? <p className="mt-1 text-xs text-ink-mute">{entry.reason}</p> : null}
          </div>

          <p className="shrink-0 text-right text-xs text-ink-mute">
            {entry.actorName}
            <br />
            {formatWhen(entry.atUtc)}
          </p>
        </Panel>
      ))}
    </div>
  );
}
