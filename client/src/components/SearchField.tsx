import { Search } from "lucide-react";
import { BorderBeam } from "@/components/ui/border-beam";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import { t } from "@/lib/i18n";

/**
 * Every search box on the site. One component rather than the beam pasted into three pages,
 * which is how three search fields end up looking slightly different from each other.
 */
export function SearchField({
  value,
  onChange,
  placeholder,
  className,
  autoFocus,
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  className?: string;
  autoFocus?: boolean;
}) {
  return (
    <BorderBeam className={cn("w-full", className)}>
      <div className="relative">
        <Search
          size={15}
          aria-hidden
          className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-mute"
        />
        <Input
          className="pl-9"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={placeholder ?? t("common.search")}
          aria-label={t("common.search")}
          autoFocus={autoFocus}
          type="search"
        />
      </div>
    </BorderBeam>
  );
}
