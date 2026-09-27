import { useCallback, useEffect, useState } from "react";
import { Empty, Spinner } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { get, query } from "@/lib/api";
import { t } from "@/lib/i18n";
import { HelpThreadCard, type HelpThreadItem } from "@/components/HelpThread";

/** The Security desk's side of the help service. */
export function HelpInbox() {
  const [threads, setThreads] = useState<HelpThreadItem[] | null>(null);
  const [includeClosed, setIncludeClosed] = useState(false);

  const load = useCallback(async () => {
    setThreads(await get<HelpThreadItem[]>("/api/help/inbox" + query({ includeClosed })).catch(() => []));
  }, [includeClosed]);

  useEffect(() => { void load(); }, [load]);

  return (
    <>
      <div className="mb-4">
        <Button
          size="sm"
          variant="outline"
          aria-pressed={includeClosed}
          onClick={() => setIncludeClosed((v) => !v)}
        >
          {t("help.showClosed")}
        </Button>
      </div>

      {threads === null ? <Spinner label={t("common.loading")} /> : null}
      {threads?.length === 0 ? <Empty title={t("help.empty")} hint="" /> : null}

      <div className="space-y-3">
        {threads?.map((thread) => (
          <HelpThreadCard key={thread.id} thread={thread} onChanged={load} desk />
        ))}
      </div>
    </>
  );
}
