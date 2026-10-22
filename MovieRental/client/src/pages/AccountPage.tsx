import { useState } from "react";
import { Section, Panel, Notice } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Input, Field } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { useAuth } from "@/components/useAuth";
import { auth, post, ApiError, type AuthResponse, type RegistrationResponse } from "@/lib/api";
import { t } from "@/lib/i18n";

type Mode = "signin" | "register" | "verify";

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
      setMessage({ tone: "error", text: err.message });
      return err;
    }
    setMessage({ tone: "error", text: fallback });
    return null;
  }

  async function signIn() {
    setBusy(true); setMessage(null); setFieldErrors({});
    try {
      auth.apply(await post<AuthResponse>("/api/auth/login", { email, password }));
      // A full reload, not a client-side redirect: the session cookie has just been set and
      // the Razor shell needs to re-render its nav with the new role.
      window.location.href = "/";
    } catch (err) {
      const api = fail(err, "Could not sign in.");
      if (api?.code === "email_unconfirmed") {
        setMode("verify");
        setMessage({ tone: "info", text: t("account.verifyLede") });
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
      fail(err, "Could not create the account.");
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
      setMessage({ tone: "ok", text: "E-mail confirmed. You can sign in now." });
    } catch (err) {
      fail(err, "That code was not accepted.");
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
      fail(err, "The code could not be sent.");
    } finally {
      setBusy(false);
    }
  }

  if (isSignedIn && user) {
    return (
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

          {user.phoneNumber && !user.isPhoneConfirmed ? (
            <div className="mt-5 space-y-3">
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
                      setMessage({ tone: "ok", text: "Phone confirmed." });
                    } catch (err) { fail(err, "That code was not accepted."); }
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
              window.location.href = "/";
            }}
          >
            Sign out
          </Button>
        </Panel>
      </Section>
    );
  }

  return (
    <Section title={mode === "register" ? t("account.register") : mode === "verify" ? t("account.verify") : t("account.signIn")}
             lede={mode === "verify" ? t("account.verifyLede") : undefined}>
      <Panel className="max-w-md">
        {message ? <div className="mb-4"><Notice tone={message.tone === "info" ? "info" : message.tone}>{message.text}</Notice></div> : null}

        {mode === "verify" ? (
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
          </div>
        )}
      </Panel>
    </Section>
  );
}
