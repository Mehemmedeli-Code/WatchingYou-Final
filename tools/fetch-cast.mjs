// The cast of every film in the catalogue, with the actors' photos, from Wikidata and Wikimedia
// Commons (free to reuse; each photo's source page is listed in cast/SOURCES.md). Writes
// src/MovieRental.Host/seed/movie-cast.json (by film slug) and the photos into
// src/MovieRental.Host/cast/, which the site serves at /api/cast/{file}. Run it again after
// adding films; photos already downloaded are kept.
//
//   node tools/fetch-cast.mjs https://localhost:7139
import fs from "node:fs";
import path from "node:path";

process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";
const SITE = process.argv[2] ?? "https://localhost:7139";
const HOST = path.join(import.meta.dirname, "..", "src", "MovieRental.Host");
const PHOTOS = path.join(HOST, "cast");
const OUT = path.join(HOST, "seed", "movie-cast.json");
const PER_FILM = 8;
const UA = { "User-Agent": "WatchingYou-cast/1.0 (student project; https://github.com/Mehemmedeli-Code/WatchingYou-Final)" };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function json(url, tries = 4) {
  for (let i = 0; ; i++) {
    const r = await fetch(url, { headers: { ...UA, Accept: "application/json" } });
    if (r.ok) return r.json();
    if (i >= tries || (r.status !== 429 && r.status < 500)) throw new Error(`${r.status} ${url.slice(0, 120)}`);
    await sleep(3000 * (i + 1));
  }
}
const sparql = (q) => json("https://query.wikidata.org/sparql?format=json&query=" + encodeURIComponent(q));
const entities = (ids, props = "labels|claims") =>
  json(`https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&languages=en|mul|az|ru|tr&props=${props}&ids=${ids.join("|")}`);
// The English name, which Wikidata now often keeps as the "mul" (every language) label instead.
const label = (e) => e?.labels?.en?.value ?? e?.labels?.mul?.value ?? null;
// The name in the site's other languages, where Wikidata spells it differently (Эндрю Гарфилд).
const others = (e) => {
  const en = label(e);
  const out = Object.fromEntries(["az", "ru", "tr"].map((l) => [l, e?.labels?.[l]?.value]).filter(([, v]) => v && v !== en));
  return Object.keys(out).length ? out : undefined;
};

// The films, from the site itself (slug, title, year).
const films = [];
for (let page = 1; ; page++) {
  const r = await fetch(`${SITE}/api/movies?pageSize=60&page=${page}`).then((x) => x.json());
  films.push(...r.items);
  if (films.length >= r.totalCount || r.items.length === 0) break;
}

const book = fs.existsSync(OUT) ? JSON.parse(fs.readFileSync(OUT, "utf8")) : {};
const sources = new Map();
fs.mkdirSync(PHOTOS, { recursive: true });

for (const film of films) {
  if (book[film.slug]) continue;   // done on an earlier run
  // The film: a work with this English title (or one of its other names), released within a
  // year of ours, with actors, or voice actors for an animation.
  const title = JSON.stringify(film.title);
  const found = await sparql(`SELECT ?f WHERE { ?f rdfs:label|skos:altLabel ${title}@en; wdt:P577 ?d.
    { ?f wdt:P161 ?any } UNION { ?f wdt:P725 ?any }
    FILTER(ABS(YEAR(?d) - ${film.releaseYear}) <= 1) } LIMIT 1`).catch(() => null);
  let qid = found?.results.bindings[0]?.f.value.split("/").pop();
  // The query service misses some titles; the plain search finds them by their description
  // ("1994 film directed by …").
  if (!qid) {
    const hits = (await json(`https://www.wikidata.org/w/api.php?action=wbsearchentities&format=json&language=en&type=item&limit=10&search=${encodeURIComponent(film.title)}`)).search ?? [];
    qid = hits.find((h) => new RegExp(`\\b(${film.releaseYear}|${film.releaseYear - 1}|${film.releaseYear + 1})\\b.*\\bfilm\\b`, "i").test(h.description ?? ""))?.id;
  }
  if (!qid) { console.log(`-  ${film.title} (${film.releaseYear}): not found`); continue; }

  // Cast members in the order Wikidata lists them, which is usually the billing.
  const all = (await entities([qid], "claims")).entities[qid].claims;
  const claims = all.P161?.length ? all.P161 : all.P725 ?? [];
  const cast = claims.slice(0, PER_FILM).map((c) => ({
    id: c.mainsnak.datavalue?.value.id,
    role: c.qualifiers?.P4633?.[0]?.datavalue?.value ?? null,
    roleId: c.qualifiers?.P453?.[0]?.datavalue?.value.id ?? null,
  })).filter((c) => c.id);
  const people = (await entities([...new Set(cast.flatMap((c) => [c.id, c.roleId].filter(Boolean)))])).entities;

  const list = [];
  for (const c of cast) {
    const person = people[c.id];
    const image = person?.claims?.P18?.[0]?.mainsnak.datavalue?.value;
    let photo = null;
    if (image) {
      photo = `${c.id.toLowerCase()}.jpg`;
      const file = path.join(PHOTOS, photo);
      if (!fs.existsSync(file)) {
        const r = await fetch(`https://commons.wikimedia.org/wiki/Special:FilePath/${encodeURIComponent(image)}?width=200`, { headers: UA });
        const type = r.headers.get("content-type") ?? "";
        if (r.ok && type.startsWith("image/jpeg")) fs.writeFileSync(file, Buffer.from(await r.arrayBuffer()));
        else photo = null;   // a PNG, an SVG or an error: the card shows initials instead
        await sleep(300);
      }
      if (photo) sources.set(photo, `https://commons.wikimedia.org/wiki/File:${encodeURIComponent(image.replace(/ /g, "_"))}`);
    }
    const name = label(person);
    const part = c.role ? null : people[c.roleId];
    if (name) list.push({ name, names: others(person), role: c.role ?? label(part) ?? null, roles: part ? others(part) : undefined, photo });
  }
  if (list.length) book[film.slug] = list;
  console.log(`${list.length ? "ok" : "- "} ${film.title} (${film.releaseYear}): ${list.length} actors, ${list.filter((a) => a.photo).length} photos`);
  fs.writeFileSync(OUT, JSON.stringify(book, null, 1) + "\n");
  await sleep(500);
}

// Where every photo came from (the licence and author are on that page).
const credits = path.join(PHOTOS, "SOURCES.md");
const old = fs.existsSync(credits) ? fs.readFileSync(credits, "utf8").split("\n").filter((l) => l.startsWith("- ")) : [];
const lines = new Map(old.map((l) => [l.slice(2).split(" ")[0], l]));
for (const [photo, page] of sources) lines.set(photo, `- ${photo} ${page}`);
fs.writeFileSync(credits, "# Actor photos\n\nFrom Wikimedia Commons; each file's licence and author are on its page.\n\n"
  + [...lines.values()].sort().join("\n") + "\n");
console.log(`${Object.keys(book).length} of ${films.length} films have a cast.`);
