import { useCallback, useEffect, useState } from "react";
import { ShieldOff, ShieldCheck } from "lucide-react";
import { Panel, Notice, Empty, Spinner } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { SearchField } from "@/components/SearchField";
import { Badge } from "@/components/ui/badge";
import { get, put, query, ApiError } from "@/lib/api";
import { t } from "@/lib/i18n";
import { formatDate } from "@/lib/format";

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
}

const GRANTABLE = ["Admin", "Security", "Cashier"] as const;

/**
 * Roles existed from the start but could only be handed out by the seeder, so a second
 * Security reviewer meant editing code and rebuilding the database. This is the half of the
 * role system that was missing.
 *
 * Customer is not listed: everyone keeps it, because it is what grants renting and the
 * Studio, and the server appends it regardless of what is sent.
 */
export function UserAdmin() {
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

  function toggleSuspension(user: AdminUser) {
    const suspending = !user.isSuspended;
    const reason = suspending ? window.prompt(t("admin.suspendReason")) : null;
    if (suspending && reason === null) return;      // cancelled the prompt

    return run(
      user.id,
      () => put(`/api/admin/users/${user.id}/suspension`, { suspended: suspending, reason }),
      suspending ? `${user.email} — ${t("admin.suspended")}` : `${user.email} — ${t("admin.unsuspend")}`,
    );
  }

  return (
    <>
      <div className="mb-4 max-w-sm">
        <SearchField value={search} onChange={setSearch} />
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
                {user.roles.map((role) => (
                  <Badge key={role} tone={role === "Admin" ? "good" : role === "Security" ? "warn" : undefined}>
                    {role}
                  </Badge>
                ))}
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

            <div className="flex flex-wrap gap-2">
              {GRANTABLE.map((role) => (
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
            </div>
          </Panel>
        ))}
      </div>
    </>
  );
}
