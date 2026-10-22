// The staff screens' addresses, worked out the way the site does (StaffPaths in
// src/MovieRental.Host/Infrastructure/StaffGate.cs): "/wy-" and 40 hex characters of
// HMAC-SHA256(Staff:PathKey, "watchingyou.staff." + screen). The key comes from the host's user
// secrets (or STAFF_KEY); it is never written down here.
import { createHmac } from "node:crypto";
import { execFileSync } from "node:child_process";

const key = process.env.STAFF_KEY ?? execFileSync("dotnet",
  ["user-secrets", "list", "--project", new URL("../src/MovieRental.Host", import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1")],
  { encoding: "utf8" }).match(/Staff:PathKey = (\S+)/)?.[1];
if (!key) throw new Error("No Staff:PathKey in the host's user secrets (or STAFF_KEY).");

/** staffPath("admin" | "security" | "backoffice" | "unlock") */
export const staffPath = (screen) => "/wy-" + createHmac("sha256", key).update("watchingyou.staff." + screen).digest("hex").slice(0, 40);
