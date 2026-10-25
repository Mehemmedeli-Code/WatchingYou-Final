import { useEffect, useRef, useState } from "react";
import { ArrowRight, ArrowUp } from "lucide-react";
import { SearchField } from "@/components/SearchField";
import { t } from "@/lib/i18n";
import { cn } from "@/lib/utils";

let numbered = 0;   // ids for the sections, never reused (a section that appears later gets a new one)

/**
 * The admin page's table of contents, in its top right corner: the site's search field, and
 * under it, once clicked, every section of the page; type to narrow it, Enter or a click to go
 * there. The list is read from the page's own section headings, so a new section appears here
 * by itself. It stays at the top of the page rather than following the scroll.
 */
export function AdminJump() {
  const box = useRef<HTMLDivElement>(null);
  const [sections, setSections] = useState<{ id: string; title: string }[]>([]);
  const [text, setText] = useState("");
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  // The way back to the top, once the page has been scrolled a screen or so.
  const [far, setFar] = useState(false);
  useEffect(() => {
    const onScroll = () => setFar(window.scrollY > 600);
    onScroll();
    window.addEventListener("scroll", onScroll, { passive: true });
    return () => window.removeEventListener("scroll", onScroll);
  }, []);

  // Read again whenever the page changes: some sections (Health) appear once their data is in.
  useEffect(() => {
    const main = document.querySelector("main");
    if (!main) return;
    const read = () => {
      const next = [...main.querySelectorAll<HTMLHeadingElement>("section h2")].map((h) => {
        const section = h.closest("section")!;
        section.id ||= `admin-section-${++numbered}`;
        return { id: section.id, title: h.textContent?.trim() ?? "" };
      });
      setSections((now) => (JSON.stringify(now) === JSON.stringify(next) ? now : next));
    };
    read();
    const observer = new MutationObserver(read);
    observer.observe(main, { childList: true, subtree: true });
    return () => observer.disconnect();
  }, []);

  // A click anywhere else closes the list.
  useEffect(() => {
    if (!open) return;
    const away = (e: PointerEvent) => { if (!box.current?.contains(e.target as Node)) setOpen(false); };
    document.addEventListener("pointerdown", away);
    return () => document.removeEventListener("pointerdown", away);
  }, [open]);

  const needle = text.trim().toLowerCase();
  const shown = needle ? sections.filter((s) => s.title.toLowerCase().includes(needle)) : sections;

  function go(id: string) {
    const section = document.getElementById(id);
    if (!section) return;
    // Clear of the sticky site header.
    section.style.scrollMarginTop = `${(document.querySelector(".rr-header")?.getBoundingClientRect().height ?? 0) + 8}px`;
    section.scrollIntoView({ behavior: "smooth", block: "start" });
    setText("");
    setOpen(false);
  }

  function onKey(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      e.preventDefault();
      setOpen(true);
      setActive((i) => (shown.length ? (i + (e.key === "ArrowDown" ? 1 : -1) + shown.length) % shown.length : 0));
    } else if (e.key === "Enter" && shown[active]) go(shown[active].id);
    else if (e.key === "Escape") setOpen(false);
  }

  return (
    // Phones and tablets: a row of its own above the sections. A wide screen: the top right
    // corner, level with the first section's title.
    <div className="relative mx-auto w-full max-w-6xl px-4 pt-8 sm:px-6 lg:pt-0">
      <button type="button" onClick={() => window.scrollTo({ top: 0, behavior: "smooth" })}
        aria-label={t("admin.toTop", "Back to the top")} title={t("admin.toTop", "Back to the top")} tabIndex={far ? 0 : -1}
        className={cn("fixed bottom-[max(1.5rem,env(safe-area-inset-bottom))] right-6 z-[70] grid h-12 w-12 place-items-center rounded-full border border-accent/50 bg-surface-raised/90 text-accent shadow-lg shadow-black/40 backdrop-blur transition-all duration-200 hover:bg-accent hover:text-surface",
          far ? "translate-y-0 opacity-100" : "pointer-events-none translate-y-3 opacity-0")}>
        <ArrowUp size={20} aria-hidden />
      </button>
      <div ref={box} className="relative ml-auto w-full sm:w-80 lg:absolute lg:right-6 lg:top-12 lg:z-20">
        <SearchField
          value={text}
          onChange={(v) => { setText(v); setActive(0); setOpen(true); }}
          placeholder={t("admin.jump", "Go to a section")}
          inputProps={{
            onFocus: () => setOpen(true),
            onKeyDown: onKey,
            role: "combobox",
            "aria-expanded": open,
            "aria-controls": "admin-jump-list",
            "aria-label": t("admin.jump", "Go to a section"),
          }}
        />
        {open ? (
          <ul id="admin-jump-list" role="listbox"
            className="absolute inset-x-0 top-full z-30 mt-2 max-h-[60vh] overflow-y-auto rounded-2xl border border-line bg-surface-raised p-1.5 shadow-2xl shadow-black/40">
            {shown.map((s, i) => (
              <li key={s.id} role="option" aria-selected={i === active}>
                <button type="button" onClick={() => go(s.id)} onPointerEnter={() => setActive(i)}
                  className={cn("group flex w-full items-center gap-3 rounded-xl px-3 py-2.5 text-left text-sm transition-colors",
                    i === active ? "bg-accent/12 text-accent" : "text-ink hover:bg-line/40")}>
                  <span className={cn("grid h-6 w-6 shrink-0 place-items-center rounded-full border text-[11px] tabular-nums",
                    i === active ? "border-accent/60 text-accent" : "border-line text-ink-mute")}>
                    {sections.indexOf(s) + 1}
                  </span>
                  <span className="min-w-0 flex-1 truncate">{s.title}</span>
                  <ArrowRight size={14} aria-hidden className={cn("shrink-0 transition-opacity", i === active ? "opacity-100" : "opacity-0")} />
                </button>
              </li>
            ))}
            {shown.length === 0 ? <li className="px-3 py-2.5 text-sm text-ink-mute">{t("admin.jumpNone", "No section by that name.")}</li> : null}
          </ul>
        ) : null}
      </div>
    </div>
  );
}
