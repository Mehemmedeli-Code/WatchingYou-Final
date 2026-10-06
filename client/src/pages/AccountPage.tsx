import { useState } from "react";
import { Section, Panel, Notice } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Input, Field } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { useAuth } from "@/components/useAuth";
import { MessagesInbox } from "@/components/MessagesInbox";
import { LoyaltyPanel } from "@/components/LoyaltyPanel";
import { auth, post, put, ApiError, type AuthResponse, type RegistrationResponse, type UserProfile } from "@/lib/api";
import { t } from "@/lib/i18n";
import { afterSignIn, afterSignOut, openServerPage } from "@/lib/platform";

type Mode = "signin" | "register" | "verify" | "forgot" | "reset";

export default function AccountPage() {
  const { user, isSignedIn, signOut } = useAuth();
  const [mode, setMode] = useState<Mode>("signin");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [fullName, setFullName] = useState("");
  const [phone, setPhone] = useState("");
  const [code, setCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ tone: "ok" | "error" | "info"; text: string } | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});

  function fail(err: unknown, fallback: string) {
    if (err instanceof ApiError) {
      setFieldErrors(err.fieldErrors ?? {});
      // A failed sign-in is a bare 401 on purpose — the server will not say which half of the
      // pair was wrong — so there is no message in it, and "Request failed (401)" is what
      // people used to see. Say the plain thing instead, and a network failure likewise.
      const text = err.status === 401 && !err.code
        ? t("account.badLogin", "E-mail or password is incorrect.")
        : err.status === 0 || err.status >= 500 ? fallback : err.message;
      setMessage({ tone: "error", text });
      return err;
    }
    setMessage({ tone: "error", text: fallback });
    return null;
  }

  async function signIn() {
    setBusy(true); setMessage(null); setFieldErrors({});
    try {
      const session = await post<AuthResponse>("/api/auth/login", { email, password });
      auth.apply(session);

      // A full reload, not a client-side redirect: the session cookie has just been set and
      // the Razor shell needs to re-render its nav with the new role. Only same-site paths
      // are followed — an open redirect here would be handed out by every sign-in link.
      // ASP.NET's cookie challenge spells it "ReturnUrl"; the site's own links, "returnUrl".
      const search = new URLSearchParams(window.location.search);
      const requested = search.get("returnUrl") ?? search.get("ReturnUrl");
      const safe = requested && requested.startsWith("/") && !requested.startsWith("//") ? requested : null;

      // Staff accounts land in the back office, not the public catalogue: the manager and the
      // cashier sign in to work, and the site is one click away from there if they want it.
      const roles = session.user.roles;
      const isStaff = roles.includes("Admin") || roles.includes("Cashier");
      afterSignIn(safe ?? (isStaff ? "/backoffice" : "/"));
    } catch (err) {
      const api = fail(err, t("account.signInFailed", "Could not sign in. Check your connection and try again."));
      if (api?.code === "email_unconfirmed") {
        setMode("verify");
        setMessage({ tone: "info", text: t("account.verifyLede") });
      }
      if (api?.code === "account_suspended") {
        setMessage({ tone: "error", text: api.message || t("error.suspended") });
      }
    } finally {
      setBusy(false);
    }
  }

  async function register() {
    setBusy(true); setMessage(null); setFieldErrors({});
    try {
      const result = await post<RegistrationResponse>("/api/auth/register",
        { fullName, email, password, phoneNumber: phone || null });
      setMode("verify");
      setMessage({ tone: result.verificationSent ? "ok" : "error", text: result.message });
    } catch (err) {
      fail(err, t("account.registerFailed", "Could not create the account."));
    } finally {
      setBusy(false);
    }
  }

  async function confirm() {
    setBusy(true); setMessage(null);
    try {
      await post("/api/auth/verification/confirm", { email, channel: "Email", code });
      setMode("signin");
      setCode("");
      setMessage({ tone: "ok", text: t("account.emailConfirmed", "E-mail confirmed. You can sign in now.") });
    } catch (err) {
      fail(err, t("account.codeRejected", "That code was not accepted."));
    } finally {
      setBusy(false);
    }
  }

  async function requestReset() {
    setBusy(true); setMessage(null);
    try {
      await post("/api/auth/password/forgot", { email });
      setMode("reset");
      // Deliberately vague: a definite "no such account" would turn this form into a way
      // of discovering who has one.
      setMessage({ tone: "ok", text: t("account.resetSent") });
    } catch (err) {
      fail(err, t("account.codeNotSent", "The code could not be sent."));
    } finally {
      setBusy(false);
    }
  }

  async function resetPassword() {
    setBusy(true); setMessage(null); setFieldErrors({});
    try {
      await post("/api/auth/password/reset", { email, code, newPassword: password });
      setMode("signin");
      setCode(""); setPassword("");
      setMessage({ tone: "ok", text: t("account.resetDone") });
    } catch (err) {
      fail(err, t("account.codeRejected", "That code was not accepted."));
    } finally {
      setBusy(false);
    }
  }

  async function resend(channel: "Email" | "Sms") {
    setBusy(true); setMessage(null);
    try {
      await post("/api/auth/verification/send", { email: email || user?.email, channel });
      setMessage({ tone: "ok", text: t("account.verifyLede") });
    } catch (err) {
      fail(err, t("account.codeNotSent", "The code could not be sent."));
    } finally {
      setBusy(false);
    }
  }

  if (isSignedIn && user) {
    return (
      <>
      <Section title={t("nav.account")}>
        <Panel className="max-w-xl">
          <p className="font-display text-2xl text-ink">{user.fullName}</p>
          <p className="mt-1 text-sm text-ink-mute">{user.email}</p>

          <div className="mt-3 flex flex-wrap gap-1.5">
            {user.roles.map((role) => <Badge key={role}>{role}</Badge>)}
            <Badge tone={user.isEmailConfirmed ? "good" : "warn"}>
              {t("account.email")}: {user.isEmailConfirmed ? "✓" : "—"}
            </Badge>
            {user.phoneNumber ? (
              <Badge tone={user.isPhoneConfirmed ? "good" : "warn"}>
                {t("account.phone")}: {user.isPhoneConfirmed ? "✓" : "—"}
              </Badge>
            ) : null}
          </div>

          {message ? <div className="mt-4"><Notice tone={message.tone === "info" ? "info" : message.tone}>{message.text}</Notice></div> : null}

          {/* Without this, anyone who skipped the optional phone field at registration had no
              way to add one later — and so no way to ever receive an SMS code. */}
          <div className="mt-6 border-t border-line pt-5">
            <p className="font-display text-lg text-ink">{t("account.profile")}</p>
            <div className="mt-3 space-y-3">
              <Field label={t("account.fullName")}>
                <Input value={fullName || user.fullName} onChange={(e) => setFullName(e.target.value)} />
              </Field>
              <Field label={t("account.phone")} hint={t("account.phoneHint")}>
                <Input
                  value={phone || user.phoneNumber || ""}
                  onChange={(e) => setPhone(e.target.value)}
                  placeholder="+994..."
                />
              </Field>
              <Button
                size="sm"
                variant="outline"
                disabled={busy}
                onClick={async () => {
                  setBusy(true); setMessage(null);
                  try {
                    const updated = await put<UserProfile>("/api/auth/profile", {
                      fullName: fullName || user.fullName,
                      phoneNumber: (phone || user.phoneNumber) ?? null,
                    });
                    auth.patchUser(updated);
                    setMessage({ tone: "ok", text: t("account.saved") });
                  } catch (err) { fail(err, t("account.saveFailed", "Could not save.")); }
                  finally { setBusy(false); }
                }}
              >
                {t("account.saveProfile")}
              </Button>
            </div>
          </div>

          {user.phoneNumber && !user.isPhoneConfirmed ? (
            <div className="mt-5 space-y-3">
              <p className="text-xs text-ink-mute">{t("account.smsConsole")}</p>
              <Button size="sm" variant="outline" disabled={busy} onClick={() => resend("Sms")}>
                {t("account.resend")} (SMS)
              </Button>
              <div className="flex gap-2">
                <Input value={code} onChange={(e) => setCode(e.target.value)} placeholder={t("account.code")} />
                <Button
                  size="sm"
                  disabled={busy || !code}
                  onClick={async () => {
                    setBusy(true);
                    try {
                      await post("/api/auth/verification/confirm", { email: user.email, channel: "Sms", code });
                      setMessage({ tone: "ok", text: t("account.phoneConfirmed", "Phone confirmed.") });
                    } catch (err) { fail(err, t("account.codeRejected", "That code was not accepted.")); }
                    finally { setBusy(false); setCode(""); }
                  }}
                >
                  {t("common.send")}
                </Button>
              </div>
            </div>
          ) : null}

          <Button
            className="mt-6"
            variant="outline"
            onClick={async () => {
              await post("/api/auth/logout", { refreshToken: localStorage.getItem("rr.refresh") }).catch(() => null);
              signOut();
              afterSignOut();
            }}
          >
            {t("account.signOut", "Sign out")}
          </Button>
        </Panel>
      </Section>
      <LoyaltyPanel />
      <MessagesInbox />
      {/* The watchlist moved to its own page, /favourites, linked from the header. */}
      <DeleteAccountPanel isAdmin={user.roles.includes("Admin")} />
      <div className="mx-auto w-full max-w-6xl px-4 pb-12 sm:px-6"><PrivacyLink /></div>
      </>
    );
  }

  return (
    <Section
      title={
        mode === "register" ? t("account.register")
        : mode === "verify" ? t("account.verify")
        : mode === "forgot" || mode === "reset" ? t("account.resetTitle")
        : t("account.signIn")
      }
      lede={
        mode === "verify" ? t("account.verifyLede")
        : mode === "forgot" ? t("account.resetLede")
        : undefined
      }
    >
      <Panel className="max-w-md">
        {message ? <div className="mb-4"><Notice tone={message.tone === "info" ? "info" : message.tone}>{message.text}</Notice></div> : null}

        {mode === "forgot" || mode === "reset" ? (
          <div className="space-y-4">
            <Field label={t("account.email")}>
              <Input value={email} onChange={(e) => setEmail(e.target.value)} type="email" autoComplete="email" />
            </Field>

            {mode === "reset" ? (
              <>
                <Field label={t("account.code")}>
                  <Input
                    value={code}
                    onChange={(e) => setCode(e.target.value.replace(/\D/g, "").slice(0, 6))}
                    inputMode="numeric"
                    autoComplete="one-time-code"
                    placeholder="000000"
                  />
                </Field>
                <Field label={t("account.newPassword")} hint={fieldErrors.NewPassword?.[0]}>
                  <Input
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    type="password"
                    autoComplete="new-password"
                  />
                </Field>
              </>
            ) : null}

            <Button
              className="w-full"
              disabled={busy || !email || (mode === "reset" && (code.length !== 6 || password.length < 8))}
              onClick={mode === "reset" ? resetPassword : requestReset}
            >
              {busy ? t("common.loading") : mode === "reset" ? t("account.setPassword") : t("account.sendCode")}
            </Button>

            <div className="flex justify-between text-sm">
              {mode === "reset" ? (
                <button className="text-accent hover:underline" onClick={requestReset} disabled={busy}>
                  {t("account.resend")}
                </button>
              ) : <span />}
              <button className="text-ink-mute hover:underline" onClick={() => { setMode("signin"); setMessage(null); }}>
                {t("account.signIn")}
              </button>
            </div>
          </div>
        ) : mode === "verify" ? (
          <div className="space-y-4">
            <Field label={t("account.email")}>
              <Input value={email} onChange={(e) => setEmail(e.target.value)} type="email" />
            </Field>
            <Field label={t("account.code")}>
              <Input
                value={code}
                onChange={(e) => setCode(e.target.value.replace(/\D/g, "").slice(0, 6))}
                inputMode="numeric"
                autoComplete="one-time-code"
                placeholder="000000"
              />
            </Field>

            <Button className="w-full" disabled={busy || code.length !== 6} onClick={confirm}>
              {busy ? t("common.loading") : t("account.verify")}
            </Button>

            <div className="flex justify-between text-sm">
              <button className="text-accent hover:underline" onClick={() => resend("Email")} disabled={busy}>
                {t("account.resend")}
              </button>
              <button className="text-ink-mute hover:underline" onClick={() => setMode("signin")}>
                {t("account.signIn")}
              </button>
            </div>
          </div>
        ) : (
          <div className="space-y-4">
            {mode === "register" ? (
              <Field label={t("account.fullName")} hint={fieldErrors.FullName?.[0]}>
                <Input value={fullName} onChange={(e) => setFullName(e.target.value)} />
              </Field>
            ) : null}

            <Field label={t("account.email")} hint={fieldErrors.Email?.[0]}>
              <Input value={email} onChange={(e) => setEmail(e.target.value)} type="email" autoComplete="email" />
            </Field>

            <Field label={t("account.password")} hint={fieldErrors.Password?.[0]}>
              <Input
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                type="password"
                autoComplete={mode === "register" ? "new-password" : "current-password"}
              />
            </Field>

            {mode === "register" ? (
              <Field label={t("account.phone")} hint={fieldErrors.PhoneNumber?.[0]}>
                <Input value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="+994..." />
              </Field>
            ) : null}

            <Button className="w-full" disabled={busy} onClick={mode === "register" ? register : signIn}>
              {busy ? t("common.loading") : mode === "register" ? t("account.register") : t("account.signIn")}
            </Button>

            <button
              className="w-full text-sm text-ink-mute hover:underline"
              onClick={() => { setMode(mode === "register" ? "signin" : "register"); setMessage(null); setFieldErrors({}); }}
            >
              {mode === "register" ? t("account.signIn") : t("account.register")}
            </button>

            {mode === "signin" ? (
              <button
                className="w-full text-sm text-accent hover:underline"
                onClick={() => { setMode("forgot"); setMessage(null); setFieldErrors({}); }}
              >
                {t("account.forgot")}
              </button>
            ) : null}
          </div>
        )}
      </Panel>
      <PrivacyLink />
    </Section>
  );
}

/**
 * Closing the account. Both app stores require it of any app with sign-up, and it belongs on
 * the website for the same reason. Two deliberate steps — open the panel, then type the
 * password — because it cannot be undone. Admin accounts are closed by another admin instead,
 * so the panel only explains that to them.
 */
function DeleteAccountPanel({ isAdmin }: { isAdmin: boolean }) {
  const [open, setOpen] = useState(false);
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function remove() {
    setBusy(true); setError(null);
    try {
      await post("/api/auth/account/delete", { password });
      auth.clear();
      afterSignOut();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("account.deleteFailed", "The account could not be deleted."));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Section title={t("account.deleteTitle", "Delete account")}>
      <Panel className="max-w-xl border-bad-bg">
        {isAdmin ? (
          <p className="text-sm text-ink-mute">{t("account.deleteAdmin", "Administrator accounts are closed by another administrator on the admin page.")}</p>
        ) : !open ? (
          <div className="space-y-3">
            <p className="text-sm text-ink-mute">
              {t("account.deleteLede", "Your name, e-mail, phone and profile are erased and you are signed out everywhere. Tickets you bought stop showing in the app. This cannot be undone.")}
            </p>
            <Button variant="danger" onClick={() => setOpen(true)}>{t("account.deleteStart", "Delete my account")}</Button>
          </div>
        ) : (
          <div className="space-y-3">
            <Field label={t("account.deleteConfirm", "Type your password to confirm")}>
              <Input type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} />
            </Field>
            {error ? <Notice tone="error">{error}</Notice> : null}
            <div className="flex flex-wrap gap-2">
              <Button variant="danger" disabled={busy || !password} onClick={() => void remove()}>
                {t("account.deleteFinal", "Delete permanently")}
              </Button>
              <Button variant="ghost" onClick={() => { setOpen(false); setPassword(""); setError(null); }}>
                {t("common.cancel", "Cancel")}
              </Button>
            </div>
          </div>
        )}
      </Panel>
    </Section>
  );
}

/** The privacy policy, one tap from sign-up and from the account — where both stores expect it. */
function PrivacyLink() {
  return (
    <button type="button" onClick={() => openServerPage("/privacy")}
      className="mt-4 text-sm text-ink-mute underline-offset-2 hover:text-accent hover:underline">
      {t("privacy.title", "Privacy policy")}
    </button>
  );
}
