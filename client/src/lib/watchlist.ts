import { useEffect, useState } from "react";
import { auth, del, get, put } from "@/lib/api";

/**
 * The signed-in customer's "watch later" ids, shared by every heart on the page.
 *
 * A module-level store rather than per-component state: the same film can sit in the
 * carousel, the recommendations and the grid at once, and pressing one heart has to fill
 * all three.
 */
let ids = new Set<string>();
let loaded = false;
let loading: Promise<void> | null = null;
const listeners = new Set<(ids: Set<string>) => void>();

function announce() {
  const snapshot = new Set(ids);
  for (const listener of listeners) listener(snapshot);
}

export async function loadWatchlist(force = false): Promise<void> {
  if (!auth.isSignedIn) {
    ids = new Set();
    loaded = false;
    announce();
    return;
  }
  if (loaded && !force) return;
  if (loading) return loading;

  loading = get<string[]>("/api/watchlist/ids")
    .then((list) => {
      ids = new Set(list);
      loaded = true;
      announce();
    })
    .catch(() => undefined)
    .finally(() => {
      loading = null;
    });
  return loading;
}

/** Optimistic: the heart fills at once and un-fills if the server says no. */
export async function toggleWatchlist(movieId: string): Promise<boolean> {
  const adding = !ids.has(movieId);
  if (adding) ids.add(movieId); else ids.delete(movieId);
  announce();

  try {
    if (adding) await put(`/api/watchlist/${movieId}`);
    else await del(`/api/watchlist/${movieId}`);
    return adding;
  } catch (err) {
    if (adding) ids.delete(movieId); else ids.add(movieId);
    announce();
    throw err;
  }
}

export function useWatchlist() {
  const [snapshot, setSnapshot] = useState<Set<string>>(() => new Set(ids));

  useEffect(() => {
    listeners.add(setSnapshot);
    void loadWatchlist();
    // Signing in or out on another island changes whose list this is.
    const unsubscribe = auth.subscribe(() => void loadWatchlist(true));
    return () => {
      listeners.delete(setSnapshot);
      unsubscribe();
    };
  }, []);

  return {
    ids: snapshot,
    has: (movieId: string) => snapshot.has(movieId),
    toggle: toggleWatchlist,
  };
}
