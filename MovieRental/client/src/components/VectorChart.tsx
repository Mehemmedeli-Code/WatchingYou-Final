import { motion } from "motion/react";
import { t } from "@/lib/i18n";

export interface ChartPoint {
  label: string;
  value: number;
}

export interface ChartSeries {
  key: string;
  title: string;
  unit: string;
  caption: string;
  points: ChartPoint[];
}

/**
 * Hand-drawn SVG rather than a charting library: three small series do not justify
 * shipping a chart engine, and plain SVG inherits the page's palette and focus styles.
 */
export function VectorBarChart({ series }: { series: ChartSeries }) {
  if (series.points.length === 0) {
    return (
      <div className="rounded-xl border border-line bg-surface-raised p-5">
        <h3 className="font-display text-lg text-ink">{series.title}</h3>
        <p className="mt-1 text-sm text-ink-mute">{series.caption}</p>
        <p className="mt-8 mb-6 text-center text-sm text-ink-mute">
          {t("chart.empty")}
        </p>
      </div>
    );
  }

  const width = 320;
  const height = 170;
  const padLeft = 34;
  const padBottom = 26;
  const max = Math.max(...series.points.map((p) => p.value), 1);
  const plotW = width - padLeft - 8;
  const plotH = height - padBottom - 14;
  const slot = plotW / series.points.length;
  const barW = Math.min(30, slot * 0.56);

  return (
    <figure className="rounded-xl border border-line bg-surface-raised p-4">
      <figcaption className="mb-3">
        <h3 className="font-display text-lg text-ink">{series.title}</h3>
        <p className="text-xs text-ink-mute">{series.caption}</p>
      </figcaption>

      <svg viewBox={`0 0 ${width} ${height}`} className="w-full" role="img" aria-label={`${series.title}, ${series.unit}`}>
        {[0, 0.25, 0.5, 0.75, 1].map((t) => {
          const y = 14 + plotH * (1 - t);
          return (
            <g key={t}>
              <line x1={padLeft} x2={width - 8} y1={y} y2={y} stroke="var(--color-line)" strokeWidth="1" />
              <text x={padLeft - 6} y={y + 3.5} textAnchor="end" fontSize="8" fill="var(--color-ink-mute)">
                {Math.round(max * t)}
              </text>
            </g>
          );
        })}

        {series.points.map((point, i) => {
          const barH = (point.value / max) * plotH;
          const x = padLeft + i * slot + (slot - barW) / 2;
          return (
            <g key={point.label}>
              <motion.rect
                x={x}
                width={barW}
                rx="2"
                fill={i % 2 === 0 ? "var(--color-accent)" : "var(--color-accent-dim)"}
                initial={{ height: 0, y: 14 + plotH }}
                animate={{ height: barH, y: 14 + plotH - barH }}
                transition={{ type: "spring", stiffness: 180, damping: 20, delay: i * 0.06 }}
              />
              <text x={x + barW / 2} y={height - padBottom + 14} textAnchor="middle" fontSize="8" fill="var(--color-ink-mute)">
                {point.label}
              </text>
              <text x={x + barW / 2} y={14 + plotH - barH - 4} textAnchor="middle" fontSize="8.5" fill="var(--color-ink)">
                {point.value}
              </text>
            </g>
          );
        })}
      </svg>

      <p className="mt-1 text-right text-[11px] text-ink-mute">{series.unit}</p>
    </figure>
  );
}
