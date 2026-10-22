import { useEffect, useRef, useState } from "react";
import { Search } from "lucide-react";
import { Section, Panel, Empty, Spinner, Notice } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useAuth } from "@/components/useAuth";
import { get, query, ApiError } from "@/lib/api";
import { FollowList, PersonRow, flip, toggleFollow, type PeoplePage as PeopleResult } from "@/components/FollowList";
import { t } from "@/lib/i18n";


/**
 * Find people by @username (or name) and follow them. Nothing is listed until something is
 * typed: with a large user base "everyone" is not a page, it is a search.
 */
export default function PeoplePage() {
  const { isSignedIn } = useAuth();
  const [term, setTerm] = useState(() => new URLSearchParams(window.location.search).get("q") ?? "");
  const [result, setResult] = useState<PeopleResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Whose followers are open in the Instagram-style list, if any.
  const [followersOf, setFollowersOf] = useState<string | null>(null);

  // The list follows the box from the first letter. A short pause folds a burst of keys into
  // one search, and only the newest answer is shown: a slow reply for "a" must not land on
  // top of the one for "ay".
  const latest = useRef(0);
  useEffect(() => {
    if (!isSignedIn) return;
    const q = term.trim().replace(/^@/, "");
    if (!q) { latest.current++; setResult(null); setLoading(false); return; }
    const timer = window.setTimeout(() => void search(q, 1), 120);
    return () => window.clearTimeout(timer);
  }, [term, isSignedIn]);

  async function search(q: string, page: number) {
    const ticket = ++latest.current;
    setLoading(true);
    setError(null);
    try {
      const next = await get<PeopleResult>("/api/people" + query({ q, page }));
      if (ticket !== latest.current) return;
      setResult((current) => page > 1 && current ? { ...next, items: [...current.items, ...next.items] } : next);
      window.history.replaceState(null, "", `/people?q=${encodeURIComponent(q)}`);
    } catch (e) {
      if (ticket === latest.current) setError(e instanceof ApiError ? e.message : t("error.generic", "Something went wrong."));
    } finally {
      if (ticket === latest.current) setLoading(false);
    }
  }

  if (!isSignedIn) {
    return (
      <Section title={t("people.title", "People")} lede={t("people.lede", "Find someone by their @username and follow them.")}>
        <Empty title={t("people.signedOut", "Sign in to find people.")} hint=""
          action={<a href="/account?returnUrl=%2Fpeople"><Button>{t("nav.signIn")}</Button></a>} />
      </Section>
    );
  }

  const q = term.trim().replace(/^@/, "");
  return (
    <Section title={t("people.title", "People")} lede={t("people.lede", "Find someone by their @username and follow them.")}>
      <div className="relative mb-6 max-w-xl">
        <Search size={16} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-mute" aria-hidden />
        <Input
          autoFocus
          value={term}
          onChange={(e) => setTerm(e.target.value)}
          placeholder={t("people.search", "@username or name")}
          aria-label={t("people.search", "@username or name")}
          className="pl-9"
        />
      </div>

      {error ? <div className="mb-4"><Notice tone="error">{error}</Notice></div> : null}
      {!q ? <p className="text-sm text-ink-mute">{t("people.hint", "Start typing a @username or a name.")}</p> : null}
      {loading && !result ? <Spinner label={t("common.loading")} /> : null}
      {result && result.items.length === 0 && q && !loading ? (
        <Empty title={t("people.none", "Nobody by that name.")} hint="" />
      ) : null}

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        {result?.items.map((person) => (
          <Panel key={person.id} className="space-y-2">
            <PersonRow
              person={person}
              onToggle={() => void toggleFollow(person, (on) =>
                setResult((r) => r && { ...r, items: r.items.map((p) => flip(p, person.id, on)) }))}
            />
            <button className="pl-14 text-xs text-ink-mute hover:text-ink hover:underline" onClick={() => setFollowersOf(person.id)}>
              {person.followers} {t("people.followers", "followers")}
            </button>
          </Panel>
        ))}
      </div>

      {result?.hasMore ? (
        <div className="mt-6 flex justify-center">
          <Button variant="outline" disabled={loading} onClick={() => void search(q, result.page + 1)}>
            {loading ? t("common.loading") : t("globe.loadMore")}
          </Button>
        </div>
      ) : null}

      {followersOf ? <FollowList userId={followersOf} kind="followers" onClose={() => setFollowersOf(null)} /> : null}
    </Section>
  );
}
