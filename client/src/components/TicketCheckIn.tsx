import { useRef, useState } from "react";
import { ScanLine, Check, X, AlertTriangle } from "lucide-react";
import { Panel, Notice } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Input, Field } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { post, ApiError } from "@/lib/api";
import { t, formatWhen } from "@/lib/i18n";

type Outcome = "Admitted" | "AlreadyUsed" | "WrongScreening" | "NotConfirmed" | "NotFound";

interface CheckInResult {
  outcome: Outcome;
  reference?: string | null;
  movieTitle?: string | null;
  hall?: string | null;
  venueName?: string | null;
  startsAtUtc?: string | null;
  seat?: string | null;
  usedAtUtc?: string | null;
  message: string;
}

const LABEL: Record<Outcome, string> = {
  Admitted: "checkin.admitted",
  AlreadyUsed: "checkin.alreadyUsed",
  WrongScreening: "checkin.wrongScreening",
  NotConfirmed: "checkin.notConfirmed",
  NotFound: "checkin.notFound",
};

/**
 * The door.
 *
 * QR codes were only half a feature while nothing read them back. A handheld scanner types
 * the payload and presses Enter, which is why the input submits on Enter and clears itself:
 * a doorman with a queue never touches the mouse.
 */
export function TicketCheckIn() {
  const [payload, setPayload] = useState("");
  const [result, setResult] = useState<CheckInResult | null>(null);
  const [history, setHistory] = useState<CheckInResult[]>([]);
  const [busy, setBusy] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  async function check() {
    if (!payload.trim()) return;
    setBusy(true);
    try {
      const outcome = await post<CheckInResult>("/api/tickets/check-in", { payload, screeningId: null });
      setResult(outcome);
      setHistory((previous) => [outcome, ...previous].slice(0, 8));
      setPayload("");
      inputRef.current?.focus();
    } catch (err) {
      setResult({
        outcome: "NotFound",
        message: err instanceof ApiError ? err.message : t("error.action"),
      });
    } finally {
      setBusy(false);
    }
  }

  const tone = (outcome: Outcome) => (outcome === "Admitted" ? "good" : outcome === "AlreadyUsed" ? "warn" : "bad");

  return (
    <div className="grid grid-cols-1 gap-6 lg:grid-cols-[1fr_1fr]">
      <Panel>
        <h3 className="flex items-center gap-2 font-display text-xl text-ink">
          <ScanLine size={18} aria-hidden />
          {t("checkin.title")}
        </h3>
        <p className="mt-1 text-sm text-ink-mute">{t("checkin.lede")}</p>

        <div className="mt-4">
          <Field label={t("book.reference")}>
            <Input
              ref={inputRef}
              value={payload}
              onChange={(e) => setPayload(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter") void check(); }}
              placeholder={t("checkin.placeholder")}
              autoFocus
              autoComplete="off"
              spellCheck={false}
            />
          </Field>
        </div>

        <Button className="mt-3" disabled={busy || !payload.trim()} onClick={check}>
          {busy ? t("common.loading") : t("checkin.check")}
        </Button>

        {result ? (
          <div
            className={`mt-5 rounded-xl border p-4 ${
              result.outcome === "Admitted"
                ? "border-accent bg-good-bg"
                : result.outcome === "AlreadyUsed"
                  ? "border-warn bg-warn-bg"
                  : "border-bad bg-bad-bg"
            }`}
          >
            <p className="flex items-center gap-2 font-display text-2xl text-ink">
              {result.outcome === "Admitted" ? <Check size={22} aria-hidden />
                : result.outcome === "AlreadyUsed" ? <AlertTriangle size={22} aria-hidden />
                : <X size={22} aria-hidden />}
              {t(LABEL[result.outcome])}
            </p>

            <p className="mt-2 text-sm text-ink">{result.message}</p>

            {result.reference ? (
              <div className="mt-3 flex flex-wrap gap-1.5">
                <Badge>{result.reference}</Badge>
                {result.seat ? <Badge tone="good">{result.seat}</Badge> : null}
                {result.movieTitle ? <Badge>{result.movieTitle}</Badge> : null}
                {result.venueName ? <Badge>{result.venueName} · {result.hall}</Badge> : null}
              </div>
            ) : null}

            {result.usedAtUtc && result.outcome === "AlreadyUsed" ? (
              <p className="mt-2 text-xs text-ink-mute">{formatWhen(result.usedAtUtc)}</p>
            ) : null}
          </div>
        ) : null}
      </Panel>

      <Panel>
        <h3 className="font-display text-lg text-ink">{t("checkin.recent")}</h3>
        {history.length === 0 ? (
          <p className="mt-2 text-sm text-ink-mute">—</p>
        ) : (
          <div className="mt-3 space-y-2">
            {history.map((entry, i) => (
              <div key={`${entry.reference}-${i}`} className="flex items-center justify-between gap-3 border-b border-line pb-2 text-sm">
                <span className="text-ink">
                  {entry.reference ?? "—"}{entry.seat ? ` · ${entry.seat}` : ""}
                </span>
                <Badge tone={tone(entry.outcome)}>{t(LABEL[entry.outcome])}</Badge>
              </div>
            ))}
          </div>
        )}
        <div className="mt-4">
          <Notice tone="info">{t("checkin.hint", "A handheld scanner types the code and presses Enter by itself — the field is ready for the next ticket straight away.")}</Notice>
        </div>
      </Panel>
    </div>
  );
}
