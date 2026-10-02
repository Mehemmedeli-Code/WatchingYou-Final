import { useEffect, useState } from "react";
import { Modal } from "@/components/ui/modal";
import { X } from "lucide-react";
import { Panel, Spinner, Empty } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { get } from "@/lib/api";
import { t } from "@/lib/i18n";

interface GenreShare { genre: string; mine: number; theirs: number }

interface TasteComparison {
  otherUserId: string;
  otherName: string;
  matchPercent: number;
  sharedTitles: number;
  myTotal: number;
  theirTotal: number;
  genres: GenreShare[];
  sharedFilms: string[];
}

/**
 * "How alike are our tastes?" — the question that makes a stranger on a map worth talking to.
 *
 * The match is computed over genres, not titles. Two people who have watched no film in common
 * can still both live on westerns and documentaries, and that is the useful thing to know
 * before starting a conversation.
 */
export function TasteCompare({
  member,
  onClose,
}: {
  member: { userId: string; displayName: string; avatarUrl?: string | null };
  onClose: () => void;
}) {
  const [data, setData] = useState<TasteComparison | null>(null);
  const [missing, setMissing] = useState(false);

  useEffect(() => {
    get<TasteComparison>(`/api/taste/compare/${member.userId}`)
      .then(setData)
      .catch(() => setMissing(true));
  }, [member.userId]);

  const peak = Math.max(1, ...(data?.genres.flatMap((g) => [g.mine, g.theirs]) ?? [1]));

  return (
    <Modal onClose={onClose} label={member.displayName} layer={92}>
        <Panel>
          <div className="flex items-start justify-between gap-3">
            <div className="flex items-center gap-3">
              {member.avatarUrl ? (
                <img src={member.avatarUrl} alt="" className="h-10 w-10 rounded-full object-cover" />
              ) : (
                <span className="flex h-10 w-10 items-center justify-center rounded-full bg-surface text-ink-mute">
                  {member.displayName.slice(0, 1).toUpperCase()}
                </span>
              )}
              <div>
                <p className="font-display text-xl text-ink">{member.displayName}</p>
                <p className="text-xs text-ink-mute">{t("globe.compare")}</p>
              </div>
            </div>
            <button onClick={onClose} aria-label={t("common.cancel")} className="text-ink-mute hover:text-ink">
              <X size={18} aria-hidden />
            </button>
          </div>

          {missing ? (
            <div className="mt-6"><Empty title="—" hint={t("globe.empty")} /></div>
          ) : !data ? (
            <div className="mt-6"><Spinner label={t("common.loading")} /></div>
          ) : (
            <>
              <div className="mt-5 flex items-baseline gap-3">
                <span className="font-display text-5xl text-accent">{data.matchPercent}%</span>
                <span className="text-sm text-ink-mute">{t("globe.match")}</span>
              </div>

              {data.genres.length === 0 ? (
                <p className="mt-4 text-sm text-ink-mute">{t("globe.empty")}</p>
              ) : (
                <div className="mt-5 space-y-3">
                  {data.genres.map((genre) => (
                    <div key={genre.genre}>
                      <div className="flex items-baseline justify-between text-xs">
                        <span className="text-ink">{genre.genre}</span>
                        <span className="text-ink-mute">{genre.mine} · {genre.theirs}</span>
                      </div>

                      {/* Two bars against a shared scale, so the shapes can be read against
                          each other rather than each against itself. */}
                      <div className="mt-1 space-y-1">
                        <div className="h-2 rounded-full bg-surface">
                          <div className="h-2 rounded-full bg-accent" style={{ width: `${(genre.mine / peak) * 100}%` }} />
                        </div>
                        <div className="h-2 rounded-full bg-surface">
                          <div className="h-2 rounded-full bg-accent-dim" style={{ width: `${(genre.theirs / peak) * 100}%` }} />
                        </div>
                      </div>
                    </div>
                  ))}

                  <p className="flex gap-4 text-xs text-ink-mute">
                    <span className="flex items-center gap-1.5">
                      <i className="h-2 w-4 rounded-full bg-accent" />{t("globe.you")}
                    </span>
                    <span className="flex items-center gap-1.5">
                      <i className="h-2 w-4 rounded-full bg-accent-dim" />{t("globe.them")}
                    </span>
                  </p>
                </div>
              )}

              {data.sharedFilms.length > 0 ? (
                <div className="mt-5 border-t border-line pt-4">
                  <p className="text-sm text-ink">{t("globe.sharedFilms")}</p>
                  <div className="mt-2 flex flex-wrap gap-1.5">
                    {data.sharedFilms.map((film) => <Badge key={film} tone="good">{film}</Badge>)}
                  </div>
                </div>
              ) : null}

              <Button className="mt-5 w-full" variant="outline" onClick={onClose}>
                {t("common.cancel")}
              </Button>
            </>
          )}
        </Panel>
    </Modal>
  );
}
