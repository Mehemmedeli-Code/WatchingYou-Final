import { useState } from "react";
import { AlertTriangle, Check, ScanLine, X } from "lucide-react";
import { Capacitor } from "@capacitor/core";
import { Section } from "@/components/Shell";
import { TicketCheckIn } from "@/components/TicketCheckIn";
import { post, ApiError } from "@/lib/api";
import { cn } from "@/lib/utils";
import { t, formatWhen } from "@/lib/i18n";

type Outcome = "Admitted" | "AlreadyUsed" | "WrongScreening" | "NotConfirmed" | "NotFound";

interface CheckInResult {
  outcome: Outcome;
  reference?: string | null;
  movieTitle?: string | null;
  hall?: string | null;
  seat?: string | null;
  startsAtUtc?: string | null;
  usedAtUtc?: string | null;
  message: string;
}

/**
 * The door, on a phone. Staff point the camera at a ticket's QR code and get one big answer —
 * green to let them in, red to stop — readable at arm's length in a dark foyer. The same
 * check the website's door page makes, against the same endpoint; the typed form stays
 * underneath for a torn or unreadable code.
 */
export function DoorTab() {
  const [result, setResult] = useState<CheckInResult | null>(null);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const native = Capacitor.isNativePlatform();

  async function scan() {
    setProblem(null);
    setBusy(true);
    try {
      const { BarcodeScanner, BarcodeFormat } = await import("@capacitor-mlkit/barcode-scanning");

      // Android uses Google's ready-made scanner screen, a small module Play Services fetches
      // once. Asked for the first time it is needed, rather than on every scan.
      if (Capacitor.getPlatform() === "android") {
        const { available } = await BarcodeScanner.isGoogleBarcodeScannerModuleAvailable();
        if (!available) {
          await BarcodeScanner.installGoogleBarcodeScannerModule();
          setProblem(t("door.installing", "Installing the scanner… try again in a moment."));
          return;
        }
      } else {
        const permission = await BarcodeScanner.requestPermissions();
        if (permission.camera !== "granted" && permission.camera !== "limited") {
          setProblem(t("door.noCamera", "Camera access is off. Allow it in Settings to scan."));
          return;
        }
      }

      const { barcodes } = await BarcodeScanner.scan({ formats: [BarcodeFormat.QrCode] });
      const payload = barcodes[0]?.rawValue;
      if (!payload) return;   // closed without scanning

      setResult(await post<CheckInResult>("/api/tickets/check-in", { payload, screeningId: null }));
    } catch (err) {
      setProblem(err instanceof ApiError ? err.message : t("door.scanFailed", "The scan did not work. Try again or type the code below."));
    } finally {
      setBusy(false);
    }
  }

  const admitted = result?.outcome === "Admitted";

  return (
    <Section title={t("door.title", "Door")} lede={t("door.lede", "Scan tickets as people come in. Each ticket lets one person in once.")}>
      {native ? (
        <button
          type="button"
          onClick={() => void scan()}
          disabled={busy}
          className="flex h-40 w-full flex-col items-center justify-center gap-3 rounded-2xl bg-accent text-xl font-semibold text-surface shadow-lg shadow-accent/20 active:scale-[0.99] disabled:opacity-60"
        >
          <ScanLine size={44} aria-hidden />
          {busy ? t("door.checking", "Checking…") : t("door.scan", "Scan ticket")}
        </button>
      ) : null}

      {problem ? <p role="alert" className="mt-4 rounded-md border border-bad px-3 py-2 text-sm text-bad">{problem}</p> : null}

      {result ? (
        <div
          role="status"
          className={cn(
            "mt-5 flex items-start gap-4 rounded-2xl border-2 p-5",
            admitted ? "border-good bg-good-bg" : result.outcome === "AlreadyUsed" ? "border-warn bg-warn-bg" : "border-bad bg-bad-bg",
          )}
        >
          <span className={cn("grid h-14 w-14 shrink-0 place-items-center rounded-full",
            admitted ? "bg-good text-surface" : result.outcome === "AlreadyUsed" ? "bg-warn text-surface" : "bg-bad text-surface")}>
            {admitted ? <Check size={32} /> : result.outcome === "AlreadyUsed" ? <AlertTriangle size={28} /> : <X size={32} />}
          </span>
          <div className="min-w-0">
            <p className={cn("font-display text-2xl font-bold", admitted ? "text-good" : result.outcome === "AlreadyUsed" ? "text-warn" : "text-bad")}>
              {admitted ? t("checkin.admitted", "Admitted") : result.outcome === "AlreadyUsed" ? t("checkin.alreadyUsed", "Already used") : t("door.stop", "Do not admit")}
            </p>
            {result.movieTitle ? (
              <p className="mt-1 text-base text-ink">
                {result.movieTitle}{result.seat ? ` · ${result.seat}` : ""}
              </p>
            ) : null}
            <p className="mt-1 text-sm text-ink-mute">
              {[result.hall, result.startsAtUtc ? formatWhen(result.startsAtUtc) : null, result.reference].filter(Boolean).join(" · ")}
            </p>
            {!admitted ? <p className="mt-2 text-sm text-ink">{result.message}</p> : null}
          </div>
        </div>
      ) : null}

      <div className="mt-8">
        <TicketCheckIn />
      </div>
    </Section>
  );
}
