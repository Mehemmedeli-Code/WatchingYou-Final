import { cn } from "@/lib/utils";
import { flagUrl } from "@/lib/worldCities";
import { t, languageName } from "@/lib/i18n";

/** The four languages a screening can be shown in, as the admin forms offer them. */
const LANGUAGES = [
  { code: "en", flag: "gb", label: "EN" },
  { code: "az", flag: "az", label: "AZE" },
  { code: "tr", flag: "tr", label: "TR" },
  { code: "ru", flag: "ru", label: "RUS" },
];

/**
 * Audio or subtitle language picked by flag. With `optional`, a "none" choice comes first
 * (subtitles), and the value is "" for it.
 */
export function LanguageFlags({ value, onChange, label, optional }: {
  value: string;
  onChange: (code: string) => void;
  label: string;
  optional?: boolean;
}) {
  const choice = (selected: boolean) => cn(
    "flex h-10 items-center gap-1.5 rounded-lg border px-2.5 text-xs font-semibold transition",
    selected ? "border-accent bg-accent/15 text-ink" : "border-line text-ink-mute hover:border-accent/60 hover:text-ink",
  );

  return (
    <div role="radiogroup" aria-label={label} className="flex flex-wrap gap-1.5">
      {optional ? (
        <button type="button" role="radio" aria-checked={value === ""} onClick={() => onChange("")} className={choice(value === "")}>
          {t("admin.noSubtitles", "None")}
        </button>
      ) : null}
      {LANGUAGES.map((language) => (
        <button key={language.code} type="button" role="radio" aria-checked={value === language.code}
          title={languageName(language.code)} onClick={() => onChange(language.code)} className={choice(value === language.code)}>
          <img src={flagUrl(language.flag)} alt="" className="h-3.5 w-5 rounded-sm object-cover" />
          {language.label}
        </button>
      ))}
    </div>
  );
}
