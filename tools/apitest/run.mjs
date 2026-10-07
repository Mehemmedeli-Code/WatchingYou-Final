// Runs the requested parts in order and writes results.json. Usage: node run.mjs 1 2 3 ...
import { writeFileSync } from "node:fs";
import { results } from "./lib.mjs";

const which = process.argv.slice(2).length ? process.argv.slice(2) : ["1", "2", "3", "4"];
const ctx = {};
for (const n of which) {
  const mod = await import(`./part${n}.mjs`);
  try { await mod[`part${n}`](ctx); }
  catch (err) { results.push({ section: `part${n}`, name: "CRASHED: " + err.message, method: "", path: "", status: "", expect: "", ok: false, detail: err.stack?.split("\n")[1] ?? "" }); console.error(err); }
}
writeFileSync("C:/wy/apitest/ctx.json", JSON.stringify(ctx, null, 2));
writeFileSync("C:/wy/apitest/results.json", JSON.stringify(results, null, 2));
const failed = results.filter((r) => !r.ok);
console.log(`\n${results.length} checks, ${results.length - failed.length} passed, ${failed.length} failed`);
for (const f of failed) console.log(`  FAIL [${f.section}] ${f.method} ${f.path} — ${f.name} (got ${f.status}, want ${f.expect}) ${f.detail}`);
