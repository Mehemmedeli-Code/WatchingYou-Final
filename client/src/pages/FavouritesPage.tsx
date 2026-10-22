import { Heart } from "lucide-react";
import { Section, Empty } from "@/components/Shell";
import { useAuth } from "@/components/useAuth";
import { WatchlistShelf } from "@/components/WatchlistShelf";
import { t } from "@/lib/i18n";

/**
 * Favourites — the watchlist on a page of its own, reached from the header. It used to sit at
 * the bottom of the account page, under the profile form and the inbox, where nobody went
 * looking for films.
 */
export default function FavouritesPage() {
  const { isSignedIn } = useAuth();

  // The Razor page already sends anonymous visitors to sign in; this covers a session that
  // ended while the page was open.
  if (!isSignedIn) {
    return (
      <Section title={t("nav.favourites", "Favourites")}>
        <Empty
          title={t("watchlist.emptyTitle", "Nothing saved yet")}
          hint={t("favourites.signIn", "Sign in to see the films you saved.")}
          action={
            <a href="/account?returnUrl=%2Ffavourites"
              className="inline-flex h-10 items-center gap-2 rounded-full bg-accent px-5 text-sm font-medium text-surface hover:bg-accent-bright">
              <Heart size={16} />{t("nav.signIn", "Sign in")}
            </a>
          }
        />
      </Section>
    );
  }

  return <WatchlistShelf />;
}
