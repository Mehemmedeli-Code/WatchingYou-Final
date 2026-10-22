// The test logins' passwords and staff PIN, drawn at random once per machine by the site
// (src/MovieRental.Host/Infrastructure/TestLogins.cs) and kept outside the repository, which is
// public. The checks here sign in to their throwaway copies of the site with them.
import { readFileSync } from "node:fs";
import path from "node:path";

export const file = path.join(process.env.LOCALAPPDATA ?? "", "WatchingYou", "test-logins.json");

let read;
try { read = JSON.parse(readFileSync(file, "utf8").replace(/^﻿/, "")); }
catch { throw new Error(`No ${file}: start a test copy of the site once (tools/api-check.ps1 does) and it is made.`); }

/** { admin, security, customer, kassa, demo, staffPin } */
export const logins = read;
