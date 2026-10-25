import { useCallback, useEffect, useState } from "react";
import { createPortal } from "react-dom";
import { motion, AnimatePresence } from "motion/react";
import { Star, X } from "lucide-react";
import { Textarea } from "@/components/ui/input";
import { post, ApiError } from "@/lib/api";
import { Notice } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Spinner } from "@/components/Shell";
import { get, query } from "@/lib/api";
import { formatRuntime, formatUsd } from "@/lib/format";
import { useAuth } from "@/components/useAuth";
import { t, formatWhen, genreName, lang } from "@/lib/i18n";
import { isApp } from "@/lib/platform";
import { TrailerPlayer } from "@/components/TrailerPlayer";

interface Show {
  id: string; movieId: string; hall: string; venueName: string; startsAtUtc: string;
  seatPrice: number; capacity: number; seatsTaken: number; audioLanguage: string; subtitleLanguage?: string | null;
}
interface Similar { id: string; title: string; releaseYear: number; posterUrl?: string | null }

export interface MovieDetail {
  id: string;
  title: string;
  slug: string;
  description: string;
  /** The longer write-up in en / az / ru / tr, when one has been written. */
  descriptions?: Record<string, string> | null;
  genre: string;
  releaseYear: number;
  durationMinutes: number;
  director?: string | null;
  posterUrl?: string | null;
  trailerUrl?: string | null;
  videoUrl?: string | null;
  /** The address itself comes from /watch, only to a renter or a PRO member. */
  hasVideo?: boolean;
  dailyPrice: number;
  availableCopies: number;
  totalCopies: number;
  averageRating: number;
  reviewCount: number;
  reviews: { id: string; authorName: string; stars: number; comment: string; createdAtUtc: string }[];
  /** Who plays in it, in billing order; null for a film nobody looked up. */
  cast?: { name: string; role?: string | null; photoUrl?: string | null; names?: Record<string, string> | null; roles?: Record<string, string> | null }[] | null;
}

/**
 * Turns a pasted link into something playable. Most people will paste a YouTube or Vimeo
 * watch URL rather than a file, and those cannot go in a <video> tag — they need the
 * provider's embed player in an iframe.
 */
export function embedFor(url: string): { kind: "iframe" | "file"; src: string } {
  try {
    const parsed = new URL(url);
    const host = parsed.hostname.replace(/^www\./, "");

    if (host === "youtu.be") return { kind: "iframe", src: `https://www.youtube.com/embed${parsed.pathname}` };
    if (host.endsWith("youtube.com")) {
      const id = parsed.searchParams.get("v");
      if (id) return { kind: "iframe", src: `https://www.youtube.com/embed/${id}` };
      if (parsed.pathname.startsWith("/embed/")) return { kind: "iframe", src: url };
    }
    if (host.endsWith("vimeo.com")) {
      const id = parsed.pathname.split("/").filter(Boolean).pop();
      if (id && /^\d+$/.test(id)) return { kind: "iframe", src: `https://player.vimeo.com/video/${id}` };
    }
  } catch {
    /* not a parseable URL — fall through and let the video element try */
  }
  return { kind: "file", src: url };
}

export function MovieDialog({
  movieId: startId,
  mode,
  onClose,
  onRent,
}: {
  movieId: string;
  mode: "details" | "watch";
  onClose: () => void;
  onRent?: (id: string) => void;
}) {
  // A similar film picked inside the dialog replaces the one shown, without closing it.
  const [movieId, setMovieId] = useState(startId);
  const [detail, setDetail] = useState<MovieDetail | null>(null);
  const [shows, setShows] = useState<Show[]>([]);
  const [similar, setSimilar] = useState<Similar[]>([]);
  // Staff (Admin, Security) watch everything and are never offered a rental or PRO.
  const { isSignedIn, isSecurity: isStaff } = useAuth();
  // The phone app sells no digital content (the stores require their own billing for that),
  // so there it shows no prices and no way to rent or subscribe — only what is already yours.
  const inApp = isApp();
  const [playing, setPlaying] = useState(false);
  const [videoUrl, setVideoUrl] = useState<string | null>(null);
  // Why the film would not play: "none" (rent or PRO), "awaitingDecision" (+3 days or return),
  // "signedOut", or null when there is nothing to explain.
  const [blocked, setBlocked] = useState<string | null>(null);
  const [asking, setAsking] = useState(false);
  const [showTrailer, setShowTrailer] = useState(false);

  const watch = useCallback(async () => {
    if (!isSignedIn) { setBlocked("signedOut"); return; }
    setAsking(true);
    try {
      const answer = await get<{ allowed: boolean; access: string; videoUrl?: string | null }>(`/api/movies/${movieId}/watch`);
      if (answer.allowed && answer.videoUrl) {
        setVideoUrl(answer.videoUrl);
        setBlocked(null);
        setPlaying(true);
      } else {
        setBlocked(answer.access);
      }
    } catch {
      setBlocked("none");
    } finally {
      setAsking(false);
    }
  }, [isSignedIn, movieId]);

  // Opened with "Watch": ask straight away.
  useEffect(() => { if (mode === "watch") void watch(); }, [mode, watch]);
  const [stars, setStars] = useState(0);
  const [comment, setComment] = useState("");
  const [posting, setPosting] = useState(false);
  const [reviewMessage, setReviewMessage] = useState<{ tone: "ok" | "error"; text: string } | null>(null);

  const reload = useCallback(
    () => get<MovieDetail>(`/api/movies/${movieId}`).then(setDetail),
    [movieId],
  );

  useEffect(() => {
    reload().catch(() => onClose());
  }, [reload, onClose]);

  // Where it plays in the cinemas, and what else is like it.
  useEffect(() => {
    let live = true;
    setShows([]); setSimilar([]); setPlaying(false); setBlocked(null);
    get<Show[]>("/api/screenings")
      .then((all) => live && setShows(all.filter((x) => x.movieId === movieId && new Date(x.startsAtUtc) > new Date()).slice(0, 4)))
      .catch(() => {});
    return () => { live = false; };
  }, [movieId]);
  useEffect(() => {
    if (!detail) return;
    let live = true;
    get<{ items: Similar[] }>("/api/movies" + query({ genre: detail.genre, pageSize: 7 }))
      .then((r) => live && setSimilar(r.items.filter((m) => m.id !== detail.id).slice(0, 6)))
      .catch(() => {});
    return () => { live = false; };
  }, [detail?.id, detail?.genre]); // eslint-disable-line react-hooks/exhaustive-deps

  async function postReview() {
    setPosting(true);
    setReviewMessage(null);
    try {
      await post(`/api/movies/${movieId}/reviews`, { stars, comment });
      setStars(0);
      setComment("");
      setReviewMessage({ tone: "ok", text: t("review.posted") });
      await reload();
    } catch (err) {
      // The server refuses a review from somebody who never rented the film; say which it was
      // rather than a generic failure.
      setReviewMessage({
        tone: "error",
        text: err instanceof ApiError
          ? (err.status === 403 ? t("review.needRental") : err.message)
          : t("error.action"),
      });
    } finally {
      setPosting(false);
    }
  }

  // Escape closes, and the page behind stops scrolling while the dialog is open.
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === "Escape") onClose(); };
    document.addEventListener("keydown", onKey);
    const previous = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.removeEventListener("keydown", onKey);
      document.body.style.overflow = previous;
    };
  }, [onClose]);

  const source = videoUrl ? embedFor(videoUrl) : null;

  return createPortal(
    <AnimatePresence>
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        className="fixed inset-0 z-[90] flex items-start justify-center overflow-y-auto bg-black/80 p-4 backdrop-blur-sm sm:p-8"
        onClick={onClose}
        role="dialog"
        aria-modal="true"
        aria-label={detail?.title ?? t("common.loading")}
      >
        <motion.div
          initial={{ opacity: 0, y: 18, scale: 0.98 }}
          animate={{ opacity: 1, y: 0, scale: 1 }}
          transition={{ type: "spring", stiffness: 280, damping: 28 }}
          className="my-auto w-full max-w-3xl rounded-2xl border border-line bg-surface-raised p-5 sm:p-6"
          onClick={(event) => event.stopPropagation()}
        >
          {!detail ? <Spinner label={t("common.loading")} /> : (
            <>
              <div className="flex items-start justify-between gap-4">
                <div>
                  <h2 className="font-display text-2xl text-ink">{detail.title}</h2>
                  <p className="mt-1 text-xs text-ink-mute">
                    {genreName(detail.genre)} · {detail.releaseYear} · {formatRuntime(detail.durationMinutes)}
                    {detail.director ? ` · ${detail.director}` : null}
                  </p>
                </div>
                <button onClick={onClose} aria-label={t("common.cancel")} className="text-ink-mute hover:text-ink">
                  <X size={20} aria-hidden />
                </button>
              </div>

              {playing && source ? (
                <div className="mt-4 aspect-video overflow-hidden rounded-lg border border-line bg-black">
                  {source.kind === "iframe" ? (
                    <iframe
                      src={source.src}
                      title={detail.title}
                      className="h-full w-full"
                      allow="accelerometer; autoplay; clipboard-write; encrypted-media; picture-in-picture"
                      allowFullScreen
                    />
                  ) : (
                    <video src={source.src} className="h-full w-full" controls autoPlay playsInline />
                  )}
                </div>
              ) : (
                <>
                  <div className="mt-4 flex gap-4">
                    {detail.posterUrl ? (
                      <img src={detail.posterUrl} alt="" className="h-52 w-36 shrink-0 rounded-lg object-cover sm:h-60 sm:w-40" />
                    ) : null}
                    <p className="whitespace-pre-line text-sm leading-relaxed text-ink/90">{detail.descriptions?.[lang] ?? detail.description}</p>
                  </div>
                  <dl className="mt-5 grid grid-cols-2 gap-px overflow-hidden rounded-xl border border-line bg-line text-sm sm:grid-cols-3">
                    {([
                      [t("movie.genre"), genreName(detail.genre)],
                      [t("movie.year"), String(detail.releaseYear)],
                      [t("movie.runtime"), formatRuntime(detail.durationMinutes)],
                      [t("movie.directorLabel"), detail.director || "—"],
                      [t("movie.rating"), detail.reviewCount > 0 ? `★ ${detail.averageRating.toFixed(1)} (${detail.reviewCount})` : t("movie.noReviews")],
                      [t("movie.copies"), `${detail.availableCopies} / ${detail.totalCopies}`],
                    ] as [string, string][]).map(([k, v]) => (
                      <div key={k} className="bg-surface-raised px-3 py-2.5">
                        <dt className="text-xs text-ink-mute">{k}</dt>
                        <dd className="mt-0.5 font-medium text-ink">{v}</dd>
                      </div>
                    ))}
                  </dl>
                </>
              )}

              <div className="mt-4 flex flex-wrap items-center gap-2">
                {detail.availableCopies > 0 ? null : <Badge tone="bad">{t("movie.allOut")}</Badge>}
                {isStaff || inApp ? null : <Badge>{formatUsd(0.5)} / 3 {t("pro.days", "days")}</Badge>}
                {detail.reviewCount > 0 ? (
                  <Badge tone="warn">
                    <Star size={12} aria-hidden /> {detail.averageRating.toFixed(1)} ({detail.reviewCount})
                  </Badge>
                ) : null}
              </div>

              <div className="mt-5 flex flex-wrap gap-2">
                {detail.hasVideo ? (
                  <Button size="sm" disabled={asking} onClick={() => (playing ? setPlaying(false) : void watch())}>
                    {playing ? t("movie.details") : t("movie.watch")}
                  </Button>
                ) : (
                  <Badge tone="warn">{t("movie.noVideo")}</Badge>
                )}

                {detail.trailerUrl ? (
                  <Button size="sm" variant={detail.hasVideo ? "outline" : "solid"} onClick={() => setShowTrailer(true)}>
                    {t("movie.trailer")}
                  </Button>
                ) : null}
                {showTrailer && detail.trailerUrl ? (
                  <TrailerPlayer film={{ ...detail, trailerUrl: detail.trailerUrl }} onClose={() => setShowTrailer(false)} />
                ) : null}

                {onRent && !isStaff ? (
                  <Button size="sm" variant="outline" disabled={detail.availableCopies === 0}
                          onClick={() => onRent(detail.id)}>
                    {t("movie.rent")} · {formatUsd(0.5)}
                  </Button>
                ) : null}
              </div>

              {blocked ? (
                <div className="mt-4 rounded-xl border border-accent-dim bg-surface p-4">
                  {blocked === "signedOut" ? (
                    <>
                      <p className="text-sm text-ink">{t("pro.signInToWatch", "Sign in to watch.")}</p>
                      <a href="/account" className="mt-3 inline-block"><Button size="sm">{t("nav.signIn")}</Button></a>
                    </>
                  ) : inApp ? (
                    <p className="text-sm text-ink">{t("app.notInLibrary", "This film is not in your library.")}</p>
                  ) : blocked === "awaitingDecision" ? (
                    <>
                      <p className="text-sm text-ink">{t("pro.decideFirst", "Your three days are over. Keep it three more days for $0.50, or return it — nothing is charged while you decide.")}</p>
                      <a href="/rentals" className="mt-3 inline-block"><Button size="sm">{t("pro.goDecide", "Go to my rentals")}</Button></a>
                    </>
                  ) : (
                    <>
                      <p className="text-sm text-ink">{t("pro.howToWatch", "Rent it for 3 days, or watch everything with Watching PRO.")}</p>
                      <div className="mt-3 flex flex-wrap gap-2">
                        {onRent ? (
                          <Button size="sm" variant="outline" onClick={() => onRent(detail.id)}>
                            {t("pro.rentFor", "Rent 3 days")} · {formatUsd(0.5)}
                          </Button>
                        ) : null}
                        <a href="/pro"><Button size="sm">Watching PRO · {formatUsd(5)}/{t("pro.month", "month")}</Button></a>
                      </div>
                    </>
                  )}
                </div>
              ) : null}

              {detail.cast?.length ? (
                <div className="mt-6 border-t border-line pt-4">
                  <h3 className="font-display text-lg text-ink">{t("movie.cast", "Cast")}</h3>
                  <ul className="mt-3 flex gap-3 overflow-x-auto pb-1">
                    {detail.cast.map((a) => (
                      <li key={a.name} className="w-24 shrink-0 text-center">
                        {a.photoUrl
                          ? <img src={a.photoUrl} alt="" loading="lazy" className="mx-auto h-20 w-20 rounded-full border border-line object-cover" />
                          : <div className="mx-auto grid h-20 w-20 place-items-center rounded-full border border-line bg-surface font-display text-lg text-ink-mute" aria-hidden>{(a.names?.[lang] ?? a.name).split(" ").map((w) => w[0]).slice(0, 2).join("")}</div>}
                        <p className="mt-1.5 line-clamp-2 text-xs font-medium text-ink">{a.names?.[lang] ?? a.name}</p>
                        {a.role ? <p className="line-clamp-2 text-[11px] text-ink-mute">{a.roles?.[lang] ?? a.role}</p> : null}
                      </li>
                    ))}
                  </ul>
                </div>
              ) : null}

              {shows.length > 0 ? (
                <div className="mt-6 border-t border-line pt-4">
                  <h3 className="font-display text-lg text-ink">{t("movie.showtimes")}</h3>
                  <ul className="mt-3 space-y-2">
                    {shows.map((x) => (
                      <li key={x.id} className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-line px-3 py-2 text-sm">
                        <span className="text-ink">
                          <span className="font-medium">{x.venueName}</span> · {x.hall}
                          <span className="block text-xs text-ink-mute">
                            {formatWhen(x.startsAtUtc)} · {x.audioLanguage}{x.subtitleLanguage ? ` + ${x.subtitleLanguage}` : ""} · {x.capacity - x.seatsTaken} {t("searching.seatsLeft")}
                          </span>
                        </span>
                        <a href={`/cinema?screening=${x.id}`}><Button size="sm" variant="outline">{formatUsd(x.seatPrice)}</Button></a>
                      </li>
                    ))}
                  </ul>
                </div>
              ) : null}

              {similar.length > 0 ? (
                <div className="mt-6 border-t border-line pt-4">
                  <h3 className="font-display text-lg text-ink">{t("movie.similar")}</h3>
                  <div className="mt-3 flex gap-3 overflow-x-auto pb-1">
                    {similar.map((m) => (
                      <button key={m.id} type="button" onClick={() => setMovieId(m.id)} className="group w-24 shrink-0 text-left">
                        {m.posterUrl ? <img src={m.posterUrl} alt="" className="h-36 w-24 rounded-lg object-cover" /> : <div className="h-36 w-24 rounded-lg bg-surface" />}
                        <p className="mt-1 line-clamp-2 text-xs text-ink group-hover:text-accent">{m.title}</p>
                      </button>
                    ))}
                  </div>
                </div>
              ) : null}

              <div className="mt-6 border-t border-line pt-4">
                <h3 className="font-display text-lg text-ink">{t("review.write")}</h3>

                <div className="mt-2 flex items-center gap-1" role="radiogroup" aria-label={t("review.stars")}>
                  {[1, 2, 3, 4, 5].map((value) => (
                    <button
                      key={value}
                      type="button"
                      role="radio"
                      aria-checked={stars === value}
                      aria-label={`${value}`}
                      onClick={() => setStars(value)}
                      className={value <= stars ? "text-accent" : "text-ink-mute/50 hover:text-ink-mute"}
                    >
                      <Star size={20} fill={value <= stars ? "currentColor" : "none"} aria-hidden />
                    </button>
                  ))}
                </div>

                <Textarea
                  className="mt-3"
                  value={comment}
                  onChange={(e) => setComment(e.target.value)}
                  placeholder={t("review.comment")}
                  rows={3}
                  maxLength={1000}
                />

                {reviewMessage ? (
                  <div className="mt-2"><Notice tone={reviewMessage.tone}>{reviewMessage.text}</Notice></div>
                ) : null}

                <Button
                  className="mt-3"
                  size="sm"
                  disabled={posting || stars === 0 || !comment.trim()}
                  onClick={postReview}
                >
                  {posting ? t("common.loading") : t("review.submit")}
                </Button>
              </div>

              {detail.reviews.length > 0 ? (
                <div className="mt-6 border-t border-line pt-4">
                  <h3 className="font-display text-lg text-ink">{t("movie.reviews")}</h3>
                  <div className="mt-3 space-y-3">
                    {detail.reviews.slice(0, 6).map((review) => (
                      <div key={review.id}>
                        <p className="text-xs text-ink-mute">
                          {review.authorName} · {"★".repeat(review.stars)} · {formatWhen(review.createdAtUtc)}
                        </p>
                        <p className="mt-0.5 text-sm text-ink">{review.comment}</p>
                      </div>
                    ))}
                  </div>
                </div>
              ) : null}
            </>
          )}
        </motion.div>
      </motion.div>
    </AnimatePresence>,
    document.body,
  );
}
