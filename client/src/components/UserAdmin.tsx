import { useCallback, useEffect, useState } from "react";
import { Crown, KeyRound, ShieldOff, ShieldCheck } from "lucide-react";
import { Panel, Notice, Empty, Spinner } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { SearchField } from "@/components/SearchField";
import { Badge } from "@/components/ui/badge";
import { get, put, query, ApiError } from "@/lib/api";
import { t } from "@/lib/i18n";
import { formatDate } from "@/lib/format";
import { askText } from "@/lib/dialog";
import { useAuth } from "@/components/useAuth";

interface AdminUser {
  id: string;
  fullName: string;
  email: string;
  phoneNumber?: string | null;
  roles: string[];
  isEmailConfirmed: boolean;
  isPhoneConfirmed: boolean;
  isSuspended: boolean;
  suspensionReason?: string | null;
  createdAtUtc: string;
  lastLoginAtUtc?: string | null;
  hasStaffPin: boolean;
}

const GRANTABLE = ["Admin", "Security", "Cashier", "Moderator"] as const;
const STAFF = ["SuperAdmin", "Admin", "Security", "Cashier", "Moderator"];

/**
 * Roles existed from the start but could only be handed out by the seeder, so a second
 * Security reviewer meant editing code and rebuilding the database. This is the half of the
 * role system that was missing.
 *
 * Customer is not listed: everyone keeps it, because it is what grants renting and the
 * Studio, and the server appends it regardless of what is sent.
 */
export function UserAdmin() {
  // Only the owner makes admins and sets PINs; the server refuses anyone else either way.
  const { isOwner } = useAuth();
  const [users, setUsers] = useState<AdminUser[] | null>(null);
  const [search, setSearch] = useState("");
  const [message, setMessage] = useState<{ tone: "ok" | "error"; text: string } | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const load = useCallback(async () => {
    setUsers(await get<AdminUser[]>("/api/admin/users" + query({ search })).catch(() => []));
  }, [search]);

  useEffect(() => {
    const timer = setTimeout(() => void load(), 250);
    return () => clearTimeout(timer);
  }, [load]);

  async function run(id: string, action: () => Promise<unknown>, ok: string) {
    setBusyId(id);
    setMessage(null);
    try {
      await action();
      setMessage({ tone: "ok", text: ok });
      await load();
    } catch (err) {
      setMessage({ tone: "error", text: err instanceof ApiError ? err.message : t("error.action") });
    } finally {
      setBusyId(null);
    }
  }

  function toggleRole(user: AdminUser, role: string) {
    const next = user.roles.includes(role)
      ? user.roles.filter((r) => r !== role)
      : [...user.roles, role];

    return run(user.id, () => put(`/api/admin/users/${user.id}/roles`, { roles: next }), `${user.email} → ${next.join(", ")}`);
  }

  async function toggleSuspension(user: AdminUser) {
    const suspending = !user.isSuspended;
    const reason = suspending ? await askText(t("admin.suspendReason")) : null;
    if (suspending && reason === null) return;      // cancelled the prompt

    return run(
      user.id,
      () => put(`/api/admin/users/${user.id}/suspension`, { suspended: suspending, reason }),
      suspending ? `${user.email} — ${t("admin.suspended")}` : `${user.email} — ${t("admin.unsuspend")}`,
    );
  }

  async function setPin(user: AdminUser) {
    const pin = await askText(t("admin.pinPrompt", "New PIN (4–12 digits). Leave empty to remove the PIN."));
    if (pin === null) return;      // cancelled the prompt
    return run(user.id, () => put(`/api/admin/users/${user.id}/staff-pin`, { pin: pin.trim() || null }), `${user.email} — ${t("admin.pinSet", "PIN updated.")}`);
  }

  return (
    <>
      <div className="mb-4 max-w-sm">
        <SearchField value={search} onChange={setSearch} />
      {!search.trim() ? <p className="text-xs text-ink-mute">{t("admin.staffOnly", "Showing staff only. Search by e-mail or name to find a customer and give them a role.")}</p> : null}
      </div>

      {message ? <div className="mb-4"><Notice tone={message.tone}>{message.text}</Notice></div> : null}
      {users === null ? <Spinner label={t("common.loading")} /> : null}
      {users?.length === 0 ? <Empty title={t("gallery.empty")} hint="" /> : null}

      <div className="space-y-3">
        {users?.map((user) => (
          <Panel key={user.id} className="flex flex-wrap items-start justify-between gap-4">
            <div>
              <p className="font-display text-lg text-ink">{user.fullName}</p>
              <p className="text-xs text-ink-mute">{user.email}</p>

              <div className="mt-2 flex flex-wrap gap-1.5">
                {user.roles.map((role) => role === "SuperAdmin" ? (
                  <Badge key={role} tone="good"><Crown size={12} aria-hidden /> {t("admin.owner", "Owner")}</Badge>
                ) : (
                  <Badge key={role} tone={role === "Admin" ? "good" : role === "Security" ? "warn" : undefined}>
                    {role}
                  </Badge>
                ))}
                {user.roles.some((r) => STAFF.includes(r)) && !user.hasStaffPin ? <Badge tone="warn">{t("admin.noPinYet", "No PIN")}</Badge> : null}
                {user.isSuspended ? <Badge tone="bad">{t("admin.suspended")}</Badge> : null}
                {!user.isEmailConfirmed ? <Badge tone="warn">{t("account.email")}: —</Badge> : null}
              </div>

              {user.suspensionReason ? (
                <p className="mt-2 text-xs text-bad">{user.suspensionReason}</p>
              ) : null}

              <p className="mt-2 text-xs text-ink-mute">
                {user.lastLoginAtUtc
                  ? `${formatDate(user.lastLoginAtUtc)}`
                  : t("admin.neverSignedIn")}
              </p>
            </div>

            {/* The owner's account is not managed from here: no roles, no suspension. */}
            {user.roles.includes("SuperAdmin") ? null : (
            <div className="flex flex-wrap gap-2">
              {GRANTABLE.filter((role) => role !== "Admin" || isOwner).map((role) => (
                <Button
                  key={role}
                  size="sm"
                  variant={user.roles.includes(role) ? "solid" : "outline"}
                  disabled={busyId === user.id}
                  onClick={() => toggleRole(user, role)}
                >
                  {role}
                </Button>
              ))}

              <Button
                size="sm"
                variant={user.isSuspended ? "outline" : "danger"}
                disabled={busyId === user.id}
                onClick={() => toggleSuspension(user)}
              >
                {user.isSuspended ? <ShieldCheck size={14} aria-hidden /> : <ShieldOff size={14} aria-hidden />}
                {user.isSuspended ? t("admin.unsuspend") : t("admin.suspend")}
              </Button>

              {isOwner && user.roles.some((r) => STAFF.includes(r)) ? (
                <Button size="sm" variant="outline" disabled={busyId === user.id} onClick={() => setPin(user)}>
                  <KeyRound size={14} aria-hidden /> {t("admin.setPin", "Set PIN")}
                </Button>
              ) : null}
            </div>
            )}
          </Panel>
        ))}
      </div>
    </>
  );
}
