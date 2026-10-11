import { useCallback, useEffect, useState } from "react";
import { Panel, Notice, Spinner } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input, Field, Select } from "@/components/ui/input";
import { BannerSlide, type Banner } from "@/components/PromoBoard";
import { get, put, ApiError } from "@/lib/api";
import { t } from "@/lib/i18n";

interface PromoCodeRow { id: string; code: string; percentOff?: number | null; amountOff?: number | null; isActive: boolean }

/**
 * The four boards on the catalogue page. Each one: shown or not, its place, its days, the
 * promo code it sells, and — only if wanted — its own words. The preview is the board as
 * visitors will see it, so a change is checked before it is saved.
 */
export function BannerAdmin() {
  const [rows, setRows] = useState<Banner[] | null>(null);
  const [codes, setCodes] = useState<PromoCodeRow[]>([]);

  const load = useCallback(async () => {
    setRows(await get<Banner[]>("/api/admin/banners").catch(() => []));
    setCodes(await get<PromoCodeRow[]>("/api/admin/promos").catch(() => []));
  }, []);
  useEffect(() => { void load(); }, [load]);

  if (rows === null) return <Spinner />;
  return (
    <div className="space-y-6">
      {rows.map((banner) => <BannerEditor key={banner.id} banner={banner} codes={codes} onSaved={load} />)}
    </div>
  );
}

function BannerEditor({ banner, codes, onSaved }: { banner: Banner; codes: PromoCodeRow[]; onSaved: () => void }) {
  const [draft, setDraft] = useState(banner);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ tone: "ok" | "error"; text: string } | null>(null);
  useEffect(() => setDraft(banner), [banner]);

  const brand = banner.theme === "Brand";
  const code = codes.find((c) => c.code === draft.promoCode);
  const preview: Banner = { ...draft, percentOff: code?.percentOff ?? null, amountOff: code?.amountOff ?? null };
  const changed = JSON.stringify(draft) !== JSON.stringify(banner);

  async function save() {
    setBusy(true);
    setMessage(null);
    try {
      await put(`/api/admin/banners/${banner.id}`, {
        isActive: draft.isActive, sortOrder: draft.sortOrder,
        headline: draft.headline || null, subline: draft.subline || null,
        promoCode: draft.promoCode || null, startsOn: draft.startsOn || null, endsOn: draft.endsOn || null,
      });
      setMessage({ tone: "ok", text: t("banner.saved", "Saved. The catalogue shows it from now on.") });
      onSaved();
    } catch (err) {
      setMessage({ tone: "error", text: err instanceof ApiError && err.message ? err.message : t("error.action") });
    } finally {
      setBusy(false);
    }
  }

  return (
    <Panel>
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <p className="font-display text-lg text-ink">{t(`banner.${banner.theme.toLowerCase()}.name`, banner.theme)}</p>
        {banner.showing
          ? <Badge tone="good">{t("banner.live", "On the site now")}</Badge>
          : <Badge tone="warn">{banner.isActive ? t("banner.outOfSeason", "Waiting for its days") : t("banner.off", "Off")}</Badge>}
      </div>

      <div className="h-48 overflow-hidden rounded-xl border border-line sm:h-56"><BannerSlide banner={preview} /></div>

      <div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Field label={t("banner.show", "Show")}>
          <Select value={draft.isActive ? "1" : "0"} onChange={(e) => setDraft({ ...draft, isActive: e.target.value === "1" })}>
            <option value="1">{t("banner.on", "On")}</option>
            <option value="0">{t("banner.off", "Off")}</option>
          </Select>
        </Field>
        <Field label={t("banner.order", "Order")}>
          <Input type="number" min={0} max={99} value={draft.sortOrder} onChange={(e) => setDraft({ ...draft, sortOrder: Number(e.target.value) })} />
        </Field>
        <Field label={t("banner.from", "First day")} hint={t("banner.openEnded", "Empty: no limit")}>
          <Input type="date" value={draft.startsOn ?? ""} onChange={(e) => setDraft({ ...draft, startsOn: e.target.value || null })} />
        </Field>
        <Field label={t("banner.to", "Last day")} hint={t("banner.openEnded", "Empty: no limit")}>
          <Input type="date" value={draft.endsOn ?? ""} onChange={(e) => setDraft({ ...draft, endsOn: e.target.value || null })} />
        </Field>
        {brand ? null : (
          <Field label={t("banner.promo", "Promo code (the discount comes from it)")}>
            <Select value={draft.promoCode ?? ""} onChange={(e) => setDraft({ ...draft, promoCode: e.target.value || null })}>
              <option value="">—</option>
              {codes.map((c) => (
                <option key={c.id} value={c.code}>
                  {c.code} · {c.percentOff ? `${c.percentOff}%` : `${c.amountOff} ₼`}{c.isActive ? "" : ` (${t("banner.off", "Off")})`}
                </option>
              ))}
            </Select>
          </Field>
        )}
        <Field label={t("banner.headline", "Headline")} hint={t("banner.ownWords", "Empty: the theme's own words, in every language")}>
          <Input maxLength={80} value={draft.headline ?? ""} onChange={(e) => setDraft({ ...draft, headline: e.target.value })} />
        </Field>
        <div className="sm:col-span-2">
          <Field label={t("banner.subline", "Second line")} hint={t("banner.ownWords", "Empty: the theme's own words, in every language")}>
            <Input maxLength={140} value={draft.subline ?? ""} onChange={(e) => setDraft({ ...draft, subline: e.target.value })} />
          </Field>
        </div>
      </div>

      {message ? <div className="mt-3"><Notice tone={message.tone}>{message.text}</Notice></div> : null}
      <div className="mt-4 flex gap-2">
        <Button disabled={busy || !changed} onClick={() => void save()}>{busy ? t("common.loading") : t("common.save", "Save")}</Button>
        {changed ? <Button variant="outline" disabled={busy} onClick={() => setDraft(banner)}>{t("common.cancel")}</Button> : null}
      </div>
    </Panel>
  );
}
