// Finds text that would not change with the language switch. Run from the repo root:
//   node tools/i18n-audit.mjs
//
// Reports, per file and line:
//   key     t("key") with no entry in one of the four locale files (the English fallback shows)
//   text    words written straight into JSX, or into placeholder / title / aria-label / alt
//   string  a sentence in a string literal that ends up on screen (setError("..."), {"..."})
//   date    toLocale*String() or Intl without the page language (the browser's language shows)
//   native  <input type="file"> and friends, whose buttons the browser labels in its own language
//   server  a message the API sends that lib/serverMessages.ts cannot translate
//   razor   words written straight into a .cshtml page
// Exits 1 when anything is found, so it can sit in CI next to the type check.
import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative } from "node:path";
import { createRequire } from "node:module";
import { fileURLToPath, pathToFileURL } from "node:url";

const root = fileURLToPath(new URL("..", import.meta.url));
const require = createRequire(join(root, "client", "package.json"));
const { parseAst } = await import(pathToFileURL(require.resolve("rolldown/parseAst")).href);

const langs = ["az", "en", "ru", "tr"];
const locales = Object.fromEntries(langs.map((l) => [l, JSON.parse(readFileSync(join(root, "src/MovieRental.Host/locales", `${l}.json`), "utf8"))]));
const findings = [];
const report = (kind, file, line, detail) => findings.push({ kind, file: relative(root, file).replaceAll("\\", "/"), line, detail });

function walk(dir, exts, out = []) {
  for (const name of readdirSync(dir)) {
    const full = join(dir, name);
    if (["node_modules", "bin", "obj", "wwwroot", "dist", "dist-mobile", "Migrations"].includes(name)) continue;
    if (statSync(full).isDirectory()) walk(full, exts, out);
    else if (exts.some((e) => name.endsWith(e))) out.push(full);
  }
  return out;
}

// A run of at least two letters, in any of the four alphabets.
const WORDS = /\p{L}{2,}/u;
const SENTENCE = /\p{L}{2,}.*\s.*\p{L}{2,}/u;
// Things that are words to a regex but not to a reader: class lists, URLs, ids, units, brand.
const IGNORE = /^(?:← Watching PRO|WATCHINGYOU|[\s·•|/–—:,.()+\-#%*×→←↑↓✓…"'!?]*|WatchingYou|Watching PRO|PRO|AZN|USD|QR|PDF|CSV|API|AI|OK|ID|URL|IMAX|VIP|Stripe|Higgsfield|https?:\S+|\/\S*|[\w.-]+@[\w.-]+|\{[^}]*\}|[A-Z]{1,4}\d*|[A-Z]+\d+)$/u;
const userFacing = (s) => WORDS.test(s) && !/^[·\s(]*(?:[A-Z]{2}\s?\*?|SMS\)|Aa|Watching PRO ·)$/.test(s.trim()) && !/[{};]\s*$|@keyframes/.test(s) && !IGNORE.test(s.trim()) && !/^[a-z0-9_-]+(\s[a-z0-9_:\/\[\]().-]+)+$/.test(s.trim()) /* class lists */;

const offsets = (src) => { const lines = [0]; for (let i = 0; i < src.length; i++) if (src[i] === "\n") lines.push(i + 1); return lines; };
const lineOf = (lines, pos) => { let lo = 0, hi = lines.length - 1; while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (lines[mid] <= pos) lo = mid; else hi = mid - 1; } return lo + 1; };

const TEXT_ATTRS = new Set(["placeholder", "title", "aria-label", "alt", "label", "aria-description"]);
const SHOWN_CALLS = new Set(["setError", "setMessage", "setNotice", "setStatus", "alert", "confirm", "onDone", "toast", "setInfo", "setWarning"]);
const NATIVE = new Set(["file", "date", "time", "datetime-local", "month", "week"]);

// ------------------------------------------------------------------ client
for (const file of walk(join(root, "client/src"), [".tsx", ".ts"])) {
  if (file.endsWith(".d.ts") || file.includes("serverMessages.ts")) continue;
  const src = readFileSync(file, "utf8");
  let ast;
  try { ast = parseAst(src, { lang: file.endsWith(".tsx") ? "tsx" : "ts" }); }
  catch (e) { report("parse", file, 0, e.message.split("\n")[0]); continue; }
  const lines = offsets(src);
  const at = (node) => lineOf(lines, node.start);

  const visit = (node, parent, insideT) => {
    if (!node || typeof node.type !== "string") return;
    switch (node.type) {
      case "CallExpression": {
        const callee = node.callee.type === "Identifier" ? node.callee.name : node.callee.property?.name;
        if (callee === "t" && (node.arguments[0]?.type !== "Literal" || typeof node.arguments[0].value === "string")) {
          if (node.arguments[0]?.type !== "Literal") { for (const arg of node.arguments) visit(arg, node, true); return; }
          const key = node.arguments[0].value;
          const missing = langs.filter((l) => !(key in locales[l]));
          // A key with no fallback and no entry anywhere prints the key itself.
          if (missing.length) report("key", file, at(node), `${key} — missing in ${missing.join(", ")}`);
          for (const arg of node.arguments) visit(arg, node, true);
          return;
        }
        if (SHOWN_CALLS.has(callee)) {
          for (const arg of node.arguments) {
            if (arg.type === "Literal" && typeof arg.value === "string" && userFacing(arg.value)) report("string", file, at(arg), `${callee}(${JSON.stringify(arg.value)})`);
            if (arg.type === "ObjectExpression")
              for (const p of arg.properties) if (p.key?.name === "text" && p.value?.type === "Literal" && userFacing(String(p.value.value))) report("string", file, at(p), JSON.stringify(p.value.value));
          }
        }
        // window.confirm / prompt / alert: a grey system box in the browser's language.
        if (node.callee.type === "MemberExpression" && node.callee.object?.name === "window" && ["confirm", "prompt", "alert"].includes(node.callee.property?.name))
          report("native", file, at(node), `window.${node.callee.property.name}() is the browser's own dialog; use askConfirm / askText`);
        if (["toLocaleDateString", "toLocaleTimeString", "toLocaleString"].includes(node.callee.property?.name) && node.arguments.length === 0)
          report("date", file, at(node), `${node.callee.property.name}() uses the browser's language`);
        break;
      }
      case "NewExpression":
        if (node.callee.type === "MemberExpression" && node.callee.object?.name === "Intl" && (node.arguments.length === 0 || node.arguments[0].type === "Identifier" && node.arguments[0].name === "undefined"))
          report("date", file, at(node), `new Intl.${node.callee.property.name}() without a language`);
        if (node.callee.name === "ApiError" || node.callee.name === "Error") {
          const arg = node.arguments[0];
          if (node.callee.name === "ApiError" && arg?.type === "Literal" && userFacing(String(arg.value))) report("string", file, at(arg), `new ApiError(${JSON.stringify(arg.value)})`);
        }
        break;
      case "Literal":
        // A sentence anywhere outside t(): it ends up on screen sooner or later (run(fn, "Saved."),
        // a label table, a default prop). Console output and thrown programmer errors are exempt.
        if (!insideT && typeof node.value === "string" && /^[A-ZÀ-ÿƏÜÖĞİŞÇА-Я][^\n]*\s[^\n]*[.!?…]$/u.test(node.value) && parent?.type !== "JSXAttribute"
            && !(parent?.type === "CallExpression" && parent.callee.object?.name === "console")
            && !(parent?.type === "NewExpression" && parent.callee.name === "Error"))
          report("sentence", file, at(node), JSON.stringify(node.value.slice(0, 90)));
        break;
      case "JSXText":
        if (!insideT && userFacing(node.value.trim())) report("text", file, at(node), JSON.stringify(node.value.trim().slice(0, 80)));
        break;
      case "JSXAttribute": {
        const name = node.name.type === "JSXIdentifier" ? node.name.name : "";
        if (TEXT_ATTRS.has(name) && !(name === "label" && /Boundary|Map$/.test(parent?.name?.name ?? "")) && node.value?.type === "Literal" && userFacing(node.value.value)) report("text", file, at(node), `${name}="${node.value.value}"`);
        break;
      }
      case "JSXOpeningElement": {
        if (node.name.name === "input") {
          const type = node.attributes.find((a) => a.name?.name === "type");
          const hidden = node.attributes.some((a) => a.name?.name === "className" && /\b(hidden|sr-only)\b/.test(a.value?.value ?? ""));
          if (type?.value?.type === "Literal" && NATIVE.has(type.value.value) && !hidden)
            report("native", file, at(node), `<input type="${type.value.value}"> shows the browser's own wording`);
        }
        break;
      }
      case "JSXExpressionContainer": {
        // {"A sentence"} or {cond ? "Yes" : "No"} directly in JSX.
        const lits = [];
        const collect = (e) => {
          if (!e) return;
          if (e.type === "Literal" && typeof e.value === "string") lits.push(e);
          else if (e.type === "ConditionalExpression") { collect(e.consequent); collect(e.alternate); }
          else if (e.type === "LogicalExpression") collect(e.right);
          else if (e.type === "TemplateLiteral") for (const q of e.quasis) if (userFacing(q.value.cooked ?? "")) lits.push({ value: q.value.cooked, start: q.start });
        };
        if (parent?.type === "JSXElement" || parent?.type === "JSXFragment") collect(node.expression);
        if (parent?.type === "JSXAttribute" && TEXT_ATTRS.has(parent.name?.name)) collect(node.expression);
        for (const l of lits) if (!insideT && SENTENCE.test(l.value) || (l.value && userFacing(l.value) && /^[A-Z]/.test(l.value) && !insideT && WORDS.test(l.value) && l.value.length > 3 && !IGNORE.test(l.value)))
          report("string", file, at(l), JSON.stringify(String(l.value).slice(0, 80)));
        break;
      }
    }
    for (const key of Object.keys(node)) {
      if (key === "parent") continue;
      const child = node[key];
      if (Array.isArray(child)) for (const c of child) visit(c, node, insideT);
      else if (child && typeof child === "object" && typeof child.type === "string") visit(child, node, insideT);
    }
  };
  visit(ast, null, false);
}

// ------------------------------------------------------------------ server messages
const serverMessages = readFileSync(join(root, "client/src/lib/serverMessages.ts"), "utf8");
const known = [...serverMessages.matchAll(/^\s*"((?:[^"\\]|\\.)+)":\s*\[/gm)].map((m) => m[1].replace(/\\"/g, '"'));
const patterns = known.map((k) => new RegExp("^" + k.replace(/[.*+?^$()|[\]\\]/g, "\\$&").replace(/\\\{\d\\\}|\{\d\}/g, ".+") + "$"));
const translatable = (msg) => known.includes(msg) || patterns.some((p) => p.test(msg));
const MSG = /(?:Error\.(?:Validation|Conflict|Forbidden|Unauthorized|Failure)\(\s*|WithMessage\(\s*|\bmessage\s*=\s*|Problem\(\s*(?:detail:\s*)?|title:\s*)\$?"((?:[^"\\]|\\.){6,})"/g;
for (const file of walk(join(root, "src"), [".cs"])) {
  if (/Pages[\\/]InfoPages|Pages[\\/]Info\.cshtml\.cs|Pages[\\/]Privacy|DemoPeople|Bootstrapper|Seed/.test(file)) continue;
  const src = readFileSync(file, "utf8");
  const lines = offsets(src);
  for (const m of src.matchAll(MSG)) {
    // Interpolated parts become {0}, {1} as serverMessages writes them.
    // An interpolation with quotes inside ({string.Join(", ", x)}) cuts the match short: compare
    // what comes before it with the start of a known message.
    const open = m[1].lastIndexOf("{");
    if (open > m[1].lastIndexOf("}")) { const head = m[1].slice(0, open); if (known.some((k) => k.startsWith(head))) continue; }
    let i = 0;
    const msg = m[1].replace(/\{[^}]+\}/g, () => `{${i++}}`);
    if (!/\s/.test(msg) || /^[a-z_.]+$/.test(msg)) continue;
    if (!translatable(msg) && !translatable(msg.replace(/\{\d\}/g, "X")))
      report("server", file, lineOf(lines, m.index), msg);
  }
}

// ------------------------------------------------------------------ razor
for (const file of walk(join(root, "src/MovieRental.Host/Pages"), [".cshtml"])) {
  const src = readFileSync(file, "utf8").replace(/@\*[\s\S]*?\*@/g, (c) => c.replace(/[^\n]/g, " ")).replace(/<script[\s\S]*?<\/script>|<style[\s\S]*?<\/style>/g, (c) => c.replace(/[^\n]/g, " "));
  const lines = offsets(src);
  for (const m of src.matchAll(/>([^<>@{}]*\p{L}{2,}[^<>@{}]*)</gu)) {
    const text = m[1].trim();
    if (!text || IGNORE.test(text) || /^[\s\p{P}\p{S}\d]*$/u.test(text)) continue;
    report("razor", file, lineOf(lines, m.index), JSON.stringify(text.slice(0, 80)));
  }
  for (const m of src.matchAll(/\b(placeholder|title|aria-label|alt)="([^"@]*\p{L}{2,}[^"@]*)"/gu))
    if (!IGNORE.test(m[2])) report("razor", file, lineOf(lines, m.index), `${m[1]}="${m[2]}"`);
}

// ------------------------------------------------------------------ keys the server looks up: T["key"], shell.Create("key", "key", ...)
for (const file of walk(join(root, "src/MovieRental.Host"), [".cshtml", ".cs"])) {
  const src = readFileSync(file, "utf8");
  const lines = offsets(src);
  const keys = [...src.matchAll(/\bT\["([\w.-]+)"\]/g), ...src.matchAll(/\.Create\("([\w.-]+)",\s*"([\w.-]+)"/g)]
    .flatMap((m) => m.slice(1).filter(Boolean).map((k) => [k, m.index]));
  for (const [key, index] of keys) {
    const missing = langs.filter((l) => !(key in locales[l]));
    if (missing.length) report("key", file, lineOf(lines, index), `${key} — missing in ${missing.join(", ")}`);
  }
}

// ------------------------------------------------------------------ keys built at run time: t(`status.${x}`)
// Every value the prefix can take has to exist; list the prefixes with how many keys each has.
for (const file of walk(join(root, "client/src"), [".tsx", ".ts"])) {
  const src = readFileSync(file, "utf8");
  const lines = offsets(src);
  for (const m of src.matchAll(/\bt\(\s*`([\w.-]*)\$\{/g)) {
    const prefix = m[1];
    const have = Object.keys(locales.en).filter((k) => k.startsWith(prefix));
    const uneven = langs.filter((l) => Object.keys(locales[l]).filter((k) => k.startsWith(prefix)).length !== have.length);
    if (!have.length || uneven.length) report("dynamic", file, lineOf(lines, m.index), `t(\`${prefix}\${…}\`) — ${have.length} keys in en${uneven.length ? `, different count in ${uneven.join(", ")}` : ""}`);
  }
}

// ------------------------------------------------------------------ locales: a key in one file but not another
const all = new Set(langs.flatMap((l) => Object.keys(locales[l])));
for (const key of all) {
  const missing = langs.filter((l) => !(key in locales[l]));
  if (missing.length) report("locale", `src/MovieRental.Host/locales/${missing[0]}.json`, 0, `${key} — missing in ${missing.join(", ")}`);
  // Copied across untranslated: identical to English in another language, and a real sentence.
  for (const l of ["az", "ru", "tr"])
    if (locales[l][key] && locales[l][key] === locales.en[key] && SENTENCE.test(locales.en[key]) && !IGNORE.test(locales.en[key]))
      report("locale", `src/MovieRental.Host/locales/${l}.json`, 0, `${key} is still English: ${JSON.stringify(locales.en[key].slice(0, 60))}`);
}

const byKind = Object.groupBy(findings, (f) => f.kind);
for (const [kind, list] of Object.entries(byKind)) {
  console.log(`\n== ${kind} (${list.length})`);
  for (const f of list) console.log(`${f.file}:${f.line}  ${f.detail}`);
}
console.log(`\n${findings.length} finding(s).`);
process.exit(findings.length ? 1 : 0);
