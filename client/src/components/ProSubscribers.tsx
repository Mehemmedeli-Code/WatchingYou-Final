import { useEffect, useState } from "react";
import { Panel, Spinner } from "@/components/Shell";
import { Badge } from "@/components/ui/badge";
import { get } from "@/lib/api";
import { formatDate, formatUsd } from "@/lib/format";
import { t } from "@/lib/i18n";

interface Row {
  userId: string;
  name: string;
  email: string;
  active: boolean;
  firstPaidAtUtc: string;
  endsAtUtc: string;
  daysLeft: number;
  months: number;
  totalPaid: number;
  lastCard: string;
  reminderSentAtUtc?: string | null;
}

interface Overview {
  active: number;
  endingIn3Days: number;
  lapsed: number;
  revenueThisMonth: number;
  revenueTotal: number;
  subscribers: Row[];
}

/** Everyone who has paid for Watching PRO, for Admin and Security. Read-only. */
export function ProSubscribers() {
  const [data, setData] = useState<Overview | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    get<Overview>("/api/pro/subscribers").then(setData).catch(() => setFailed(true));
  }, []);

  if (failed) return <p className="text-sm text-ink-mute">{t("error.action")}</p>;
  if (!data) return <Spinner label={t("common.loading")} />;

  const tiles = [
    { label: t("proAdmin.active", "Active"), value: String(data.active) },
    { label: t("proAdmin.ending", "Ending within 3 days"), value: String(data.endingIn3Days) },
    { label: t("proAdmin.lapsed", "Lapsed"), value: String(data.lapsed) },
    { label: t("proAdmin.month", "Revenue this month"), value: formatUsd(data.revenueThisMonth) },
    { label: t("proAdmin.total", "Revenue in total"), value: formatUsd(data.revenueTotal) },
  ];

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-5">
        {tiles.map((tile) => (
          <Panel key={tile.label} className="p-4">
            <p className="text-xs text-ink-mute">{tile.label}</p>
            <p className="mt-1 font-display text-2xl text-ink">{tile.value}</p>
          </Panel>
        ))}
      </div>

      {data.subscribers.length === 0 ? (
        <p className="py-4 text-sm text-ink-mute">{t("proAdmin.none", "Nobody has subscribed yet.")}</p>
      ) : (
        <div className="overflow-x-auto rounded-xl border border-line">
          <table className="w-full min-w-[760px] text-left text-sm">
            <thead className="bg-surface text-xs uppercase tracking-wider text-ink-mute">
              <tr>
                <th className="px-3 py-2">{t("proAdmin.member", "Member")}</th>
                <th className="px-3 py-2">{t("proAdmin.status", "Status")}</th>
                <th className="px-3 py-2">{t("proAdmin.since", "Since")}</th>
                <th className="px-3 py-2">{t("proAdmin.until", "Until")}</th>
                <th className="px-3 py-2">{t("proAdmin.months", "Months")}</th>
                <th className="px-3 py-2">{t("proAdmin.paid", "Paid")}</th>
                <th className="px-3 py-2">{t("proAdmin.card", "Card")}</th>
                <th className="px-3 py-2">{t("proAdmin.reminder", "3-day e-mail")}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {data.subscribers.map((row) => (
                <tr key={row.userId}>
                  <td className="px-3 py-2">
                    <p className="text-ink">{row.name}</p>
                    <p className="text-xs text-ink-mute">{row.email}</p>
                  </td>
                  <td className="px-3 py-2">
                    {row.active ? (
                      <Badge tone={row.daysLeft <= 3 ? "warn" : "good"}>
                        {t("proAdmin.active", "Active")} · {row.daysLeft} {t("pro.days", "days")}
                      </Badge>
                    ) : (
                      <Badge>{t("proAdmin.lapsed", "Lapsed")}</Badge>
                    )}
                  </td>
                  <td className="px-3 py-2 text-ink-mute">{formatDate(row.firstPaidAtUtc)}</td>
                  <td className="px-3 py-2 text-ink">{formatDate(row.endsAtUtc)}</td>
                  <td className="px-3 py-2 text-ink">{row.months}</td>
                  <td className="px-3 py-2 text-ink">{formatUsd(row.totalPaid)}</td>
                  <td className="px-3 py-2 text-ink-mute">{row.lastCard}</td>
                  <td className="px-3 py-2 text-ink-mute">
                    {row.reminderSentAtUtc ? formatDate(row.reminderSentAtUtc) : "—"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
