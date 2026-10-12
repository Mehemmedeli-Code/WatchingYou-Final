import { useEffect, useState } from "react";
import { auth, type UserProfile } from "@/lib/api";

/** Subscribes a component to the shared auth store in lib/api. */
export function useAuth() {
  const [user, setUser] = useState<UserProfile | null>(auth.user);
  useEffect(() => auth.subscribe(setUser) as unknown as () => void, []);

  const roles = user?.roles ?? [];
  const isAdmin = roles.includes("Admin");

  return {
    user,
    isSignedIn: user !== null,
    isAdmin,
    // Admin can stand at the security desk; the reverse is deliberately not true.
    isSecurity: isAdmin || roles.includes("Security"),
    // Help requests and reported messages only; the rest of the security desk is not theirs.
    isModerator: roles.includes("Moderator"),
    isOwner: roles.includes("SuperAdmin"),
    signOut: () => auth.clear(),
  };
}
