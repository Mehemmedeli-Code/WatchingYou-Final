import { useState } from "react";
import { t, lang } from "@/lib/i18n";
import { azn } from "./shared";

export interface DayRevenue {
  day: string;
  boxOffice: number;
  bar: number;
}

/**
 * Daily revenue, box office stacked under the bar. Plain SVG: two series and a handful of
 * days need no chart library. One axis (money), a legend because there are two series, a
 * tooltip per day, and the same numbers as a table for anyone who cannot use the picture.
 */
export function RevenueChart({ data, height = 220 }: { data: DayRevenue[]; height?: number }) {
  const [hover, setHover] = useState<number | null>(null);
  const [asTable, setAsTable] = useState(false);

  const width = 720;
  const pad = { top: 12, right: 8, bottom: 26, left: 56 };
  const innerW = width - pad.left - pad.right;
  const innerH = height - pad.top - pad.bottom;

  const max = Math.max(1, ...data.map((d) => d.boxOffice + d.bar));
  const step = niceStep(max / 4);
  const top = Math.ceil(max / step) * step;
  const ticks = Array.from({ length: Math.round(top / step) + 1 }, (_, i) => i * step);

  const slot = innerW / Math.max(1, data.length);
  const barW = Math.min(36, slot * 0.6);
  const y = (v: number) => pad.top + innerH - (v / top) * innerH;
  // Every nth label, so a month of days does not turn the axis into a smear.
  const labelEvery = Math.ceil(data.length / 12);

  const legend = (
    <div className="flex flex-wrap items-center gap-4 text-xs text-ink-mute">
      <span className="flex items-center gap-1.5">
        <span className="h-2.5 w-2.5 rounded-sm bg-series-1" aria-hidden />{t("bo.boxOffice", "Box office")}
      </span>
      <span className="flex items-center gap-1.5">
        <span className="h-2.5 w-2.5 rounded-sm bg-series-2" aria-hidden />{t("bo.bar", "Bar")}
      </span>
      <button type="button" onClick={() => setAsTable((v) => !v)} className="ml-auto underline-offset-2 hover:text-ink hover:underline">
        {asTable ? t("bo.showChart", "Show chart") : t("bo.showTable", "Show as table")}
      </button>
    </div>
  );

  if (asTable) {
    return (
      <div className="space-y-3">
        {legend}
        <table className="w-full text-sm tabular-nums">
          <thead className="text-xs text-ink-mute">
            <tr className="border-b border-line">
              <th className="py-1.5 text-left font-medium">{t("bo.day", "Day")}</th>
              <th className="py-1.5 text-right font-medium">{t("bo.boxOffice", "Box office")}</th>
              <th className="py-1.5 text-right font-medium">{t("bo.bar", "Bar")}</th>
              <th className="py-1.5 text-right font-medium">{t("bo.total", "Total")}</th>
            </tr>
          </thead>
          <tbody>
            {data.map((d) => (
              <tr key={d.day} className="border-b border-line/60">
                <td className="py-1.5">{d.day}</td>
                <td className="py-1.5 text-right">{azn(d.boxOffice)}</td>
                <td className="py-1.5 text-right">{azn(d.bar)}</td>
                <td className="py-1.5 text-right">{azn(d.boxOffice + d.bar)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    );
  }

  const active = hover !== null ? data[hover] : null;

  return (
    <div className="space-y-3">
      {legend}
      <div className="relative">
        <svg viewBox={`0 0 ${width} ${height}`} className="h-auto w-full" role="img"
          aria-label={t("bo.revenueChart", "Revenue per day, box office and bar")}
          onMouseLeave={() => setHover(null)}>
          {ticks.map((tick) => (
            <g key={tick}>
              <line x1={pad.left} x2={width - pad.right} y1={y(tick)} y2={y(tick)}
                stroke="var(--color-line)" strokeWidth={1} />
              <text x={pad.left - 8} y={y(tick)} dy="0.32em" textAnchor="end" fontSize={11} fill="var(--color-ink-mute)">
                {tick.toLocaleString(lang)}
              </text>
            </g>
          ))}

          {data.map((d, i) => {
            const cx = pad.left + slot * i + slot / 2;
            const x = cx - barW / 2;
            const boxTop = y(d.boxOffice);
            const barTop = y(d.boxOffice + d.bar);
            const base = y(0);
            // A 2px gap between the stacked segments, taken from the upper one.
            const gap = d.boxOffice > 0 && d.bar > 0 ? 2 : 0;
            return (
              <g key={d.day}>
                {hover === i ? (
                  <rect x={pad.left + slot * i} y={pad.top} width={slot} height={innerH} fill="var(--color-line)" opacity={0.45} />
                ) : null}
                {d.boxOffice > 0 ? (
                  <path d={roundedTop(x, boxTop, barW, base - boxTop, d.bar > 0 ? 0 : 4)} fill="var(--color-series-1)" />
                ) : null}
                {d.bar > 0 ? (
                  <path d={roundedTop(x, barTop, barW, Math.max(0, boxTop - barTop - gap), 4)} fill="var(--color-series-2)" />
                ) : null}
                {i % labelEvery === 0 ? (
                  <text x={cx} y={height - 8} textAnchor="middle" fontSize={11} fill="var(--color-ink-mute)">{d.day}</text>
                ) : null}
                {/* Hit target wider than the bar: the whole day's column. */}
                <rect x={pad.left + slot * i} y={pad.top} width={slot} height={innerH} fill="transparent"
                  onMouseEnter={() => setHover(i)} />
              </g>
            );
          })}
        </svg>

        {active && hover !== null ? (
          <div
            className="pointer-events-none absolute top-2 z-10 min-w-40 rounded-md border border-line bg-surface px-3 py-2 text-xs shadow-lg"
            style={{
              left: `${((pad.left + slot * hover + slot / 2) / width) * 100}%`,
              transform: hover > data.length / 2 ? "translateX(calc(-100% - 12px))" : "translateX(12px)",
            }}
          >
            <p className="mb-1 font-medium text-ink">{active.day}</p>
            <p className="flex justify-between gap-4 text-ink-mute"><span>{t("bo.boxOffice", "Box office")}</span><span className="tabular-nums text-ink">{azn(active.boxOffice)}</span></p>
            <p className="flex justify-between gap-4 text-ink-mute"><span>{t("bo.bar", "Bar")}</span><span className="tabular-nums text-ink">{azn(active.bar)}</span></p>
            <p className="mt-1 flex justify-between gap-4 border-t border-line pt-1 text-ink-mute"><span>{t("bo.total", "Total")}</span><span className="tabular-nums text-ink">{azn(active.boxOffice + active.bar)}</span></p>
          </div>
        ) : null}
      </div>
    </div>
  );
}

function niceStep(raw: number): number {
  const power = 10 ** Math.floor(Math.log10(Math.max(raw, 1)));
  const n = raw / power;
  return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10) * power;
}

/** A rectangle with only its top corners rounded — the data end rounds, the baseline does not. */
function roundedTop(x: number, y: number, w: number, h: number, r: number): string {
  if (h <= 0) return "";
  const radius = Math.min(r, w / 2, h);
  return `M${x},${y + h} V${y + radius} Q${x},${y} ${x + radius},${y} H${x + w - radius} Q${x + w},${y} ${x + w},${y + radius} V${y + h} Z`;
}
